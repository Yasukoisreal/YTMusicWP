---
name: samples-video-findings
description: "What was learned making the Samples tab play video on WP8.1 (what works, what failed, emulator caveats)"
metadata:
  node_type: memory
  type: project
  originSessionId: 41ddc17e-1f6a-4c60-814b-c985e3c2a866
  modified: 2026-09-28T15:45:16.680Z
---

Samples tab (2026-09-28): MediaElement plays itag 18 (360p muxed MP4) via plain Source=Uri while BackgroundMediaPlayer is paused — works on WP8.1.

- Never animate the MediaElement's own Opacity (poster Rectangle sits on top and fades instead).
- Arbitrary seeks cause grey macroblocks: clip start comes from the MP4 keyframe index (Services/Mp4KeyframeParser, first 256 KB via Range), seek 0.3 s before the keyframe, poster hides until 0.2 s past it.
- A custom IRandomAccessStream (chunked range reads) + SetSource made MediaElement stall forever — removed; don't retry that.
- googlevideo does NOT throttle open-ended ranges (measured 3.6 MB/s); emulator network 370–1260 KB/s — bandwidth is not the bottleneck.
- The 1080p 6" emulator stutters on video (software upscaling to 1080x1920); test video on the WVGA 512MB emulator.
- FEmusic_immersive (real YTM Samples feed) returns 400 with cookie auth (web SAPISIDHASH) and 404 anonymously; feed uses Home videos + region "Video charts" (FEmusic_charts formData gl) instead.

- Black picture + sound on one clip (2026-09-28) = H.264 level WP8.1 can't decode: itag 18 of Uws510cVia4 declares Level 6.2 (+B-frames) for 640x360; decoder refuses the video track. Chart survey: 20/22 Level 3.0, 1 Level 2.1, 1 Level 6.2. YouTube's codecs string ("avc1.42001E" Baseline) lies — files are Main (77). Fix: Mp4KeyframeParser.ReadAvcLevel > 51 → skip clip.
- "Stalls ~6 s into every clip" once, then gone next run with unchanged code: googlevideo itag 18 measured 1.5–2 MB/s from the host with any User-Agent (not throttled, no n param) → treat as transient emulator/CDN network, not app code. DEBUG A/B tool Pages/MainPage.SamplesDebug.cs (Seek/Fill/Overl/Src FILE + "[SamplesAB]" downloaded-vs-played log) exists for the next occurrence.

- "Stutter + scratchy picture" (2026-09-28): the 360p landscape clip was Stretch=UniformToFill on the portrait screen = ~2.2x zoom of the middle third (macroblocks visible) and a full-screen scale+blend per frame under gradients/translucent buttons. User chose letterbox (Stretch=Uniform, ~1.3x) over a blurred backdrop (mqdefault decoded at 24 px, stretched) with the poster sized to the video rect (ShortsPanel.PosterAspect, real aspect set in MediaOpened).

Related: [[user-tests-on-emulator]], [[animated-artwork-findings]]
