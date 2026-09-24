# Automated Testing & CI/CD Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish an industrial-grade automated unit testing suite for core algorithms (SABR Protobuf, UMP Multiplexer, LRC Parser) and a fail-fast CI/CD pipeline on GitHub Actions that blocks broken builds and gates releases.

**Architecture:** 
1. Create a lightweight, high-performance Test Project (`YTMusicWP.Tests`) using Microsoft MSTest targeting .NET Framework 4.5.2+, executable via Visual Studio 2015's native `vstest.console.exe` and modern CI runners.
2. Link pure C# core parsers (`MiniProtoWriter`, `UmpParser`) and extract `LrcParser` to shared logic for zero-dependency testability.
3. Construct a GitHub Actions workflow (`.github/workflows/ci.yml`) featuring compile verification, automated test execution, and tag-gated AppX packaging.

**Tech Stack:** C# 6.0, MSTest (`Microsoft.VisualStudio.QualityTools.UnitTestFramework`), MSBuild 14.0 / Visual Studio 2015, GitHub Actions YAML.

## Global Constraints

- Platform: Compatible with Visual Studio 2015 Community Update 3 & MSBuild 14.0
- C# 6.0 compatibility only (no tuples, no local functions, no out var)
- Native Antigravity tools only for reading/modifying code (no terminal cat/sed/echo)
- Fail-fast CI/CD design: Broken builds or failing tests must halt the pipeline immediately

---

### Task 1: Create `YTMusicWP.Tests` Project and Configure Solution

**Files:**
- Create: `YTMusicWP.Tests\YTMusicWP.Tests.csproj`
- Create: `YTMusicWP.Tests\Properties\AssemblyInfo.cs`
- Modify: `YTMusicWP.sln`

- [ ] **Step 1: Create the Test Project file**
Create `YTMusicWP.Tests\YTMusicWP.Tests.csproj` configured for MSTest with links to `MiniProtoWriter.cs` and `UmpParser.cs`.

- [ ] **Step 2: Create AssemblyInfo.cs for the test project**
Create `YTMusicWP.Tests\Properties\AssemblyInfo.cs` with standard assembly attributes.

- [ ] **Step 3: Register `YTMusicWP.Tests` into `YTMusicWP.sln`**
Add the new project GUID to `YTMusicWP.sln` with Debug and Release build configurations.

- [ ] **Step 4: Verify Compilation**
Run: `& "C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe" YTMusicWP.Tests\YTMusicWP.Tests.csproj /p:Configuration=Debug /p:Platform="Any CPU" /v:m`
Expected: 0 Errors, Build Succeeded.

---

### Task 2: Implement Unit Tests for `MiniProtoWriter` and `UmpParser`

**Files:**
- Create: `YTMusicWP.Tests\MiniProtoWriterTests.cs`
- Create: `YTMusicWP.Tests\UmpParserTests.cs`

- [ ] **Step 1: Write `MiniProtoWriterTests.cs`**
Test:
  1. Varint encoding for single-byte values (e.g. 0, 1, 127).
  2. Multi-byte varint encoding (e.g. 128, 300, 16384).
  3. Field tag encoding (`fieldNumber << 3 | wireType`).
  4. String and bytes field serialization (`WriteBytesField`, `WriteStringField`).

- [ ] **Step 2: Write `UmpParserTests.cs`**
Test:
  1. UMP Header parsing (verifying part types: `MEDIA_HEADER`, `MEDIA`, `FORMAT_INITIALIZATION_METADATA`).
  2. Multi-part UMP stream splitting from in-memory byte streams.
  3. `ChunkedStreamReader` buffer handling and EOF detection.

- [ ] **Step 3: Compile and Run Tests locally**
Run:
```powershell
& "C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe" YTMusicWP.Tests\YTMusicWP.Tests.csproj /p:Configuration=Debug /p:Platform="Any CPU" /v:m
& "C:\Program Files (x86)\Microsoft Visual Studio 14.0\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe" YTMusicWP.Tests\bin\Debug\YTMusicWP.Tests.dll
```
Expected: All tests PASS (Total tests > 8, Failed: 0).

---

### Task 3: Extract `LrcParser` and Implement `LrcParserTests`

**Files:**
- Create: `YTMusicWP\Services\LrcParser.cs`
- Modify: `YTMusicWP\Pages\MainPage.Lyrics.cs`
- Create: `YTMusicWP.Tests\LrcParserTests.cs`

- [ ] **Step 1: Extract pure static `LrcParser` helper**
Move `TryParseLrcTime` logic to `YTMusicWP.Services.LrcParser.cs` so it is decoupled from `MainPage` and directly testable.

- [ ] **Step 2: Update `MainPage.Lyrics.cs` to delegate to `LrcParser`**
Update `TryParseLrcTime` in `MainPage.Lyrics.cs` to call `LrcParser.TryParseLrcTime`.

- [ ] **Step 3: Write `LrcParserTests.cs` in `YTMusicWP.Tests`**
Test:
  1. Standard timestamps `[01:23.45]` -> 1 min, 23 sec, 450 ms.
  2. Standard timestamps with 3 millisecond digits `[02:15.890]` -> 2 min, 15 sec, 890 ms.
  3. Standard timestamps without milliseconds `[00:45]` -> 45 sec.
  4. Malformed and invalid strings (`null`, `""`, `"abc"`, `"[invalid]"`) safely return `false`.

- [ ] **Step 4: Verify test execution**
Run: `& "C:\Program Files (x86)\Microsoft Visual Studio 14.0\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe" YTMusicWP.Tests\bin\Debug\YTMusicWP.Tests.dll`
Expected: 100% Passed.

---

### Task 4: Setup Fail-Fast CI/CD Pipeline on GitHub Actions

**Files:**
- Create: `.github\workflows\ci.yml`

- [ ] **Step 1: Create `.github\workflows\ci.yml`**
Configure two distinct jobs:
1. `test_and_verify`:
   - Runs on `push` to any branch and on all `pull_request`.
   - Checks out code, sets up MSBuild, compiles the test project, runs `vstest.console.exe`.
   - If tests fail, workflow immediately terminates with exit code 1.
2. `release_package`:
   - Runs **ONLY** when a git tag starting with `v` is pushed (e.g. `v1.0.0`).
   - Requires `test_and_verify` to complete with 100% success first.
   - Compiles Release artifacts and uploads them.

- [ ] **Step 2: Validate YAML syntax**
Ensure valid GitHub Actions YAML syntax without indentation or schema issues.

---

### Task 5: Full System Verification & Git Commit

- [ ] **Step 1: Build the entire solution (ARM + x86 + Tests)**
Verify that adding the test project and `LrcParser` causes zero regressions to `YTMusicWP` and `AudioPlayerTask`.

- [ ] **Step 2: Run all unit tests**
Ensure all tests execute and pass cleanly.

- [ ] **Step 3: Commit all changes**
Commit with: `feat: add automated unit testing suite and fail-fast GitHub Actions CI/CD`
