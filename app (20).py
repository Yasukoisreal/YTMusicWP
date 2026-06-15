import os
import re
import traceback
import requests
from flask import Flask, request, jsonify, send_file, Response, stream_with_context

import yt_dlp
from yt_dlp.networking.impersonate import ImpersonateTarget
from ytmusicapi import YTMusic  # pyright: ignore[reportMissingImports]

app = Flask(__name__)

# ==========================================
# CẤU HÌNH
# ==========================================
SECRET_KEY = os.environ.get("APP_SECRET_KEY", "LumiaWP81-An")
DOWNLOAD_DIR = "/tmp/ytmusic"
MAX_CACHE_FILES = 50

if not os.path.exists(DOWNLOAD_DIR):
    os.makedirs(DOWNLOAD_DIR)

ytmusic = YTMusic()

print(f"[STARTUP] yt-dlp: {yt_dlp.version.__version__}")
print(f"[STARTUP] cookies.txt: {'FOUND' if os.path.exists('cookies.txt') else 'NOT FOUND'}")

# ==========================================
# HÀM BỔ TRỢ
# ==========================================
def cleanup_cache():
    try:
        files = [os.path.join(DOWNLOAD_DIR, f) for f in os.listdir(DOWNLOAD_DIR) if f.endswith('.m4a')]
        if len(files) > MAX_CACHE_FILES:
            files.sort(key=os.path.getmtime)
            for f in files[:-MAX_CACHE_FILES]:
                os.remove(f)
    except Exception:
        pass

def is_valid_video_id(video_id):
    return bool(video_id and re.match(r'^[a-zA-Z0-9_-]{11}$', video_id))

def _pick_best_audio_url(info):
    """Chọn URL audio tốt nhất. Ưu tiên: itag 140 > 139 > m4a > audio bất kỳ."""
    if not info:
        return None
    if info.get('url') and info.get('acodec', 'none') != 'none':
        return info['url']

    formats = info.get('formats', [])
    if not formats:
        rd = info.get('requested_downloads', [])
        if rd and rd[0].get('url'):
            return rd[0]['url']
        return info.get('url')

    for itag in ['140', '139']:
        for fmt in formats:
            if fmt.get('format_id') == itag and fmt.get('url'):
                return fmt['url']

    for fmt in formats:
        if (fmt.get('acodec', 'none') != 'none' and
            fmt.get('vcodec', 'none') == 'none' and
            fmt.get('ext') == 'm4a' and fmt.get('url')):
            return fmt['url']

    for fmt in reversed(formats):
        if (fmt.get('acodec', 'none') != 'none' and
            fmt.get('vcodec', 'none') == 'none' and
            fmt.get('url')):
            return fmt['url']

    return info.get('url')

# ==========================================
# PARSE SEARCH RESULTS
# ==========================================
def _clean_channel_name(name):
    if not name:
        return name
    if name.endswith(' - Topic'):
        return name[:-8]
    if name.endswith(' - Chủ đề'):
        return name[:-9]
    return name

def _get_best_thumbnail(thumbnails):
    if not thumbnails:
        return ''
    return thumbnails[-1].get('url', '')

def _parse_search_item(item):
    rtype = item.get('resultType')
    if rtype not in ('song', 'video', 'artist', 'playlist'):
        return None

    vid_id = item.get('videoId') or item.get('browseId')
    if not vid_id:
        return None

    title = item.get('title') or item.get('artist') or ''
    thumb_url = _get_best_thumbnail(item.get('thumbnails', []))

    result = {
        "videoId": vid_id,
        "title": title,
        "type": rtype,
        "thumbnailUrl": thumb_url
    }

    if rtype in ('song', 'video'):
        artists = item.get('artists', [])
        if artists and isinstance(artists, list) and len(artists) > 0:
            result["channelName"] = _clean_channel_name(artists[0].get('name', 'Unknown Artist'))
            result["channelId"] = artists[0].get('id', '')
        else:
            result["channelName"] = _clean_channel_name(item.get('artist', 'Unknown Artist'))
            result["channelId"] = ''
        duration = item.get('duration')
        if duration:
            result["duration"] = duration
    elif rtype == 'playlist':
        result["videoId"] = "PLAYLIST:" + vid_id
        result["channelName"] = f"Playlist · {item.get('author', 'Unknown')}"
    elif rtype == 'artist':
        result["videoId"] = "CHANNEL:" + vid_id
        result["channelName"] = "Artist"

    return result

# ==========================================
# ENDPOINTS
# ==========================================
@app.route('/')
def home():
    return (f"🚀 YTMusicWP Backend v4.0<br>"
            f"yt-dlp: {yt_dlp.version.__version__}<br>"
            f"Endpoints: /api/search, /api/ytdlp-stream, /api/download, /api/lyrics, /api/playlist, /api/artist")

# --- SEARCH ---
@app.route('/api/search')
def search_music():
    query = request.args.get('q')
    if not query:
        return jsonify([])
    try:
        results = ytmusic.search(query, limit=30)
        clean_results = []
        for item in results:
            parsed = _parse_search_item(item)
            if parsed:
                clean_results.append(parsed)
            if len(clean_results) >= 20:
                break
        return jsonify(clean_results)
    except Exception as e:
        print(f"Search error: {e}")
        return jsonify([])

# --- YT-DLP STREAM (extract + proxy stream) ---
@app.route('/api/ytdlp-stream')
def ytdlp_stream():
    """yt-dlp extract URL + proxy stream audio. Dùng Chrome impersonation."""
    if request.args.get("key") != SECRET_KEY:
        return "Unauthorized", 403
    video_id = request.args.get('v')
    if not is_valid_video_id(video_id):
        return "Invalid Video ID", 400

    try:
        youtube_url = f"https://www.youtube.com/watch?v={video_id}"
        opts = {
            'noplaylist': True,
            'quiet': True,
            'no_warnings': True,
            'socket_timeout': 15,
            'retries': 2,
            'ignore_no_formats_error': True,
            'impersonate': ImpersonateTarget(client='chrome'),
        }
        if os.path.exists('cookies.txt'):
            opts['cookiefile'] = 'cookies.txt'

        print(f"[YTDLP-STREAM] {video_id}: extracting...")
        with yt_dlp.YoutubeDL(opts) as ydl:
            info = ydl.extract_info(youtube_url, download=False)

        audio_url = _pick_best_audio_url(info)
        if not audio_url:
            return jsonify({"error": "No audio URL found", "formats": len(info.get('formats', []))}), 404

        print(f"[YTDLP-STREAM] {video_id}: OK, streaming...")

        fetch_headers = {
            'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131.0.0.0 Safari/537.36',
        }
        range_header = request.headers.get('Range')
        if range_header:
            fetch_headers['Range'] = range_header

        audio_resp = requests.get(audio_url, headers=fetch_headers, stream=True, timeout=30)

        resp_headers = {
            'Content-Type': 'audio/mp4',
            'Accept-Ranges': 'bytes',
            'Access-Control-Allow-Origin': '*',
        }
        if audio_resp.headers.get('Content-Length'):
            resp_headers['Content-Length'] = audio_resp.headers['Content-Length']
        if audio_resp.headers.get('Content-Range'):
            resp_headers['Content-Range'] = audio_resp.headers['Content-Range']

        def generate():
            for chunk in audio_resp.iter_content(chunk_size=65536):
                yield chunk

        return Response(stream_with_context(generate()),
                       status=audio_resp.status_code,
                       headers=resp_headers)

    except Exception as e:
        print(f"[YTDLP-STREAM ERROR] {video_id}: {e}")
        traceback.print_exc()
        return jsonify({"error": str(e)}), 500

# --- DOWNLOAD (tải file m4a) ---
@app.route('/api/download')
def download_audio():
    if request.args.get("key") != SECRET_KEY:
        return "Unauthorized", 403
    video_id = request.args.get('v')
    if not is_valid_video_id(video_id):
        return "Invalid Video ID", 400
    try:
        file_path = os.path.join(DOWNLOAD_DIR, f"{video_id}.m4a")

        if not (os.path.exists(file_path) and os.path.getsize(file_path) > 0):
            cleanup_cache()
            youtube_url = f"https://www.youtube.com/watch?v={video_id}"
            opts = {
                'noplaylist': True, 'quiet': True, 'no_warnings': True,
                'socket_timeout': 15, 'retries': 2,
                'impersonate': ImpersonateTarget(client='chrome'),
            }
            if os.path.exists('cookies.txt'):
                opts['cookiefile'] = 'cookies.txt'

            with yt_dlp.YoutubeDL(opts) as ydl:
                info = ydl.extract_info(youtube_url, download=False)
            audio_url = _pick_best_audio_url(info)
            if not audio_url:
                return "No audio URL found", 404

            resp = requests.get(audio_url, stream=True, timeout=30)
            with open(file_path, 'wb') as f:
                for chunk in resp.iter_content(chunk_size=65536):
                    f.write(chunk)

        return send_file(file_path, mimetype="audio/mp4", as_attachment=True, download_name=f"{video_id}.m4a")
    except Exception as e:
        return f"Error: {str(e)}", 500

# --- LYRICS ---
@app.route('/api/lyrics')
def get_lyrics():
    """Proxy lrclib.net cho WP8.1 (không kết nối được TLS 1.3)."""
    query = request.args.get('q', '')
    track_name = request.args.get('track', '')
    artist_name = request.args.get('artist', '')

    synced = None
    plain = None

    try:
        if track_name and artist_name:
            url = f"https://lrclib.net/api/search?track_name={requests.utils.quote(track_name)}&artist_name={requests.utils.quote(artist_name)}"
            resp = requests.get(url, timeout=8)
            if resp.ok:
                data = resp.json()
                if data and len(data) > 0:
                    synced = data[0].get('syncedLyrics')
                    plain = data[0].get('plainLyrics')

        if not synced and not plain and query:
            url = f"https://lrclib.net/api/search?q={requests.utils.quote(query)}"
            resp = requests.get(url, timeout=8)
            if resp.ok:
                data = resp.json()
                if data and len(data) > 0:
                    synced = data[0].get('syncedLyrics')
                    plain = data[0].get('plainLyrics')
    except Exception as e:
        print(f"Lyrics error: {e}")

    return jsonify({"syncedLyrics": synced, "plainLyrics": plain})

# --- PLAYLIST ---
@app.route('/api/playlist')
def get_playlist():
    playlist_id = request.args.get('id', '')
    if not playlist_id:
        return jsonify({"error": "Missing playlist ID"}), 400

    try:
        playlist = ytmusic.get_playlist(playlist_id, limit=100)
        tracks = []
        thumbnail = ''

        if playlist:
            pl_thumbs = playlist.get('thumbnails', [])
            if pl_thumbs:
                thumbnail = pl_thumbs[-1].get('url', '')

            for item in playlist.get('tracks', []):
                vid_id = item.get('videoId')
                if not vid_id:
                    continue
                title = item.get('title', '')
                artists = item.get('artists', [])
                channel_name = ''
                if artists and isinstance(artists, list) and len(artists) > 0:
                    channel_name = _clean_channel_name(artists[0].get('name', ''))
                thumbs = item.get('thumbnails', [])
                thumb_url = thumbs[-1].get('url', '') if thumbs else ''
                tracks.append({
                    "videoId": vid_id, "title": title,
                    "channelName": channel_name, "thumbnailUrl": thumb_url
                })

        return jsonify({
            "title": playlist.get('title', ''),
            "thumbnail": thumbnail,
            "tracks": tracks
        })
    except Exception as e:
        print(f"Playlist error: {e}")
        return jsonify({"error": str(e)}), 500

# --- ARTIST ---
@app.route('/api/artist')
def get_artist():
    channel_id = request.args.get('id', '')
    if not channel_id:
        return jsonify({"error": "Missing artist/channel ID"}), 400

    try:
        artist = ytmusic.get_artist(channel_id)
        result = {"name": artist.get('name', ''), "avatar": '', "cover": '', "tracks": []}

        artist_thumbs = artist.get('thumbnails', [])
        if artist_thumbs:
            result["avatar"] = artist_thumbs[-1].get('url', '')

        header_thumbs = artist.get('header', artist).get('thumbnails', artist_thumbs)
        if header_thumbs and header_thumbs != artist_thumbs:
            result["cover"] = header_thumbs[-1].get('url', '')

        songs_section = artist.get('songs', {})
        if isinstance(songs_section, dict):
            song_results = songs_section.get('results', [])
        elif isinstance(songs_section, list):
            song_results = songs_section
        else:
            song_results = []

        for item in song_results:
            vid_id = item.get('videoId')
            if not vid_id:
                continue
            title = item.get('title', '')
            artists_list = item.get('artists', [])
            channel_name = ''
            if artists_list and isinstance(artists_list, list) and len(artists_list) > 0:
                channel_name = _clean_channel_name(artists_list[0].get('name', ''))
            thumbs = item.get('thumbnails', [])
            thumb_url = thumbs[-1].get('url', '') if thumbs else ''
            result["tracks"].append({
                "videoId": vid_id, "title": title,
                "channelName": channel_name, "thumbnailUrl": thumb_url
            })

        if not result["tracks"]:
            artist_name = result["name"] or channel_id
            search_results = ytmusic.search(artist_name, filter='songs', limit=20)
            for item in search_results:
                parsed = _parse_search_item(item)
                if parsed and parsed.get('type') in ('song', 'video'):
                    result["tracks"].append(parsed)

        return jsonify(result)
    except Exception as e:
        print(f"Artist error: {e}")
        try:
            search_results = ytmusic.search(channel_id, limit=20)
            tracks = [_parse_search_item(i) for i in search_results if _parse_search_item(i)]
            return jsonify({"name": "", "avatar": "", "cover": "", "tracks": tracks})
        except Exception:
            return jsonify({"error": str(e)}), 500

if __name__ == '__main__':
    port = int(os.environ.get("PORT", 8080))
    app.run(host='0.0.0.0', port=port, threaded=True)
