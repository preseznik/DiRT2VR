# Read-only startup evidence. Does not change game files, saves, DLLs or Windows settings.
param(
    [string]$GameRoot,
    [string]$OutputPath = (Join-Path ([Environment]::GetFolderPath('Desktop')) 'DiRT2VR-LAN-startup.json'),
    [switch]$NoOpen
)
$ErrorActionPreference = 'Stop'
$issues = New-Object 'System.Collections.Generic.List[string]'
$roots = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
if ($GameRoot) { [void]$roots.Add([IO.Path]::GetFullPath($GameRoot)) }
function FileFacts([string]$Path) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) { return [ordered]@{ Path=$Path; Exists=$false } }
    try {
        $file = Get-Item -LiteralPath $Path
        $reader = New-Object IO.BinaryReader ([IO.File]::OpenRead($Path))
        try {
            $machine = $null
            if ($reader.ReadUInt16() -eq 0x5A4D) {
                $reader.BaseStream.Position = 0x3C
                $pe = $reader.ReadUInt32()
                if ($pe -le $reader.BaseStream.Length - 6) {
                    $reader.BaseStream.Position = $pe
                    if ($reader.ReadUInt32() -eq 0x4550) { $machine = ('0x{0:X4}' -f $reader.ReadUInt16()) }
                }
            }
        } finally { $reader.Dispose() }
        return [ordered]@{ Path=$file.FullName; Exists=$true; Bytes=$file.Length; Machine=$machine; SHA256=(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash; FileVersion=$file.VersionInfo.FileVersion; ProductVersion=$file.VersionInfo.ProductVersion }
    } catch { return [ordered]@{ Path=$Path; Error=$_.Exception.Message } }
}
$processes = @()
try {
    $processes = @(Get-CimInstance Win32_Process -Filter "Name='dirt2_game.exe' OR Name='dirt2.exe' OR Name='DiRT2VR.exe'" | ForEach-Object {
        if ($_.ExecutablePath) { [void]$roots.Add([IO.Path]::GetDirectoryName($_.ExecutablePath)) }
        [ordered]@{ Name=$_.Name; Id=$_.ProcessId; ParentId=$_.ParentProcessId; Path=$_.ExecutablePath; CommandLine=$_.CommandLine }
    })
} catch { $issues.Add('Process query: '+$_.Exception.Message) }
$modules = @()
foreach ($process in @(Get-Process dirt2,dirt2_game,DiRT2VR -ErrorAction SilentlyContinue)) {
    try {
        if ($process.Path) { [void]$roots.Add([IO.Path]::GetDirectoryName($process.Path)) }
        $modules += @($process.Modules | Where-Object { $_.ModuleName -match '^(xlive|dbxLive32|d3d11)\.dll$' } | ForEach-Object { [ordered]@{ ProcessId=$process.Id; Module=$_.ModuleName; Path=$_.FileName } })
    } catch { $issues.Add('Modules for process '+$process.Id+': '+$_.Exception.Message) }
    finally { $process.Dispose() }
}
$installations = @()
foreach ($root in $roots) {
    $files = @('DiRT2VR.exe','dirt2.exe','dirt2_game.exe','xlive.dll','dbxLive32.dll','DiRT2VR/payload/xlive-lan.dll') | ForEach-Object { FileFacts (Join-Path $root $_) }
    $manifest = $null; $journal = $null; $status = $null
    try {
        $path = Join-Path $root 'DiRT2VR/package.json'
        if (Test-Path -LiteralPath $path) { $value=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json; $manifest=[ordered]@{Version=$value.Version; LanPayloadHash=$value.Files.'DiRT2VR/payload/xlive-lan.dll'} }
        $path = Join-Path $root 'DiRT2VR/lan-backups/pending.json'
        if (Test-Path -LiteralPath $path) { $journal=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json }
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $id=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($root.TrimEnd('\').ToUpperInvariant())))).Replace('-','').Substring(0,24) } finally { $sha.Dispose() }
        $path = Join-Path $env:LOCALAPPDATA ('DiRT2VR/'+$id+'/session.json')
        if (Test-Path -LiteralPath $path) { $status=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json }
    } catch { $issues.Add('Installation metadata '+$root+': '+$_.Exception.Message) }
    $installations += [ordered]@{Root=$root;Files=@($files);Package=$manifest;PendingLAN=$journal;Session=$status}
}
$report = [ordered]@{
    CollectedUtc=[DateTime]::UtcNow.ToString('o')
    Note='Run while the ordinal error is open; close it only after collecting. No save contents or network credentials are collected.'
    OS=[Environment]::OSVersion.VersionString
    Collector64Bit=[Environment]::Is64BitProcess
    Processes=$processes
    LoadedModules=$modules
    Installations=$installations
    SystemLibraries=@((FileFacts (Join-Path $env:WINDIR 'System32/xlive.dll')),(FileFacts (Join-Path $env:WINDIR 'SysWOW64/xlive.dll')))
    Errors=@($issues.ToArray())
}
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
Write-Host ('Report saved: '+$OutputPath)
if (!$roots.Count) { Write-Warning 'No running game/launcher was found. Keep the error dialog and launcher open, or rerun with -GameRoot pointing to the game folder.' }
if (!$NoOpen) { & notepad.exe $OutputPath }
