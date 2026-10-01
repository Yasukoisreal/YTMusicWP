# AGENTS.md / GEMINI.md

**Core Directive:** Highest cognitive effort, ultra-deep reasoning, and zero-defect technical execution. Operate with the technical precision, paranoia, and architectural rigor of a principal systems engineer (Claude Opus 5.5 / Maximum Effort tier).

**Tradeoff:** Correctness, precision, safety, and deep verification strictly supersede speed and conciseness. Never rush, never cut corners, never make unverified assumptions.

---

## 1. Deep Reasoning & High-Effort Thinking Protocol

### A. Root Cause Analysis (5 Whys & Execution Tracing)
- **Trace the full stack:** When diagnosing any bug or unexpected behavior, trace execution flow through the entire system (Foreground UI, Dispatcher, IPC ValueSet, Background Audio Task, WinRT runtime, SQLite, and network layers).
- **Never patch symptoms:** Do not apply shallow null checks or try-catches that mask underlying logic flaws. Find the fundamental root cause and fix it architecturally.
- **Understand concurrency:** Always map out thread ownership, async continuation context, dispatcher requirements, and reentrancy risks before proposing changes.

### B. Multi-Hypothesis & Tradeoff Evaluation
- When facing non-trivial architectural or optimization problems, consider at least 2–3 viable solutions.
- Explicitly evaluate tradeoffs: Memory allocation (RAM / GC), CPU cycles, thread safety, platform compatibility, and complexity.
- Propose or pick the simplest, most robust solution that satisfies all constraints (apply YAGNI strictly).

### C. Adversarial Self-Critique & Pre-Mortem
Before writing or finalizing any code, stress-test your solution against worst-case scenarios:
1. *"What if the user taps rapidly or navigates away mid-operation?"* (Race condition / UI ObjectDisposed / Task cancellation).
2. *"What if this runs in the Background Task or on a ThreadPool thread?"* (WinRT thread affinity violation `RPC_E_WRONG_THREAD 0x8001010E`).
3. *"What happens on a 512MB RAM device under heavy memory pressure?"* (LOH fragmentation, out-of-memory crash, GC pause).
4. *"What happens if the network drops or returns malformed data?"* (Unhandled exception, corrupt state).
5. *"Are there any language features from C# 7.0+ accidentally included?"* (Build failure on MSBuild 14.0).

### D. Mandatory Deep Thinking & Prohibition of Fast/Shallow Reasoning (All Model Tiers)
- **Zero Tolerance for Fast Heuristics:** Regardless of which model tier is executing (including Gemini Flash, Flash-Lite, Pro, or Claude), rapid reflexive answering, intuitive guessing, and superficial code skimming are **strictly forbidden**.
- **Enforced Deliberate Cognitive Pacing:** You MUST deliberately slow down, think deeply, and reason with exhaustive thoroughness. Never leap prematurely to conclusions or code modifications.
- **Step-by-Step Mental Simulation:** Before writing, editing, or proposing any line of code, you must mentally simulate the execution path from start to finish:
  1. Trace all variable allocations, object lifetimes, and disposal semantics.
  2. Map thread ownership, asynchronous state machine continuations, and Dispatcher context transitions.
  3. Validate all edge failure modes (disconnection, null tokens, cancellation, memory pressure).
- **Cognitive Effort Over Speed:** Fast answers have zero value if they compromise correctness. Take as much reasoning effort and internal thinking depth as required to achieve mathematical certainty of zero defects.

### E. Clarify Ambiguities Before Modification (Never Guess Intent)
- **Always ask before acting:** If a user request, prompt, or bug report is ambiguous, underspecified, or open to multiple valid interpretations, **ALWAYS ask the user for clarification before making any code or file changes**.
- **Never make silent assumptions:** Do not guess user intent, assume unspoken requirements, or silently pick an arbitrary solution. When in doubt, present the options clearly and ask.

---



## 2. Zero-Defect Technical Constraints (WP8.1 & C# 6.0)

### A. Strict C# 6.0 Compatibility (Visual Studio 2015 Community Update 3 / MSBuild 14.0)
The compiler strictly enforces C# 6.0. **NEVER use C# 7.0+ syntax:**
- ❌ **No Out Variables:** `int.TryParse(s, out var val);` is INVALID.
  - ✅ Must write: `int val; int.TryParse(s, out val);`
- ❌ **No ValueTuples / Tuple Syntax:** `(int a, string b)` or `var pair = (1, "x");` is INVALID.
  - ✅ Must write: custom POCO, `Tuple<int, string>`, or `KeyValuePair<K, V>`.
- ❌ **No Pattern Matching:** `if (obj is Track track)` or `switch (obj) { case Track t: ... }` is INVALID.
  - ✅ Must write: `var track = obj as Track; if (track != null) { ... }`
- ❌ **No Local Functions:** `void Helper() { ... }` inside a method is INVALID.
  - ✅ Must write: private helper method or lambda `Action helper = () => { ... };`
- ❌ **No Throw Expressions:** `var x = y ?? throw new ...` is INVALID.
- ❌ **No Ref Locals / Ref Returns.**
- ❌ **No Binary Literals or Digit Separators:** `0b001` or `1_000_000` is INVALID.

### B. 512MB RAM Budget & Memory Management (Target: Lumia 520 / 530)
- **BitmapImage DecodePixelWidth Anti-Pattern:**
  - In WinRT WP8.1, setting `DecodePixelWidth` *after* `UriSource` is set has NO effect. The image is decoded at full resolution (~4–5MB RAM per image instead of <100KB), causing instant OOM crashes.
  - ❌ **Broken:** `new BitmapImage(new Uri(url)) { DecodePixelWidth = 320 };`
  - ✅ **Correct:**
    ```csharp
    var bmp = new BitmapImage();
    bmp.DecodePixelWidth = 320;
    bmp.UriSource = new Uri(url, UriKind.Absolute);
    target.ImageSource = bmp;
    ```
- **Rigorous Resource Disposal:**
  - Every `IDisposable` (`Stream`, `HttpResponseMessage`, `HttpContent`, `StorageFile` streams, `InMemoryRandomAccessStream`, Lumia Imaging SDK filters (`BlurFilter`, `StreamImageSource`, `WriteableBitmapRenderer`)) **MUST** be enclosed in a `using` block or explicitly disposed.
  - Never allocate unbounded image caches or collections without eviction.
  - Prevent Large Object Heap (LOH) allocations (>85KB) in streaming loops (e.g. SABR chunk reading). Reuse buffers wherever possible.

### C. WinRT Threading & UI Thread Affinity
- **DependencyObject Thread Affinity:**
  - `DependencyObject` instances (including `SolidColorBrush`, `BitmapImage`, `Storyboard`, `Transform`) belong strictly to the thread that created them.
  - ❌ **Never** declare static `SolidColorBrush` fields in models accessed by background threads (e.g., SQLite loaders). This causes fatal `RPC_E_WRONG_THREAD (0x8001010E)` when bound to XAML. Use `IValueConverter` instead.
- **Dispatcher Enforcement:**
  - `Application.Current.Resuming`, background IPC handlers, and ThreadPool callbacks execute off the UI thread.
  - Any access to UI elements or UI-bound properties **MUST** be wrapped in:
    ```csharp
    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
    {
        // UI updates here
    });
    ```

### D. Background Audio & Inter-Process Communication (IPC)
- **Payload Limits:** Communication between the Foreground UI and `AudioPlayerTask` via `ValueSet` is strictly size-limited. **NEVER** pass full unrestricted track arrays. Queue payloads must be capped to at most 100 items around the current track to prevent serialization/memory crashes.
- **Dead Proxy Handling:** If the foreground plays video/sound via `MediaElement`, the OS may terminate the paused background audio task. Probe `BackgroundMediaPlayer.Current` before issuing commands, and handle reconnections gracefully.
- **Storage Cleanup:** Background tasks killed by the OS do not execute `TaskCanceled` cleanup. Maintain an aggressive startup cleanup routine (`MainPage` constructor) for orphaned temporary files (`temp_play_*`, `temp_live_buf_*`, `*.tagging`, `*.tmp`).

### E. WinRT WP8.1 Runtime Quirks
- **Newtonsoft.Json POCO Serialization:**
  - `JsonConvert.DeserializeObject<T>` / `SerializeObject` with custom POCO types throws runtime `TypeAccessException` on WinRT WP8.1 due to security sandboxing.
  - Use manual `JObject` / `JArray` parsing or `Dictionary<string, string>`.
- **HttpClient vs Cookies:**
  - Shared `System.Net.Http.HttpClient` retains cookie state across requests. For cookie-isolated calls, instantiate a dedicated client with `HttpClientHandler.UseCookies = false`.
  - WinRT `HttpBaseProtocolFilter.CookieUsageBehavior` is Windows 10 only (CS1061 on 8.1).
- **UI Virtualization & Centering:**
  - WinRT `ListView.ScrollIntoView` only scrolls the item to the nearest viewport edge, **not** to the center.
  - To calculate a center offset (e.g. active lyric line), you **MUST** call `UpdateLayout()` synchronously immediately after `ScrollIntoView` to force container generation before invoking `TransformToVisual`.
- **Live Tile XML Templates:**
  - Live Tile XML strings generated from the background must strictly match official WP8.1 schema templates (e.g., `TileSquare310x310PeekImage01`).
  - Always XML-escape track titles and artist strings (`&amp;`, `&lt;`, `&gt;`, `&quot;`).

---

## 3. Surgical Code Modification & Tooling Rules

### A. Strict Tooling Discipline
- **NEVER use terminal commands** (`cat`, `Get-Content`, `Set-Content`, `Select-String`, `grep`, `sed`, `echo`) to read, search, or edit source code files.
  - Terminal commands corrupt UTF-8 encoding (breaking UI icons and Unicode text) and cause Visual Studio `Inconsistent Line Endings` warnings.
  - **READ / VIEW:** ALWAYS use the native `view_file` tool.
  - **SEARCH:** ALWAYS use the native `grep_search` or `find_by_name` tools.
  - **EDIT / WRITE:** ALWAYS use `replace_file_content` or `write_to_file`.

### B. Surgical & Minimalist Changes
- **"If it works, DO NOT touch it" (Preserve Stable Code):** If code is running stably and without errors, **DO NOT TOUCH IT**. Strictly forbid unprompted refactoring, unsolicited "cleanups", modernizations, or speculative rewrites. Never alter working logic unless the user explicitly commands it.
- Read the entire file or surrounding context completely with `view_file` before making any edit. Never guess imports, method names, or class structures.
- Make targeted replacements. Do not reformat unrelated code, alter indentation, or touch existing comments.
- Every modified line must trace directly to the user's explicit objective.
- Keep the git working tree clean and organized.

---

## 4. Verification & Empirical Evidence Protocol

**Never claim success without hard empirical evidence.**

### A. Multi-Step Execution Format
```text
1. [Investigate / Root Cause] → Verify: [File inspection & failure diagnosis]
2. [Surgical Implementation]  → Verify: [Precise diff review]
3. [Compile & Test]            → Verify: [MSBuild / vstest execution results]
```

### B. Build & Test Verification Commands
- **Compile Verification (Solution):**
  *LumiaImagingSDK is native C++ WinRT and does NOT support AnyCPU. You must build specifically for ARM (device) or x86 (emulator):*
  ```powershell
  # ARM (Physical Lumia Device):
  & "C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe" YTMusicWP.sln /p:Configuration=Debug /p:Platform=ARM /v:m

  # x86 (WP8.1 Emulator):
  & "C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe" YTMusicWP.sln /p:Configuration=Debug /p:Platform=x86 /v:m
  ```
- **Automated Test Suite (vstest):**
  ```powershell
  & "C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe" YTMusicWP.Tests\YTMusicWP.Tests.csproj /p:Configuration=Debug /p:Platform="Any CPU" /v:m
  & "C:\Program Files (x86)\Microsoft Visual Studio 14.0\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe" YTMusicWP.Tests\bin\Debug\YTMusicWP.Tests.dll
  ```

---

## 5. Communication, Language & Version Control

- **Language:** Always reply to the user in **Vietnamese** (natural, concise, engineering-focused tone). Code identifiers, file paths, and commit messages remain in English.
- **Direct Communication:** Zero sycophancy, zero empty disclaimers, zero boilerplate apologies. Deliver crisp technical reasoning, diffs, and verification proof.
- **Proactive Clarification:** If a prompt or requirement is ambiguous or open to multiple interpretations, **ALWAYS ask the user for clarification before modifying code**. Never guess or assume intent.
- **Git Commits & AI Policy (CONTRIBUTING.md Compliance):**
  - **NEVER** append AI trailers (`Co-Authored-By: Claude...`, `Generated with...`) to git commit messages or PR descriptions.
  - Write standard semantic commit messages (`feat: ...`, `fix: ...`, `perf: ...`, `refactor: ...`, `docs: ...`).
---

## 6. Project Knowledge Base & Memory System

Full project memory files transferred from Claude Code are indexed and maintained in [`.agents/memory/`](file:///d:/Documents/Visual%20Studio%202015/Projects/YTMusicWP/.agents/memory/):

- [`MEMORY.md`](file:///d:/Documents/Visual%20Studio%202015/Projects/YTMusicWP/.agents/memory/MEMORY.md): Master memory index.
- [`wp81-platform-gotchas.md`](file:///d:/Documents/Visual%20Studio%202015/Projects/YTMusicWP/.agents/memory/wp81-platform-gotchas.md): Deep platform traps (Newtonsoft POCO TypeAccessException, cookie isolation, dead audio proxy, no background audio EQ).
- [`live-memory-growth-findings.md`](file:///d:/Documents/Visual%20Studio%202015/Projects/YTMusicWP/.agents/memory/live-memory-growth-findings.md): SABR livestream memory growth analysis, 20MB background task limit, and `NativeBufferReader` fix.
- [`animated-artwork-findings.md`](file:///d:/Documents/Visual%20Studio%202015/Projects/YTMusicWP/.agents/memory/animated-artwork-findings.md): Apple Music motion artwork probe results (`editorialVideo` fMP4).
- [`samples-video-findings.md`](file:///d:/Documents/Visual%20Studio%202015/Projects/YTMusicWP/.agents/memory/samples-video-findings.md): Samples tab video playback findings (itag 18, keyframe parsing, H.264 level bounds).
- [`lyrics-sources-findings.md`](file:///d:/Documents/Visual%20Studio%202015/Projects/YTMusicWP/.agents/memory/lyrics-sources-findings.md): Apple Music TTML & KuGou lyrics source research, duet & background vocal parsing rules.
- [`mainpage-xaml-split.md`](file:///d:/Documents/Visual%20Studio%202015/Projects/YTMusicWP/.agents/memory/mainpage-xaml-split.md): MainPage XAML decomposition status and UserControl isolation.
- [`user-tests-on-emulator.md`](file:///d:/Documents/Visual%20Studio%202015/Projects/YTMusicWP/.agents/memory/user-tests-on-emulator.md): User testing protocol on physical/emulator WP8.1 devices.
- [`no-ai-trailers.md`](file:///d:/Documents/Visual%20Studio%202015/Projects/YTMusicWP/.agents/memory/no-ai-trailers.md): AI co-author trailer prohibition.
