param(
    [string]$Runtime = 'C:\Program Files (x86)\Steam\steamapps\common\SteamVR\steamxr_win32.json',
    [switch]$Render
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$probe = Join-Path $root 'build\ninja\bin\xr_probe.exe'
if (!(Test-Path -LiteralPath $probe)) { throw 'Build the Win32 targets first with tools\build.cmd.' }
if (!(Test-Path -LiteralPath $Runtime)) { throw "SteamVR x86 manifest not found: $Runtime" }
$output = Join-Path $root ('artifacts\xr-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $output | Out-Null
$previousRuntime = $env:XR_RUNTIME_JSON
$previousOutput = $env:DIRT2VR_OUTPUT
try {
    # Process-local override; never change the machine's active OpenXR runtime.
    $env:XR_RUNTIME_JSON = (Resolve-Path -LiteralPath $Runtime).Path
    $env:DIRT2VR_OUTPUT = $output
    $arguments = @()
    if ($Render) { $arguments += '--render' }
    & $probe @arguments 2>&1 | Tee-Object -FilePath (Join-Path $output 'probe.txt')
    $code = $LASTEXITCODE
    if ($code -ne 0) { throw "OpenXR diagnostic failed with exit code $code. See $output" }
    Write-Host "Diagnostic passed. Receipt: $output"
} finally {
    $env:XR_RUNTIME_JSON = $previousRuntime
    $env:DIRT2VR_OUTPUT = $previousOutput
}
