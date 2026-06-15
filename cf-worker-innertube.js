export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    // === FETCH PROXY — Phone resolves URL, CF Worker proxies stream ===
    if (url.pathname === '/fetch') {
      const key = url.searchParams.get('key');
      if (key !== 'LumiaWP81-An') return new Response('Unauthorized', { status: 403 });
      
      const targetUrl = url.searchParams.get('url');
      if (!targetUrl || !targetUrl.startsWith('http')) {
        return new Response('Missing url param', { status: 400 });
      }

      try {
        const fetchHeaders = {
          'User-Agent': 'com.google.android.apps.youtube.vr.oculus/1.60.19 gzip',
        };
        const rangeHeader = request.headers.get('Range');
        if (rangeHeader) fetchHeaders['Range'] = rangeHeader;

        const resp = await fetch(targetUrl, { headers: fetchHeaders });

        const rh = new Headers();
        rh.set('Content-Type', resp.headers.get('Content-Type') || 'audio/mp4');
        rh.set('Accept-Ranges', 'bytes');
        rh.set('Access-Control-Allow-Origin', '*');
        if (resp.headers.get('Content-Length')) rh.set('Content-Length', resp.headers.get('Content-Length'));
        if (resp.headers.get('Content-Range')) rh.set('Content-Range', resp.headers.get('Content-Range'));

        return new Response(resp.body, { status: resp.status, headers: rh });
      } catch (e) {
        return new Response(JSON.stringify({ error: e.message }), { 
          status: 502, headers: { 'Content-Type': 'application/json' }
        });
      }
    }

    // === STREAM AUDIO — CF Worker self-resolve via InnerTube + proxy ===
    if (url.pathname === '/stream') {
      const videoId = url.searchParams.get('v');
      const key = url.searchParams.get('key');
      if (key !== 'LumiaWP81-An' || !videoId) {
        return new Response('Bad Request', { status: 400 });
      }

      try {
        // Get cookies from env vars (set via wrangler secret)
        const ytCookies = env.YT_COOKIES || '';
        const ytSapisid = env.YT_SAPISID || '';

        // Step 1: Get visitorData from youtube.com homepage
        let visitorData = null;
        try {
          const vdHeaders = {
            'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36'
          };
          if (ytCookies) vdHeaders['Cookie'] = ytCookies;
          
          const vdResp = await fetch('https://www.youtube.com/', { headers: vdHeaders });
          if (vdResp.ok) {
            const html = await vdResp.text();
            const marker = 'visitorData":"';
            const idx = html.indexOf(marker);
            if (idx >= 0) {
              const start = idx + marker.length;
              const end = html.indexOf('"', start);
              if (end > start && end - start >= 20 && end - start < 600) {
                const vd = html.substring(start, end);
                if (vd.startsWith('Cg')) visitorData = vd;
              }
            }
          }
        } catch(e) {}

        // Step 2: InnerTube ANDROID_VR request with cookies + SAPISIDHASH
        const innerBody = {
          contentCheckOk: true,
          context: {
            client: {
              clientName: 'ANDROID_VR',
              clientVersion: '1.60.19',
              deviceMake: 'Oculus',
              deviceModel: 'Quest 3',
              osName: 'ANDROID',
              osVersion: '12L',
              platform: 'MOBILE',
              hl: 'en',
              gl: 'US'
            }
          },
          videoId: videoId
        };
        if (visitorData) innerBody.context.client.visitorData = visitorData;

        const innerHeaders = {
          'Content-Type': 'application/json',
          'User-Agent': 'com.google.android.apps.youtube.vr.oculus/1.60.19 (Linux; U; Android 12L; eureka-user Build/SQ3A.220605.009.A1) gzip',
          'X-YouTube-Client-Name': '28',
          'X-YouTube-Client-Version': '1.60.19',
          'Origin': 'https://www.youtube.com',
        };
        
        // Add cookies + SAPISIDHASH auth
        if (ytCookies) innerHeaders['Cookie'] = ytCookies;
        if (ytSapisid) {
          const ts = Math.floor(Date.now() / 1000);
          const hashInput = `${ts} ${ytSapisid} https://www.youtube.com`;
          // SHA-1 hash
          const encoder = new TextEncoder();
          const data = encoder.encode(hashInput);
          const hashBuffer = await crypto.subtle.digest('SHA-1', data);
          const hashArray = Array.from(new Uint8Array(hashBuffer));
          const hashHex = hashArray.map(b => b.toString(16).padStart(2, '0')).join('');
          innerHeaders['Authorization'] = `SAPISIDHASH ${ts}_${hashHex}`;
        }

        const innerResp = await fetch('https://www.youtube.com/youtubei/v1/player?key=AIzaSyDSXy9qVx1CzG2S7hYy7G-F6-HQ8_kB4vI&prettyPrint=false', {
          method: 'POST',
          headers: innerHeaders,
          body: JSON.stringify(innerBody)
        });

        const innerJson = await innerResp.json();
        
        // Step 3: Find audio URL (itag 140 → 139 → 18)
        let audioUrl = null;
        const formats = [
          ...(innerJson.streamingData?.adaptiveFormats || []),
          ...(innerJson.streamingData?.formats || [])
        ];
        for (const itag of [140, 139, 18]) {
          const fmt = formats.find(f => f.itag === itag && f.url);
          if (fmt) { audioUrl = fmt.url; break; }
        }

        if (audioUrl) {
          // Step 4: Proxy the audio stream
          const fetchHeaders = {
            'User-Agent': 'com.google.android.apps.youtube.vr.oculus/1.60.19 gzip',
            'Origin': 'https://www.youtube.com',
            'Referer': 'https://www.youtube.com/',
          };
          if (ytCookies) fetchHeaders['Cookie'] = ytCookies;
          const rangeHeader = request.headers.get('Range');
          if (rangeHeader) fetchHeaders['Range'] = rangeHeader;

          const audioResp = await fetch(audioUrl, { headers: fetchHeaders });
          const rh = new Headers();
          rh.set('Content-Type', audioResp.headers.get('Content-Type') || 'audio/mp4');
          rh.set('Accept-Ranges', 'bytes');
          rh.set('Access-Control-Allow-Origin', '*');
          if (audioResp.headers.get('Content-Length')) rh.set('Content-Length', audioResp.headers.get('Content-Length'));
          if (audioResp.headers.get('Content-Range')) rh.set('Content-Range', audioResp.headers.get('Content-Range'));
          return new Response(audioResp.body, { status: audioResp.status, headers: rh });
        }

        // InnerTube failed — return error with debug info
        const status = innerJson.playabilityStatus?.status || 'UNKNOWN';
        const reason = innerJson.playabilityStatus?.reason || '';
        return jsonResponse({ 
          error: 'InnerTube failed', 
          status, reason, 
          visitorData: visitorData ? 'yes' : 'no',
          formatCount: formats.length 
        }, 502);

      } catch (e) {
        // Fallback to Render (if available)
        try {
          const renderUrl = `https://ytproxy-t7r8.onrender.com/api/ytdlp-stream?v=${videoId}&key=${key}`;
          const resp = await fetch(renderUrl, { headers: { 'User-Agent': 'WP81-Audio-Client/1.0' } });
          if (resp.ok) {
            const rh = new Headers();
            rh.set('Content-Type', resp.headers.get('Content-Type') || 'audio/mp4');
            rh.set('Accept-Ranges', 'bytes');
            rh.set('Access-Control-Allow-Origin', '*');
            if (resp.headers.get('Content-Length')) rh.set('Content-Length', resp.headers.get('Content-Length'));
            if (resp.headers.get('Content-Range')) rh.set('Content-Range', resp.headers.get('Content-Range'));
            return new Response(resp.body, { status: resp.status, headers: rh });
          }
        } catch(e2) {}
        return jsonResponse({ error: e.message }, 500);
      }
    }

    // === DEBUG — kiểm tra env vars ===
    if (url.pathname === '/debug') {
      const key = url.searchParams.get('key');
      if (key !== 'LumiaWP81-An') return new Response('Unauthorized', { status: 403 });
      const sapisid = env.YT_SAPISID || '';
      const cookies = env.YT_COOKIES || '';
      // Kiểm tra ký tự ẩn
      const sapisidChars = [];
      for (let i = 0; i < Math.min(sapisid.length, 5); i++) sapisidChars.push(sapisid.charCodeAt(i));
      const lastSapisidChars = [];
      for (let i = Math.max(0, sapisid.length - 3); i < sapisid.length; i++) lastSapisidChars.push(sapisid.charCodeAt(i));
      const lastCookieChars = [];
      for (let i = Math.max(0, cookies.length - 5); i < cookies.length; i++) lastCookieChars.push(cookies.charCodeAt(i));
      
      // Kiểm tra có newline/carriage return không
      const hasNewline = cookies.includes('\n') || cookies.includes('\r');
      const sapisidHasNewline = sapisid.includes('\n') || sapisid.includes('\r');
      
      return jsonResponse({
        sapisidLen: sapisid.length,
        sapisidFirst5: sapisid.substring(0, 5),
        sapisidLast3: sapisid.substring(sapisid.length - 3),
        sapisidFirstChars: sapisidChars,
        sapisidLastChars: lastSapisidChars,
        sapisidHasNewline,
        cookieLen: cookies.length,
        cookieFirst30: cookies.substring(0, 30),
        cookieLast20: cookies.substring(cookies.length - 20),
        cookieLastChars: lastCookieChars,
        cookieHasNewline: hasNewline,
        cookieSemicolonCount: (cookies.match(/; /g) || []).length,
      });
    }

    // === RAW TEST — Minimal InnerTube, no filtering ===
    if (url.pathname === '/rawtest') {
      const videoId = url.searchParams.get('v') || 'pZbu-Bfe6Qc';
      const key = url.searchParams.get('key');
      if (key !== 'LumiaWP81-An') return new Response('Unauthorized', { status: 403 });
      const sapisid = env.YT_SAPISID || '';
      const cookies = env.YT_COOKIES || '';
      const origin = 'https://www.youtube.com';
      const ts = Math.floor(Date.now() / 1000);
      const hb = await crypto.subtle.digest('SHA-1', new TextEncoder().encode(`${ts} ${sapisid} ${origin}`));
      const hx = Array.from(new Uint8Array(hb)).map(b => b.toString(16).padStart(2, '0')).join('');
      const reqBody = JSON.stringify({
        contentCheckOk: true,
        context: { client: {
          clientName: 'ANDROID_VR', clientVersion: '1.60.19',
          deviceMake: 'Oculus', deviceModel: 'Quest 3',
          osName: 'ANDROID', osVersion: '12L', platform: 'MOBILE',
          hl: 'en', gl: 'US',
        }},
        videoId,
      });
      const resp = await fetch('https://www.youtube.com/youtubei/v1/player?prettyPrint=false', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'User-Agent': 'com.google.android.apps.youtube.vr.oculus/1.60.19 (Linux; U; Android 12L; eureka-user Build/SQ3A.220605.009.A1) gzip',
          'X-YouTube-Client-Name': '28', 'X-YouTube-Client-Version': '1.60.19',
          'Origin': origin, 'Authorization': `SAPISIDHASH ${ts}_${hx}`, 'Cookie': cookies,
        },
        body: reqBody,
      });
      const text = await resp.text();
      const data = JSON.parse(text);
      const status = data?.playabilityStatus?.status || 'N/A';
      const reason = data?.playabilityStatus?.reason || '';
      const formats = [...(data?.streamingData?.formats || []), ...(data?.streamingData?.adaptiveFormats || [])];
      let audioUrl = null;
      for (const itag of [140, 139]) {
        const f = formats.find(f => f.itag === itag && f.url);
        if (f) { audioUrl = f.url; break; }
      }
      if (!audioUrl) { const a = formats.find(f => f.url && f.mimeType?.includes('audio')); if (a) audioUrl = a.url; }
      if (!audioUrl) { const m = formats.find(f => f.itag === 18 && f.url); if (m) audioUrl = m.url; }
      
      return jsonResponse({
        httpStatus: resp.status,
        status,
        reason,
        formatCount: formats.length,
        audioUrl: audioUrl ? audioUrl.substring(0, 100) + '...' : null,
        hasUrl: !!audioUrl,
      });
    }

    // === INNERTUBE JSON API (trả URL, dùng cho debug) ===
    if (url.pathname === '/innertube') {
      const videoId = url.searchParams.get('v');
      const key = url.searchParams.get('key');
      if (key !== 'LumiaWP81-An' || !videoId) {
        return new Response('Bad Request', { status: 400 });
      }

      const sapisid = env.YT_SAPISID || '';
      const rawCookies = env.YT_COOKIES || '';
      if (!sapisid || !rawCookies) {
        return jsonResponse({ error: 'Cookies not configured' }, 500);
      }

      // Lọc chỉ giữ cookies auth cần thiết cho InnerTube
      const essentialNames = new Set([
        'SID', 'HSID', 'SSID', 'APISID', 'SAPISID',
        '__Secure-1PAPISID', '__Secure-3PAPISID',
        '__Secure-1PSID', '__Secure-3PSID',
        'LOGIN_INFO',
        '__Secure-1PSIDTS', '__Secure-3PSIDTS',
        'SIDCC', '__Secure-1PSIDCC', '__Secure-3PSIDCC',
      ]);
      const cookies = rawCookies.split('; ')
        .filter(c => essentialNames.has(c.split('=')[0]))
        .join('; ');

      try {
        let visitorData = '';
        try {
          const vdResp = await fetch('https://www.youtube.com/sw.js_data', {
            headers: {
              'Accept': 'application/json',
              'User-Agent': "Mozilla/5.0 (Linux; Andr0id 9; BRAVIA 8K UR2) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/84.0.4147.125 Safari/537.36 OPR/46.0.2207.0 OMI/4.21.0.273.DIA6.149 Model/Sony-BRAVIA-8K-UR2,gzip(gfe)",
              'Cookie': cookies,
            },
          });
          if (vdResp.ok) {
            let text = await vdResp.text();
            if (text.startsWith(")]}'")) text = text.substring(4);
            const arr = JSON.parse(text);
            const vd = arr?.[0]?.[2]?.[0]?.[0]?.[13];
            if (typeof vd === 'string') visitorData = vd;
          }
        } catch (e) { /* ignore */ }

        const origin = 'https://www.youtube.com';
        const timestamp = Math.floor(Date.now() / 1000);
        const hashInput = `${timestamp} ${sapisid} ${origin}`;
        const hashBuffer = await crypto.subtle.digest('SHA-1', new TextEncoder().encode(hashInput));
        const hashHex = Array.from(new Uint8Array(hashBuffer)).map(b => b.toString(16).padStart(2, '0')).join('');

        const playerResp = await fetch('https://www.youtube.com/youtubei/v1/player?prettyPrint=false', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'User-Agent': 'com.google.android.apps.youtube.vr.oculus/1.60.19 (Linux; U; Android 12L; eureka-user Build/SQ3A.220605.009.A1) gzip',
            'X-YouTube-Client-Name': '28', 'X-YouTube-Client-Version': '1.60.19',
            'Origin': origin, 'Authorization': `SAPISIDHASH ${timestamp}_${hashHex}`, 'Cookie': cookies,
          },
          body: JSON.stringify({
            contentCheckOk: true,
            context: { client: {
              clientName: 'ANDROID_VR', clientVersion: '1.60.19',
              deviceMake: 'Oculus', deviceModel: 'Quest 3',
              osName: 'ANDROID', osVersion: '12L', platform: 'MOBILE',
              hl: 'en', gl: 'US',
              // visitorData tạm thời bỏ để test
            }},
            videoId,
          }),
        });
        const data = await playerResp.json();
        const playability = data?.playabilityStatus || {};
        const formats = [...(data?.streamingData?.formats || []), ...(data?.streamingData?.adaptiveFormats || [])];
        
        // Thử extract URL dù status không phải OK
        let audioUrl = null;
        for (const itag of [140, 139]) {
          const f = formats.find(f => f.itag === itag && f.url);
          if (f) { audioUrl = f.url; break; }
        }
        if (!audioUrl) { const a = formats.find(f => f.url && f.mimeType?.includes('audio')); if (a) audioUrl = a.url; }
        if (!audioUrl) { const m = formats.find(f => f.itag === 18 && f.url); if (m) audioUrl = m.url; }
        
        if (audioUrl) return jsonResponse({ url: audioUrl, status: playability.status });
        
        // Dump raw response keys cho debug
        return jsonResponse({
          error: 'Not playable',
          rawKeys: Object.keys(data || {}),
          youtubeError: data?.error,
          playabilityStatus: playability,
          hasStreamingData: !!data?.streamingData,
          formatCount: formats.length,
          videoDetails: data?.videoDetails ? { title: data.videoDetails.title, videoId: data.videoDetails.videoId } : null,
          playerHttpStatus: playerResp.status,
        }, 403);
      } catch (e) {
        return jsonResponse({ error: e.message }, 500);
      }
    }

    // === FETCH PROXY ===
    if (url.pathname === '/fetch') {
      const targetUrl = url.searchParams.get('url');
      const key = url.searchParams.get('key');
      if (key !== 'LumiaWP81-An' || !targetUrl) {
        return new Response('Bad Request', { status: 400 });
      }
      try {
        const headers = {
          'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36',
          'Accept-Language': 'en-US,en;q=0.9',
        };
        const cookieHeader = request.headers.get('X-YT-Cookies');
        if (cookieHeader) headers['Cookie'] = cookieHeader;
        const resp = await fetch(targetUrl, {
          method: request.method === 'POST' ? 'POST' : 'GET',
          headers, body: request.method === 'POST' ? await request.text() : undefined,
          redirect: 'follow',
        });
        const body = await resp.text();
        return new Response(body, {
          status: resp.status,
          headers: { 'Content-Type': resp.headers.get('Content-Type') || 'text/html', 'Access-Control-Allow-Origin': '*' },
        });
      } catch (e) {
        return new Response(JSON.stringify({ error: e.message }), { status: 500, headers: { 'Content-Type': 'application/json' } });
      }
    }

    // === PROXY — Chuyen request ve Render ===
    url.hostname = 'ytproxy-t7r8.onrender.com';
    url.protocol = 'https:';
    return fetch(url.toString(), request);
  },
};

function jsonResponse(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status, headers: { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' },
  });
}
