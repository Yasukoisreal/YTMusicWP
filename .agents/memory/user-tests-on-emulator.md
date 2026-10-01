---
name: user-tests-on-emulator
description: User runs and tests YTMusicWP on the WP8.1 emulators themselves; Claude builds and hands over a test checklist
metadata:
  node_type: memory
  type: feedback
  originSessionId: 41ddc17e-1f6a-4c60-814b-c985e3c2a866
  modified: 2026-09-27T15:49:16.037Z
---

Don't screenshot, drive or deploy to the emulator unless asked. Build (x86 Debug via MSBuild 14.0) to verify it compiles, then give the user a concrete checklist of what to test; they report back with screenshots/Output logs.

**Why:** The user said "đừng chụp nhé để tôi tự test" / "không cần test đâu, để tôi tự test", and a desktop screenshot once captured a private window.

**How to apply:** After each change: build, verify, list test steps, give commit commands (see [[no-ai-trailers]]). VS 2015 Live Visual Tree does not work for WP8.1 apps, so don't suggest it.
