---
name: animated-artwork-findings
description: "Probe results (2026-09-28) for animated cover art on WP8.1 - YTM has none, Apple Music editorialVideo works as direct fMP4 next to background audio"
metadata:
  node_type: memory
  type: project
  originSessionId: 1bd60524-7d70-432b-a20e-f560716232d9
  modified: 2026-09-28T10:06:06.409Z
---

Implemented as `Services/AnimatedArtworkService.cs` + `Pages/MainPage.AnimatedArtwork.cs` (Settings toggle "Animated Artwork", default OFF on every device — user's call after it made Now Playing laggy; perf fixes: start 450 ms after slide-in, pause on pivot instead of reload, 360-486 px rendition by screen size). The DEBUG probe that produced these findings was deleted. Probe results:

- YouTube Music `musicAnimatedThumbnailRenderer`: absent from album search and album browse, even "After Hours". Dead end.
- Apple Music (no account): token = JWT (iss AMPWebPlay) scraped from music.apple.com -> `/assets/index~*.js`. Stream-scan: home hit after 32 K chars, bundle after 883 K chars (whole files are 2.3 M / 3.2 M chars, ~26 MB spike if read fully). Token lifetime ~2 months (exp 2026-11-26) -> cache it.
- Search `amp-api-edge .../catalog/us/search?types=songs&include[songs]=albums&extend=editorialVideo&format[resources]=map`; only some albums have `editorialVideo` (After Hours yes, "Funny" singles no).
- Master m3u8 has ~29 variants; each variant playlist is EXT-X-BYTERANGE over ONE fMP4 file (ftyp moov moof mdat..., 17 moof, 20 s). 486x486 avc1 ~766 kbps = 1.9 MB.
- WP8.1 MediaElement: native HLS fails (0xC00D36C4); the fMP4 URL plays directly and from a local file; NaturalDuration reports TimeSpan.MaxValue.
- Muted MediaElement (AudioCategory.Other) plays alongside BackgroundMediaPlayer; the song kept Playing for 10 s x2.
- Measured on a high-RAM emulator (825 MB limit): playback added ~15-20 MB. Still unverified: loop at 20 s with unknown duration, 512 MB emulator cost.
- Spotify Canvas deliberately not pursued (needs sp_dc login + rotating TOTP secrets).

Related: [[samples-video-findings]], [[wp81-platform-gotchas]]
