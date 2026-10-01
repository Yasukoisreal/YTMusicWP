---
name: wp81-platform-gotchas
description: "WP8.1 runtime traps hit in YTMusicWP - Newtonsoft POCO serialization, cookie handling of HttpClient/WinRT, Win10-only APIs"
metadata:
  node_type: memory
  type: project
  originSessionId: 1bd60524-7d70-432b-a20e-f560716232d9
  modified: 2026-09-29T14:27:43.696Z
---

Traps confirmed on the WP8.1 emulator while building multi-account (2026-09-28):

- Newtonsoft `JsonConvert.SerializeObject/DeserializeObject<AppType>` throws `TypeAccessException` (first-chance, silently breaks saves). Use `JObject`/`JArray` by hand; only `Dictionary<string,string>` round-trips safely.
- A shared `System.Net.Http.HttpClient` keeps its own cookie store; cookie-auth requests right after a WebView login came back `logged_in=0`-ish (0 liked/playlists) until app restart. Fix: dedicated client with `HttpClientHandler.UseCookies = false` (`InnerTubeClient._authClient`).
- `HttpBaseProtocolFilter.CookieUsageBehavior` / `HttpCookieUsageBehavior` are Windows 10 only (CS1061 on 8.1). The WinRT client always adds WebView-jar cookies, so the jar is emptied after sign-in/switch instead.
- A foreground MediaElement playing sound (Samples) lets the OS close the paused background audio task; the cached `BackgroundMediaPlayer.Current` proxy is then dead but only throws when called. `SendMessageToBackground` silently starts a NEW task, so audio plays while the app listens to the dead proxy (play icon stuck, first tap lost). `EnsureBackgroundPlayer()` now probes `CurrentState` before trusting the proxy (2026-09-28).
- First-chance `ArgumentException: Use of undefined keyword value 1 for event TaskScheduled` (mscorlib, on `await Task.Delay` etc.) is debugger-only noise from the TPL ETW EventSource, caught inside mscorlib; dozens per startup under VS, none without a debugger. Not an app bug / not a perf cause (confirmed 2026-09-28 with Break-on-ArgumentException).
- `getAccountSwitcherEndpoint` body is `)]}'` + JSON + `;` — parse with `JObject.Load(JsonTextReader)`, not `JObject.Parse`.
- No equalizer / audio effect for background audio: WP8.1 `Windows.Media.Playback.MediaPlayer` has no `AddAudioEffect` (checked the SDK's abi\windows.media.playback.h 2026-09-29; `IMediaPlayerEffects` is Win10). Only MediaElement, MediaCapture and MediaTranscoder take MF effects. SABR samples are compressed, so a managed EQ would need a full decoder. Don't re-propose an in-app EQ; Lumia's system audio settings are the only option.

**Why:** each of these cost a test round; none is visible at build time.
**How to apply:** check this before adding persistence, HTTP auth, or newer WinRT APIs. Related: [[user-tests-on-emulator]].
