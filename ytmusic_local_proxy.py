"""
YTMusic Local Proxy Server
===========================
Chạy trên PC (cùng WiFi với điện thoại WP8.1).
yt-dlp extract audio URL → proxy stream về phone.

Cách dùng:
  python ytmusic_local_proxy.py

Phone kết nối:
  http://192.168.x.x:5000/stream?v=VIDEO_ID&key=LumiaWP81-An
"""

import os
import re
import time
import hashlib
import threading
from flask import Flask, request, jsonify, Response, stream_with_context
import requests as req_lib
import yt_dlp
from yt_dlp.networking.impersonate import ImpersonateTarget

app = Flask(__name__)

SECRET_KEY = "LumiaWP81-An"

# Cache URL đã extract (tránh extract lại)
_url_cache = {}  # {video_id: (url, timestamp)}
_URL_CACHE_TTL = 300  # 5 phút

def is_valid_video_id(vid):
    return bool(vid and re.match(r'^[a-zA-Z0-9_-]{11}$', vid))

def extract_audio_url(video_id):
    """yt-dlp extract audio URL — Chrome impersonation, KHÔNG cần proxy."""
    # Check cache
    if video_id in _url_cache:
        url, ts = _url_cache[video_id]
        if time.time() - ts < _URL_CACHE_TTL:
            print(f"[CACHE HIT] {video_id}")
            return url
    
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
    
    print(f"[EXTRACT] {video_id}: starting...")
    with yt_dlp.YoutubeDL(opts) as ydl:
        info = ydl.extract_info(youtube_url, download=False)
    
    # Chọn audio URL tốt nhất
    formats = info.get('formats', [])
    audio_url = None
    
    # itag 140 (m4a 128kbps)
    for f in formats:
        if f.get('format_id') == '140' and f.get('url'):
            audio_url = f['url']
            break
    
    # itag 139 (m4a 48kbps)
    if not audio_url:
        for f in formats:
            if f.get('format_id') == '139' and f.get('url'):
                audio_url = f['url']
                break
    
    # Any audio-only m4a
    if not audio_url:
        for f in formats:
            if (f.get('acodec', 'none') != 'none' and 
                f.get('vcodec', 'none') == 'none' and 
                f.get('ext') == 'm4a' and f.get('url')):
                audio_url = f['url']
                break
    
    # Any audio
    if not audio_url:
        for f in reversed(formats):
            if f.get('acodec', 'none') != 'none' and f.get('url'):
                audio_url = f['url']
                break
    
    if not audio_url:
        raise Exception(f"No audio URL in {len(formats)} formats")
    
    # Cache
    _url_cache[video_id] = (audio_url, time.time())
    print(f"[EXTRACT] {video_id}: OK!")
    return audio_url

# === ROUTES ===

@app.route('/')
def index():
    return jsonify({
        "service": "YTMusic Local Proxy",
        "version": "1.0",
        "yt_dlp": yt_dlp.version.__version__,
        "endpoints": {
            "/stream?v=VIDEO_ID&key=KEY": "Stream audio (proxy)",
            "/url?v=VIDEO_ID&key=KEY": "Get audio URL (JSON)",
        }
    })

@app.route('/stream')
def stream_audio():
    """Extract + proxy stream audio. Phone gọi endpoint này."""
    if request.args.get('key') != SECRET_KEY:
        return "Unauthorized", 403
    video_id = request.args.get('v')
    if not is_valid_video_id(video_id):
        return "Invalid Video ID", 400
    
    try:
        audio_url = extract_audio_url(video_id)
        
        # Proxy stream về phone
        headers = {
            'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36',
        }
        range_header = request.headers.get('Range')
        if range_header:
            headers['Range'] = range_header
        
        audio_resp = req_lib.get(audio_url, headers=headers, stream=True, timeout=30)
        
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
        print(f"[ERROR] {video_id}: {e}")
        return jsonify({"error": str(e)}), 500

@app.route('/url')
def get_url():
    """Trả về audio URL (JSON) — dùng cho debug."""
    if request.args.get('key') != SECRET_KEY:
        return "Unauthorized", 403
    video_id = request.args.get('v')
    if not is_valid_video_id(video_id):
        return "Invalid Video ID", 400
    
    try:
        audio_url = extract_audio_url(video_id)
        return jsonify({"url": audio_url, "videoId": video_id})
    except Exception as e:
        return jsonify({"error": str(e)}), 500

if __name__ == '__main__':
    import socket
    
    # Lấy local IP
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        s.connect(('8.8.8.8', 80))
        local_ip = s.getsockname()[0]
    except:
        local_ip = '127.0.0.1'
    finally:
        s.close()
    
    print("=" * 50)
    print("  YTMusic Local Proxy Server")
    print("=" * 50)
    print(f"  yt-dlp: {yt_dlp.version.__version__}")
    print(f"  Local IP: {local_ip}")
    print(f"  URL: http://{local_ip}:5000")
    print()
    print("  Phone stream URL:")
    print(f"  http://{local_ip}:5000/stream?v=VIDEO_ID&key={SECRET_KEY}")
    print("=" * 50)
    
    app.run(host='0.0.0.0', port=5000, debug=False, threaded=True)
