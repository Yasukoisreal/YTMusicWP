---
name: live-memory-growth-findings
description: Why SABR livestreams died after 17-40 min (audio task 20 MB cap), what was measured and ruled out; AsStreamForRead managed leak fixed, ~0.14 MB/min outside the managed heap still open
metadata:
  node_type: memory
  type: project
  originSessionId: 1bd60524-7d70-432b-a20e-f560716232d9
  modified: 2026-09-29T13:30:23.586Z
---

Audio task cap is 20 MB on the 512 MB emulator (exit code 14, or StackOverflowException in "Unknown Module" right at the cap).

Measured 2026-09-28/29 (Debug x86, `[SabrMSS] Memory:` line each minute):
- Queuing ~600 native MediaStreamSample objects cost ~3-4 MB -> now raw byte[] frames, sample created in SampleRequested (start 13.3 -> 9.8 MB).
- Growth ~0.2 MB/min remained; managed heap after full GC grew 1 -> 4 MB. WeakReference probes on every per-sample/per-request object (args, request, sample, frame, HttpRequest/Response, streams, body, UMP parts, segments, WinRT ops, AsTask tasks): all collected (0 alive).
- Disassembled WP8.1 framework (SDK `Tools\MDILXAPCompile\Framework`): AsTask(ct) bridge deregisters its token; CTS registrations reuse slots; debugger task tracking balanced. Not the cause.
- With a full GC at every request phase boundary the heap stayed flat for ~15 min, then managed-after-GC still grew 0.9 -> 6.6 MB and the task died (0x80070008, then 0xc0000005). So it is NOT fragmentation (my earlier claim was wrong): something the probes don't cover keeps ~13 KB per request.
- Offline self-test (repeat each request step, full GC, bytes left per call): AsStreamForRead over a WinRT stream left ~109 KB per call (its adapter is cached in a static ConditionalWeakTable keyed by the WinRT stream, so the whole managed graph lives as long as the native stream); NativeBufferReader left 0. CONFIRMED in a 62-min live run: managed flat 1.0-1.2 MB.
- Remaining growth (AppMemoryUsage minus live managed) ~0.14 MB/min. A/B in one run: fresh HttpClient per request changed nothing, cookies always 0 -> not the HTTP stack. Copying each sample into a native Buffer (no pinned frames) also changed nothing (0.15 MB/min, died at 45 min) -> reverted.
- Every measurement so far ran as a Debug build under the VS debugger (thread churn, exceptions, Debug output are tracked in-process). Release build started without the debugger (Ctrl+F5): still playing after 1 h on 2026-09-29 with `Memory:` flat at 15.4-16.1 MB and managed 1.0 MB from minute 45 to 62 (every debugger run died at 17-62 min). CONFIRMED: the remaining growth is debugger-induced; users are not affected. Case closed.
- Lesson: measure memory caps without the debugger attached before chasing a "leak" that only shows under it.
- A Debug self-test that allocates MBs early raises the committed heap for the rest of the run: don't leave such tests in when measuring.
- No phone CoreCLR DAC on the host, so SOS heap dumps are not an option.

Also seen: YouTube SPS code 2 -> 3 (attestation) intermittently rejects the Cloudflare-worker poToken; on code 3 the stream dies and MediaEnded falls into the legacy DASH swap path (bugs not yet fixed).

Related: [[wp81-platform-gotchas]], [[user-tests-on-emulator]]
