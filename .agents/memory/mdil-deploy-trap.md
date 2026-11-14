---
name: mdil-deploy-trap
description: "ARM device deploy fails DEP6810 / MDILXapCompile 2001 in VS's own MDIL step; solved by deploying the plain appx with AppDeployCmd, which optimizes it for the device itself (tools\\Build-ArmAppx.ps1)"
metadata:
  node_type: memory
  type: project
  modified: 2026-10-14T07:56:45.000Z
---

**RESOLVED on 2026-10-14 - use `tools\Build-ArmAppx.ps1` to deploy to a phone, not F5.**

## The skill that works

`AppDeployCmd.exe /installlaunch <appx> /targetdevice:<index>` performs the deployment optimization (the MDIL step) itself, on the host, and then installs. It must be handed the **unoptimized** appx - the appx VS's layout produces, packed with MakeAppx. Measured on 2026-10-14: install+launch of the ARM Debug package took ~45 s end to end ("Completato."), app launches.

`tools\Build-ArmAppx.ps1` does exactly that: optional MSBuild for ARM, lay out the payload from `bin\ARM\<config>\YTMusicWP.build.appxrecipe`, `MakeAppx pack /l`, then `AppDeployCmd /install` or `/installlaunch`. `-Enumerate` lists device indexes (0 is the attached phone). Total ~53 s including the build.

## What does NOT work

- **VS's own ARM deploy (F5)** - DEP6810 / `MDILXapCompile.exe ... 2001`, real error at the end of `obj\ARM\<config>\MDIL\MDILXapCompileLog.txt`: `Error processing assembly ...\MSIL\Newtonsoft.Json.dll` / `Raw error code: 2148733978` = `0x8013141A` = strong name validation failed. Its `mdilProcessor` (`Microsoft.VisualStudio.TailoredProjectServices.dll`) runs MDIL **twice** over the same `obj\ARM\<config>\MSIL` folder: pass 1 compiles all 15 files from the plain binaries, then the compiled images are written back into that same folder, and pass 2 recompiles them and dies on the first strong-named assembly (Newtonsoft.Json 13.0.4, then System.ComponentModel.Primitives/TypeConverter, System.Runtime.InteropServices.RuntimeInformation). Reproduced twice (06:40 and 07:03 on 2026-10-14), pass 2 dying ~2 s after pass 1. Non-strong-named libraries (SQLite-net, XamlAnimatedGif) are why an app can look "almost working".
- **MDIL-compiling on the host ourselves and handing the result to AppDeployCmd** - `Errore: Deployment optimization failed due to an assembly that's not valid.` AppDeployCmd re-validates/re-optimizes and rejects images that were already compiled for it.
- **Running the SDK's own optimizer first** (`MDILXAPCompile\BuildMDILAPPX.ps1`) and deploying its `_optimized` appx - `Errore: Deployment Optimization failed with error "Unable to read input file." Ensure that the input file is not already optimized for the target device.` Same reason, seen from the other side.
- **A project-side guard** - there is none available. A `DropStaleMdilIntermediates` target (RemoveDir `obj\$(Platform)\$(Configuration)\MSIL` + `MDIL`, `BeforeTargets="Build;Clean"`, ARM only) was added on 2026-10-14 and **did not prevent the failure** (reproduced with it in place at 07:03); it was removed again and `YTMusicWP.csproj` now only carries a comment saying not to re-add such a target. Deleting the state makes the engine do a *full* pass 1, which is exactly the path that then runs pass 2 into the trap; it also wipes the state VS would otherwise reuse. VS's Clean/Rebuild never removed those folders either (they are not in `FileListAbsolute.txt`).

## Where MDIL actually lives (probe of 2026-10-14)

- **A plain ARM build never runs MDIL.** `MSBuild.exe YTMusicWP.csproj -p:Configuration=Debug -p:Platform=ARM` finished with exit 0 and produced the appx in `YTMusicWP\AppPackages\YTMusicWP_2.4.0.0_ARM_Debug_Test\` (4.37 MB, unoptimized), while `find obj bin -iname "*mdil*" -o -iname "*msil*"` came back **empty**. `MSIL`/`MDIL` only ever appear under `obj\ARM\<config>\` when VS does the ARM *deploy*, never after a command-line build. Do not go looking for a build switch for them.
- **The MDIL step lives entirely in VS's deploy assemblies, not in MSBuild.** `MDIL` appears in `Microsoft.VisualStudio.TailoredProjectServices.dll` (the `mdilProcessor`), `Microsoft.VisualStudio.SmartDevice.DeployTask.dll`, `Microsoft.VisualStudio.SmartDevice.ProjectSystem.Base.dll` / `...Base.Utility.dll`, `...NativeProjectSystem.dll` (all under `Common7\IDE\`). The *only* MSBuild reference to `MDILCompile` is `Microsoft.Phone.Packaging.targets` (v8.0 and v8.1): it sets `MDILGeneration = $(MDILCompile)` (lines 444-445) only when `ProjectTypeGuids` is the Silverlight C#/VB guid pair **and** `XapOutputs == true`, then passes it to the `XapPackager` task (line 462). This project has neither, so no `.targets`/`.props`/`.csproj`/registry lever reaches that step.

## Facts worth keeping

- Only the **ARM** deploy uses MDIL: `obj\x86\Debug` has no `MSIL`/`MDIL` at all, so emulator deploys never show it. The trap is specific to a device deploy; `AppDeployCmd /targetdevice:0` = the attached phone, indexes 1+ are the emulators.
- `MDILXapCompile.exe` **aborts the whole run** when an explicit `/CompileFilter` lists a native DLL: `Error compiling ...\e_sqlite3.dll: The image being compiled is not a .NET assembly (Exception from HRESULT: 0x80131F0B)`. The SDK's supported `BuildMDILAPPX.ps1` passes **no** `/CompileFilter` at all - just `/in`, `/out`, `/log`, `/config`, `/appx` over the whole unpacked package - and the tool copies the non-IL files itself ("Crossgen rejected ... as not a managed assembly. Copying."). Native files in the package: `e_sqlite3.dll`, `Lumia.Imaging.dll`, `Lumia.InteropServices.WindowsRuntime.dll`. Managed ones carry a CLR header (data directory 14 non-zero), which is how to tell them apart from Python if needed.
- An appx produced by the SDK optimizer contains `MDILFileList.xml` (the list of optimized files, e.g. 15 entries), the plain one does not; both contain `AppxBlockMap.xml`. Sizes: plain appx 4.17 MB, MDIL-compiled 5.8 MB.
- Fast triage if DEP6810 shows up again: read `MDILXapCompileLog.txt` for `Error processing assembly` + `Raw error code`, and check for a `.mdil` section in `MSIL\*.dll` (Python: parse the PE header at `e_lfanew`, walk the section table) - a compiled copy is what recompiling chokes on.

**Why:** the error text (`DEP6810`, `MdilXapCompile`, `2001`) points at nothing in the project and reads like a NuGet/compiler problem, so it is easy to lose a session re-diagnosing it - and the working answer (let AppDeployCmd optimize) is not discoverable from the error at all.
**How to apply:** when the user wants the build on a phone, run `powershell -ExecutionPolicy Bypass -File tools\Build-ArmAppx.ps1 -Install 0 -Launch`; never hand AppDeployCmd an appx that was MDIL-compiled, and do not add MSIL/MDIL cleanup targets to the csproj. The app itself always compiled and packaged fine.

Related: [[wp81-platform-gotchas]], [[user-tests-on-emulator]]
