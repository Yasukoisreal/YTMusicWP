# GEMINI.md / AGENTS.md

Behavioral guidelines to optimize Gemini models for software engineering and prevent common LLM coding mistakes. Merge with project-specific instructions (e.g., `.agents/AGENTS.md`) as needed.

**Tradeoff:** These guidelines prioritize correctness, precision, and caution over speed.

---

## 1. Grounding & Think Before Coding

**Always ground in actual code. Don't assume, don't guess, surface tradeoffs.**

- **Read before writing:** Leverage Gemini's large context window to read full files completely before modifying (`view_file`). Never guess file content, imports, function signatures, or existing patterns.
- **Clarify ambiguities:** If a requirement has multiple valid interpretations, explicitly state them and ask or propose the cleanest option—do not silently pick one.
- **Surface simpler solutions:** If the requested approach is over-engineered or problematic, suggest the simpler, safer alternative.
- **Acknowledge unknowns:** If documentation or code context is missing, investigate or ask rather than hallucinating APIs.

## 2. Simplicity & Minimalist Architecture

**Minimum code that robustly solves the problem. No speculative abstractions.**

- **YAGNI (You Aren't Gonna Need It):** No features, helpers, or config toggles beyond what was explicitly requested.
- **No premature abstractions:** Do not build generic interfaces or utility wrappers for single-use logic.
- **No defensive overkill:** Avoid excessive boilerplate error handling or fallbacks for impossible scenarios in internal code.
- **Conciseness over verbosity:** If a solution can be implemented cleanly in 30 lines, do not generate 150 lines of boilerplate.

> *Ask yourself:* "Would a senior engineer consider this overcomplicated or unnecessarily verbose?" If yes, simplify.

## 3. Surgical & Targeted Changes

**Touch only what you must. Clean up only your own impact.**

When editing existing code:
- **ALWAYS use native Antigravity tools:** NEVER use terminal commands (like PowerShell `cat`, `Get-Content`, `Set-Content`, `Select-String`, `grep`, `sed`, or `echo`) to read, search, view or modify source code files. Terminal tools are slow, clutter the chat UI, and corrupt file encoding (destroying UTF-8 characters like UI icons) causing `Inconsistent Line Endings` in Visual Studio.
  - To **READ** or **VIEW** a file: ALWAYS use the `view_file` tool.
  - To **SEARCH** for code: ALWAYS use the `grep_search` or `find_by_name` tools.
  - To **MODIFY** a file: ALWAYS use the `replace_file_content` or `write_to_file` tools.
- **Targeted edits:** Use precise diffs/replacements. Do not reformat or overwrite entire files when changing a few lines.
- **Preserve surrounding code:** Do not "clean up", reformat, or alter existing comments, indentation, or style in unrelated blocks.
- **Match project conventions:** Strictly mirror existing naming, architecture, and coding patterns in the repository.
- **Handle orphans:** Clean up unused imports, variables, or functions that *your* changes made obsolete, but leave pre-existing dead code untouched unless asked.

> *Golden rule:* Every changed line in the diff must trace directly to the user's objective.

## 4. Goal-Driven Execution & Verification

**Define explicit success criteria. Verify before declaring completion.**

- **Transform tasks into verifiable checkpoints:**
  - *"Fix the bug"* → Identify root cause, reproduce/verify failure, apply fix, verify resolution.
  - *"Refactor component"* → Confirm behavior and functionality match exactly before and after.
  - *"Add feature"* → Implement, run build/tests, verify all edge cases.
- **Multi-step execution format:**
  ```text
  1. [Step/Action] → Verify: [Exact check / Command / Expected result]
  2. [Step/Action] → Verify: [Exact check / Command / Expected result]
  ```
- **Self-Review:** Inspect diffs before finalizing to ensure zero unintended side effects or syntax regressions.

## 5. Direct & Concise Communication

- Avoid sycophancy, excessive apologies, or repetitive disclaimers.
- Focus responses on concise explanations of technical decisions, rationale for changes, and actionable verification results.

## 6. Git Version Control & Regular Commits

- **Commit upon completing tasks:** Always create a clear, meaningful Git commit after implementing a feature, refactoring, or fixing a bug once build/verification succeeds.
- **Prevent regressions:** Keep git working tree clean and committed to make rollbacks easy and prevent code loss.

## 7. WP8.1 Project-Specific Constraints (YTMusicWP)

**Strict Hardware & OS Limitations:**
- **RAM Constraints (512MB Target):** Be extremely careful with memory allocation.
- **IPC Payload Limits:** Communication between the Foreground UI and `AudioPlayerTask` via `ValueSet` is strictly size-limited. NEVER pass full unrestricted arrays (e.g., passing thousands of tracks). Always cap queue arrays to a maximum of 100 items around the current track to prevent OutOfMemory/Serialization crashes.

**Storage Leak Prevention:**
- **No Orphaned Temp Files:** Storage space on old WP8.1 devices is critically low. Temporary streams (e.g., `temp_play_*`) must be aggressively tracked and cleaned. Always include a fallback cleanup routine on app startup (`MainPage` constructor) because Background Tasks can be killed by the OS without running their cleanup handlers.

**UI Virtualization & Live Tiles:**
- **ListView Scrolling:** `ScrollIntoView` in WinRT only brings items to the edge, it does NOT center them. To calculate center offsets for virtualized items (like Lyrics), always call `UpdateLayout()` synchronously after `ScrollIntoView` to force container generation before applying `TransformToVisual`.
- **Background Tile Consistency:** Live Tile XML templates pushed from the Background Task must EXACTLY match the templates used in `TileService.cs` (e.g., matching `TileSquare310x310PeekImage01`). Always read and respect user settings (like `LiveTileMode`) even when running in the background.

---

**These guidelines are working if:** diffs contain zero unnecessary changes, solutions avoid over-engineering, code is thoroughly grounded via complete file reading, and clarifying questions precede implementation.

