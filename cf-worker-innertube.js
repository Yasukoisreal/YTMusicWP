export default {
  async fetch(request) {
    const url = new URL(request.url);

    // === FETCH PROXY — Render goi de lay trang YouTube qua IP cua Cloudflare ===
    if (url.pathname === '/fetch') {
      const targetUrl = url.searchParams.get('url');
      const key = url.searchParams.get('key');

      if (key !== 'LumiaWP81-An' || !targetUrl) {
        return new Response('Bad Request', { status: 400 });
      }

      try {
        // Forward request toi target URL voi headers phu hop
        const headers = {
          'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36',
          'Accept-Language': 'en-US,en;q=0.9',
          'Accept': 'text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8',
        };

        // Neu Render gui cookies, forward chung
        const cookieHeader = request.headers.get('X-YT-Cookies');
        if (cookieHeader) {
          headers['Cookie'] = cookieHeader;
        }

        const resp = await fetch(targetUrl, {
          method: request.method === 'POST' ? 'POST' : 'GET',
          headers: headers,
          body: request.method === 'POST' ? await request.text() : undefined,
          redirect: 'follow',
        });

        // Tra ve response voi CORS headers
        const body = await resp.text();
        return new Response(body, {
          status: resp.status,
          headers: {
            'Content-Type': resp.headers.get('Content-Type') || 'text/html',
            'Access-Control-Allow-Origin': '*',
          },
        });
      } catch (e) {
        return new Response(JSON.stringify({ error: e.message }), {
          status: 500,
          headers: { 'Content-Type': 'application/json' },
        });
      }
    }

    // === PROXY — Chuyen tat ca request khac ve Render ===
    url.hostname = 'ytproxy-t7r8.onrender.com';
    url.protocol = 'https:';

    return fetch(url.toString(), request);
  },
};
