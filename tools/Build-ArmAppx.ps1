<#
.SYNOPSIS
    Builds YTMusicWP for ARM, packs the .appx and deploys it to a phone with AppDeployCmd.

.DESCRIPTION
    Visual Studio's ARM deploy fails with DEP6810 / MDILXapCompile 2001: its MDIL processor runs
    the compiler over obj\ARM\<config>\MSIL twice, the first pass leaves the compiled images in
    that same folder, and the second pass then rejects the already-compiled Newtonsoft.Json.dll
    ("Strong name validation failed", 0x8013141A). See .agents/memory/mdil-deploy-trap.md.

    AppDeployCmd installs fine, but it performs the deployment optimization itself, so it must be
    handed the *unoptimized* appx: an appx that was MDIL-compiled on the host is refused with
    "Unable to read input file. Ensure that the input file is not already optimized for the target
    device", and one that was compiled with a wrong filter with "an assembly that's not valid".
    This script therefore only builds, packs and hands the package to AppDeployCmd - no MDIL step
    of its own.

.PARAMETER Install
    Device index from `AppDeployCmd /EnumerateDevices` (0 is the attached phone).
    Omit it to only build the .appx.

.PARAMETER Launch
    Launch the app once it is installed (AppDeployCmd /installlaunch).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Build-ArmAppx.ps1
    powershell -ExecutionPolicy Bypass -File tools\Build-ArmAppx.ps1 -Configuration Release
    powershell -ExecutionPolicy Bypass -File tools\Build-ArmAppx.ps1 -SkipBuild -Install 0 -Launch
    powershell -ExecutionPolicy Bypass -File tools\Build-ArmAppx.ps1 -Enumerate
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Debug',
    [switch] $SkipBuild,
    [int] $Install = -1,
    [switch] $Launch,
    [switch] $Enumerate
)

$ErrorActionPreference = 'Stop'

function Write-Step([string] $text) { Write-Host "==> $text" -ForegroundColor Cyan }
function Write-Detail([string] $text) { Write-Host "    $text" -ForegroundColor DarkGray }
function Fail([string] $text) { Write-Host "ERROR: $text" -ForegroundColor Red; exit 1 }

function Resolve-Tool([string[]] $candidates, [string] $what) {
    foreach ($c in $candidates) { if ($c -and (Test-Path -LiteralPath $c)) { return $c } }
    Fail "$what not found. Looked in:`n    " + ($candidates -join "`n    ")
}

# Native tools write warnings to stderr; with $ErrorActionPreference = 'Stop' PowerShell would turn
# that into a terminating error, so run them relaxed and capture both streams, then branch on the
# exit code instead.
function Invoke-Tool([string] $exe, [string[]] $arguments, [string] $logPath) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $exe @arguments 2>&1 | Out-File -LiteralPath $logPath -Encoding utf8 -Force
        return $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }
}

function Show-LogTail([string] $logPath, [int] $lines = 15) {
    if (Test-Path -LiteralPath $logPath) {
        Get-Content -LiteralPath $logPath -Tail $lines | ForEach-Object { Write-Detail $_ }
    }
}

$programFilesX86 = ${env:ProgramFiles(x86)}
if (-not $programFilesX86) { $programFilesX86 = ${env:ProgramFiles} }

$root = Split-Path -Parent $PSScriptRoot
$cfg = $Configuration
$projectDir = Join-Path $root 'YTMusicWP'
$layout = Join-Path $projectDir "bin\ARM\$cfg"
$objDir = Join-Path $projectDir "obj\ARM\$cfg"
$recipePath = Join-Path $layout 'YTMusicWP.build.appxrecipe'

$msbuild = Resolve-Tool @(
    (Join-Path $programFilesX86 'MSBuild\14.0\Bin\MSBuild.exe'),
    (Join-Path $programFilesX86 'MSBuild\12.0\Bin\MSBuild.exe'),
    (Join-Path $env:ProgramFiles 'MSBuild\14.0\Bin\MSBuild.exe')
) 'MSBuild.exe'

$winKit = Resolve-Tool @(
    (Join-Path $programFilesX86 'Windows Kits\8.1\bin\x64'),
    (Join-Path $programFilesX86 'Windows Kits\10\bin\x64')
) 'the Windows Kits bin folder'
$makeAppx = Join-Path $winKit 'makeappx.exe'
$appDeploy = Join-Path $programFilesX86 'Microsoft SDKs\Windows Phone\v8.1\Tools\AppDeploy\AppDeployCmd.exe'

if ($Enumerate) {
    if (-not (Test-Path -LiteralPath $appDeploy)) { Fail "AppDeployCmd.exe not found at $appDeploy" }
    & $appDeploy /EnumerateDevices
    exit $LASTEXITCODE
}

# ---------------------------------------------------------------- build
if (-not $SkipBuild) {
    Write-Step "Building ARM $cfg with $msbuild"
    $buildLog = Join-Path $env:TEMP 'YTMusicWP-arm-build.log'
    $buildExit = Invoke-Tool $msbuild @((Join-Path $root 'YTMusicWP.sln'), '-nologo', '-v:m', "-p:Configuration=$cfg", '-p:Platform=ARM') $buildLog
    if ($buildExit -ne 0) {
        Show-LogTail $buildLog 20
        Fail "the ARM $cfg build failed (exit $buildExit, log: $buildLog)"
    }
}
else {
    Write-Detail '-SkipBuild: reusing the existing layout'
}

foreach ($p in @($layout, $recipePath)) {
    if (-not (Test-Path -LiteralPath $p)) { Fail "$p is missing - build ARM $cfg first (or drop -SkipBuild)" }
}

# ---------------------------------------------------------------- payload
# The recipe is the file list VS packages, and it resolves to the plain layout outputs.
$work = Join-Path $objDir 'ArmAppx'
$payloadDir = Join-Path $work 'payload'
if (Test-Path -LiteralPath $payloadDir) { Remove-Item -LiteralPath $payloadDir -Recurse -Force }
New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null

Write-Step 'Laying out the payload from YTMusicWP.build.appxrecipe'
[xml] $recipe = Get-Content -LiteralPath $recipePath
$ns = New-Object System.Xml.XmlNamespaceManager($recipe.NameTable)
$ns.AddNamespace('msb', 'http://schemas.microsoft.com/developer/msbuild/2003')

$packaged = @()
foreach ($node in $recipe.SelectNodes('//msb:AppxPackagedFile | //msb:AppXManifest', $ns)) {
    $source = $node.GetAttribute('Include')
    $pathNode = $node.SelectSingleNode('msb:PackagePath', $ns)
    if (-not $source -or -not $pathNode) { continue }
    $packaged += [pscustomobject]@{
        Source = [System.IO.Path]::GetFullPath($source)
        Relative = $pathNode.InnerText.Trim().Replace('\', '/')
    }
}
if ($packaged.Count -lt 10) { Fail "the recipe only lists $($packaged.Count) files: has its shape changed?" }

$missing = @()
foreach ($item in $packaged) {
    $target = Join-Path $payloadDir ($item.Relative -replace '/', '\')
    $targetFolder = Split-Path -Parent $target
    if (-not (Test-Path -LiteralPath $targetFolder)) { New-Item -ItemType Directory -Path $targetFolder -Force | Out-Null }
    if (Test-Path -LiteralPath $item.Source) { Copy-Item -LiteralPath $item.Source -Destination $target -Force }
    else { $missing += $item.Source }
}
if ($missing.Count) { Fail "the recipe points at files that do not exist:`n    " + ($missing -join "`n    ") }
Write-Detail "$($packaged.Count) files laid out"

$manifestPath = Join-Path $payloadDir 'AppxManifest.xml'
if (-not (Test-Path -LiteralPath $manifestPath)) { Fail "the recipe did not lay out AppxManifest.xml" }
[xml] $manifest = Get-Content -LiteralPath $manifestPath
$identity = $manifest.Package.Identity
$productId = $identity.Name
$appx = Join-Path $work "YTMusicWP_$($identity.Version)_$($identity.ProcessorArchitecture)_$cfg.appx"

# ---------------------------------------------------------------- pack
Write-Step "Packing $appx"
if (Test-Path -LiteralPath $appx) { Remove-Item -LiteralPath $appx -Force }
# /l disables the manifest's file-existence validation: it names Assets\Logo.png while the package
# ships the scale-qualified Assets\Logo.scale-XXX.png that resources.pri resolves
$makeAppxLog = Join-Path $work 'MakeAppxLog.txt'
$packExit = Invoke-Tool $makeAppx @('pack', '/d', $payloadDir, '/p', $appx, '/o', '/l') $makeAppxLog
if ($packExit -ne 0) {
    Show-LogTail $makeAppxLog 15
    Fail "MakeAppx failed with exit code $packExit (log: $makeAppxLog)"
}
$appxSize = [math]::Round((Get-Item -LiteralPath $appx).Length / 1MB, 2)
Write-Host ''
Write-Host "Package : $appx ($appxSize MB)" -ForegroundColor Green

# ---------------------------------------------------------------- install
if ($Install -ge 0) {
    if (-not (Test-Path -LiteralPath $appDeploy)) { Fail "AppDeployCmd.exe not found at $appDeploy" }
    $command = if ($Launch) { '/installlaunch' } else { '/install' }
    Write-Step "AppDeployCmd $command /targetdevice:$Install (it optimizes the package for the device itself, ~45 s)"
    $deployLog = Join-Path $work 'AppDeployCmdLog.txt'
    $deployExit = Invoke-Tool $appDeploy @($command, $appx, "/targetdevice:$Install") $deployLog
    $output = if (Test-Path -LiteralPath $deployLog) { (Get-Content -LiteralPath $deployLog -Raw).Trim() } else { '' }
    if ($deployExit -ne 0 -or $output -match 'Errore|Error') {
        Show-LogTail $deployLog 12
        Fail "AppDeployCmd failed with exit code $deployExit (log: $deployLog)"
    }
    Write-Detail $output
    $verb = if ($Launch) { 'Installed and launched' } else { 'Installed' }
    Write-Host "$verb on device $Install ($productId)." -ForegroundColor Green
}
else {
    Write-Host "Next   : powershell -ExecutionPolicy Bypass -File tools\Build-ArmAppx.ps1 -SkipBuild -Install <deviceIndex> -Launch"
    Write-Host 'Devices: powershell -ExecutionPolicy Bypass -File tools\Build-ArmAppx.ps1 -Enumerate' -ForegroundColor DarkGray
}
