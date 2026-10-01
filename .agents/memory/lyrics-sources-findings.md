---
name: lyrics-sources-findings
description: Apple Music TTML lyrics API (lyrics-api.boidu.dev) now needs an API key for uncached songs; TTML duet/background markup facts
metadata:
  node_type: memory
  type: project
  originSessionId: 1bd60524-7d70-432b-a20e-f560716232d9
  modified: 2026-09-29T14:12:47.208Z
---

Checked 2026-09-28:
- `https://lyrics-api.boidu.dev/getLyrics?s=&a=` (AppleMusicLyricsApi) returns 401 "Uncached queries require a valid API key via X-API-Key header" for songs nobody fetched before; cached popular songs still return TTML. So Apple Music word-sync coverage silently shrinks; KuGou/LRCLIB fill in.
- The cache is keyed by duration too: "Fool For You"/Kastra answers 200 only for `&d=206..210` (cached ~208 ±2), 401 without `d` or outside that. So the lyrics request must carry the song's real duration — the app waits for the player to be on the new song (CurrentVideoId + Playing) and retries d±3.
- The artist text is part of the key too: "Stay" hits only as `a=The Kid LAROI, Justin Bieber` (d=140..144), not with " & " — the app retries with artists joined by ", ".
- Apple TTML duet markup: `<ttm:agent type="person|group" xml:id="v1"/>` in head, `ttm:agent="v1"` on each `<p>`; background vocals are `<span ttm:role="x-bg">` wrapping timed spans inside a line and often run past the next line's start (overlapping lines).
- Samples that were cached: "Stay" (The Kid LAROI, agents v1/v2/group), "Blinding Lights", "Sweet but Psycho" (x-bg).
- SimpMusic Lyrics (`api-lyrics.simpmusic.org/v1/{videoId}`, translations at `/v1/translated/{videoId}/{lang}`) answers only `User-Agent: SimpMusicLyrics/1.0` (403 for any other UA, 2026-09-29). Using it means impersonating their client: not done; needs the maintainer's (maxrave-dev) permission first. Data is community-submitted: `syncedLyrics` and `richSyncLyrics` of one entry can even be different languages.

- "Lyrics sometimes word-by-word, sometimes not" (2026-09-29, "Last Thing You Need (from GTAVI: The Album)"): Apple Music answers 401 (uncached) for every title/artist/duration variant, so word sync comes only from KuGou, which is flaky (timeouts); a failed KuGou run leaves the page blank for 10+ s while LRCLIB/captions are tried. User declined a persistent lyrics cache / KuGou retry as too much complexity: don't re-propose unless asked.

**Why:** explains missing Apple Music lyrics and is the reference for the duet / background-vocal rendering.
**How to apply:** if Apple Music lyrics "stop working" for new songs, it's the key requirement, not app code.

Related: [[animated-artwork-findings]]
