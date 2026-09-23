using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Networking;
using Windows.Security.Cryptography.Certificates;
using Windows.Web.Http;
using Windows.Web.Http.Filters;

namespace YTMusicWP.Services
{
    /// <summary>
    /// Information about a URL rewritten with a secure DNS resolved IP.
    /// </summary>
    internal struct ResolvedUrlInfo
    {
        public string Url;
        public string OriginalHost;
        public bool WasResolved;
    }

    /// <summary>
    /// Secure DNS (DNS-over-HTTPS / DoH) Resolver.
    /// Resolves hostnames via Google Public DNS (8.8.8.8 / 8.8.4.4) with Cloudflare (1.1.1.1) fallback.
    /// Bypasses ISP port 53 DNS throttling, censorship, and routing issues without requiring a proxy server.
    /// Fully compatible with Windows Phone 8.1 (WPA81) and C# 6.0.
    /// </summary>
    internal static class SecureDnsResolver
    {
        private class CacheEntry
        {
            public string Ip;
            public DateTime ExpiryUtc;
        }

        private static readonly Dictionary<string, CacheEntry> _cache = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _cacheLock = new object();

        // Reusable HttpClient with certificate mismatch tolerance for direct IP connections
        private static HttpClient _dohClient;
        private static readonly object _clientLock = new object();

        private static HttpClient GetHttpClient()
        {
            if (_dohClient == null)
            {
                lock (_clientLock)
                {
                    if (_dohClient == null)
                    {
                        var filter = new HttpBaseProtocolFilter();
                        filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.Untrusted);
                        filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.InvalidName);
                        filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.Expired);
                        _dohClient = new HttpClient(filter);
                    }
                }
            }
            return _dohClient;
        }

        /// <summary>
        /// Clears the in-memory DNS cache.
        /// </summary>
        public static void ClearCache()
        {
            lock (_cacheLock)
            {
                _cache.Clear();
            }
        }

        /// <summary>
        /// Resolves a hostname to an IPv4 address using DNS-over-HTTPS.
        /// Returns the original host if resolution fails or if the host is already an IP.
        /// </summary>
        public static async Task<string> ResolveIpAsync(string host)
        {
            if (string.IsNullOrEmpty(host))
                return host;

            // Fast path: if already an IP address, return directly
            if (IsIpv4(host))
                return host;

            // Check cache
            lock (_cacheLock)
            {
                CacheEntry entry;
                if (_cache.TryGetValue(host, out entry))
                {
                    if (DateTime.UtcNow < entry.ExpiryUtc)
                    {
                        return entry.Ip;
                    }
                    _cache.Remove(host);
                }
            }

            // 1. Try Primary: Cloudflare DNS (1.1.1.1) — Native IP SSL certificate, ultra-fast (<50ms)
            string ip = await QueryDohEndpointAsync("https://1.1.1.1/dns-query?name=" + Uri.EscapeDataString(host) + "&type=A", "application/dns-json", host, null).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(ip))
                return ip;

            // 2. Try Secondary: Cloudflare DNS (1.0.0.1)
            ip = await QueryDohEndpointAsync("https://1.0.0.1/dns-query?name=" + Uri.EscapeDataString(host) + "&type=A", "application/dns-json", host, null).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(ip))
                return ip;

            // 3. Try Fallback: Google Public DNS (dns.google)
            ip = await QueryDohEndpointAsync("https://dns.google/resolve?name=" + Uri.EscapeDataString(host) + "&type=A", null, host, null).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(ip))
                return ip;

            // 4. Try Fallback: Google Public DNS IP (8.8.8.8) with Host header
            ip = await QueryDohEndpointAsync("https://8.8.8.8/resolve?name=" + Uri.EscapeDataString(host) + "&type=A", null, host, "dns.google").ConfigureAwait(false);
            if (!string.IsNullOrEmpty(ip))
                return ip;

            // Ultimate fallback: return original host (lets system resolver handle it)
            return host;
        }

        /// <summary>
        /// Rewrites a URL replacing its hostname with the resolved IPv4 address.
        /// Sets WasResolved = true if a valid IP was substituted.
        /// </summary>
        public static async Task<ResolvedUrlInfo> RewriteUrlAsync(string originalUrl)
        {
            var info = new ResolvedUrlInfo
            {
                Url = originalUrl,
                OriginalHost = null,
                WasResolved = false
            };

            if (string.IsNullOrEmpty(originalUrl))
                return info;

            try
            {
                Uri uri = new Uri(originalUrl);
                string originalHost = uri.Host;
                info.OriginalHost = originalHost;

                string resolvedIp = await ResolveIpAsync(originalHost).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(resolvedIp) && !string.Equals(resolvedIp, originalHost, StringComparison.OrdinalIgnoreCase))
                {
                    var builder = new UriBuilder(uri);
                    builder.Host = resolvedIp;
                    info.Url = builder.Uri.AbsoluteUri;
                    info.WasResolved = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[SecureDns] RewriteUrl error: " + ex.Message);
            }

            return info;
        }

        private static async Task<string> QueryDohEndpointAsync(string endpointUrl, string acceptHeader, string host, string customHost)
        {
            try
            {
                var client = GetHttpClient();
                using (var req = new HttpRequestMessage(HttpMethod.Get, new Uri(endpointUrl)))
                {
                    if (!string.IsNullOrEmpty(customHost))
                    {
                        req.Headers.Host = new HostName(customHost);
                    }
                    if (!string.IsNullOrEmpty(acceptHeader))
                    {
                        req.Headers.TryAppendWithoutValidation("Accept", acceptHeader);
                    }
                    req.Headers.TryAppendWithoutValidation("User-Agent", "YTMusicWP/1.0");

                    using (var cts = new CancellationTokenSource(2000)) // 2-second timeout per DoH provider
                    using (var resp = await client.SendRequestAsync(req).AsTask(cts.Token).ConfigureAwait(false))
                    {
                        if (resp.IsSuccessStatusCode)
                        {
                            string json = await resp.Content.ReadAsStringAsync().AsTask().ConfigureAwait(false);
                            return ParseDohJsonResponse(json, host);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[SecureDns] DoH query to " + endpointUrl + " failed: " + ex.Message);
            }

            return null;
        }

        private static string ParseDohJsonResponse(string json, string host)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            try
            {
                JsonObject root;
                if (!JsonObject.TryParse(json, out root))
                    return null;

                if (!root.ContainsKey("Status") || (int)root.GetNamedNumber("Status") != 0)
                    return null;

                if (!root.ContainsKey("Answer"))
                    return null;

                var answers = root.GetNamedArray("Answer");
                for (uint i = 0; i < answers.Count; i++)
                {
                    var item = answers.GetObjectAt(i);
                    // type 1 = DNS A record (IPv4)
                    if (item.ContainsKey("type") && (int)item.GetNamedNumber("type") == 1 && item.ContainsKey("data"))
                    {
                        string ipStr = item.GetNamedString("data");
                        if (IsIpv4(ipStr))
                        {
                            double ttlSec = 300;
                            if (item.ContainsKey("TTL"))
                            {
                                ttlSec = item.GetNamedNumber("TTL");
                            }
                            // Clamp TTL between 60 seconds and 3600 seconds (1 hour)
                            int finalTtl = Math.Max(60, Math.Min(3600, (int)ttlSec));

                            lock (_cacheLock)
                            {
                                _cache[host] = new CacheEntry
                                {
                                    Ip = ipStr,
                                    ExpiryUtc = DateTime.UtcNow.AddSeconds(finalTtl)
                                };
                            }

                            System.Diagnostics.Debug.WriteLine("[SecureDns] Resolved " + host + " -> " + ipStr + " (TTL: " + finalTtl + "s)");
                            return ipStr;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[SecureDns] Parse error: " + ex.Message);
            }

            return null;
        }

        private static bool IsIpv4(string host)
        {
            if (string.IsNullOrEmpty(host)) return false;
            try
            {
                var hn = new HostName(host);
                return hn.Type == HostNameType.Ipv4;
            }
            catch
            {
                return false;
            }
        }
    }
}
