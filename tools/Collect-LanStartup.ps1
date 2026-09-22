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
# The loader's module list can be empty while an import-error dialog is open.
# Query image mappings instead; this requests read-only process access and reads no save/game memory.
if (-not ('DiRT2VR.StartupImages' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
namespace DiRT2VR {
    public static class StartupImages {
        [StructLayout(LayoutKind.Sequential)]
        struct MemoryInfo {
            public IntPtr BaseAddress, AllocationBase;
            public uint AllocationProtect;
            public UIntPtr RegionSize;
            public uint State, Protect, Type;
        }
        [DllImport("kernel32.dll", SetLastError=true)]
        static extern IntPtr OpenProcess(uint access, bool inherit, int id);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError=true)]
        static extern UIntPtr VirtualQueryEx(IntPtr process, IntPtr address, out MemoryInfo info, UIntPtr size);
        [DllImport("psapi.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        static extern uint GetMappedFileNameW(IntPtr process, IntPtr address, StringBuilder path, uint length);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)]
        static extern uint QueryDosDevice(string name, StringBuilder target, int size);
        static string DosPath(string path) {
            foreach (string drive in Environment.GetLogicalDrives()) {
                var target = new StringBuilder(32768);
                if (QueryDosDevice(drive.Substring(0,2), target, target.Capacity) != 0 &&
                    path.StartsWith(target.ToString() + "\\", StringComparison.OrdinalIgnoreCase))
                    return drive.Substring(0,2) + path.Substring(target.Length);
            }
            return path;
        }
        public static string[] Read(int id) {
            IntPtr process = OpenProcess(0x410, false, id); // QUERY_INFORMATION | VM_READ
            if (process == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try {
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var bases = new HashSet<IntPtr>();
                ulong address = 0;
                ulong limit = IntPtr.Size == 8 ? (ulong)long.MaxValue : uint.MaxValue;
                MemoryInfo info;
                while (address < limit && VirtualQueryEx(process, new IntPtr(unchecked((long)address)),
                    out info, (UIntPtr)Marshal.SizeOf(typeof(MemoryInfo))) != UIntPtr.Zero) {
                    if (info.Type == 0x1000000 && bases.Add(info.AllocationBase)) { // MEM_IMAGE
                        var path = new StringBuilder(32768);
                        if (GetMappedFileNameW(process, info.AllocationBase, path, (uint)path.Capacity) != 0)
                            paths.Add(DosPath(path.ToString()));
                    }
                    ulong next = unchecked((ulong)info.BaseAddress.ToInt64()) + info.RegionSize.ToUInt64();
                    if (next <= address) break;
                    address = next;
                }
                var result = new List<string>(paths);
                result.Sort(StringComparer.OrdinalIgnoreCase);
                return result.ToArray();
            } finally { CloseHandle(process); }
        }
    }
}
'@
}
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
    $processes = @(Get-CimInstance Win32_Process -Filter "Name='dirt2_game.exe' OR Name='dirt2.exe' OR Name='dirt2o.exe' OR Name='DiRT2VR.exe'" | ForEach-Object {
        if ($_.ExecutablePath) { [void]$roots.Add([IO.Path]::GetDirectoryName($_.ExecutablePath)) }
        [ordered]@{ Name=$_.Name; Id=$_.ProcessId; ParentId=$_.ParentProcessId; Path=$_.ExecutablePath; CommandLine=$_.CommandLine }
    })
} catch { $issues.Add('Process query: '+$_.Exception.Message) }
$modules = @()
$images = @()
foreach ($process in @(Get-Process dirt2,dirt2o,dirt2_game,DiRT2VR -ErrorAction SilentlyContinue)) {
    try {
        if ($process.Path) { [void]$roots.Add([IO.Path]::GetDirectoryName($process.Path)) }
        $modules += @($process.Modules | Where-Object { $_.ModuleName -match '^(xlive|dbxLive32|d3d11|rld|secemu|launcher_interface|msidcrl40|wlidcli)\.dll$' } | ForEach-Object { [ordered]@{ ProcessId=$process.Id; Module=$_.ModuleName; Path=$_.FileName } })
    } catch { $issues.Add('Modules for process '+$process.Id+': '+$_.Exception.Message) }
    try {
        $images += @([DiRT2VR.StartupImages]::Read($process.Id) | ForEach-Object { [ordered]@{ ProcessId=$process.Id; Path=$_ } })
    } catch { $issues.Add('Image mappings for process '+$process.Id+': '+$_.Exception.Message) }
    finally { $process.Dispose() }
}
$installations = @()
foreach ($root in $roots) {
    $files = @('DiRT2VR.exe','dirt2.exe','dirt2o.exe','rld.dll','dirt2_game.exe','dirt2_game.exe.cfg','dirt2_game.exe.manifest','xlive.dll','dbxLive32.dll','msidcrl40.dll','DiRT2VR/payload/xlive-lan.dll') | ForEach-Object { FileFacts (Join-Path $root $_) }
    $rootFiles = @(Get-ChildItem -LiteralPath $root -Force -ErrorAction SilentlyContinue | Where-Object { $_.Extension -in @('.dll','.exe','.manifest','.local','.cfg','.ini') } | ForEach-Object { [ordered]@{Name=$_.Name;Bytes=$_.Length;Directory=$_.PSIsContainer} })
    $compatibility = @()
    foreach ($hive in @('HKCU','HKLM')) {
        $key = $hive+':\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers'
        if (Test-Path -LiteralPath $key) {
            try {
                $properties = Get-ItemProperty -LiteralPath $key
                foreach ($name in @('dirt2.exe','dirt2o.exe','dirt2_game.exe','DiRT2VR.exe')) {
                    $exe = Join-Path $root $name
                    $property = $properties.PSObject.Properties[$exe]
                    if ($null -ne $property) { $compatibility += [ordered]@{Hive=$hive;Path=$exe;Layers=$property.Value} }
                }
            } catch { $issues.Add('Compatibility settings '+$key+': '+$_.Exception.Message) }
        }
    }
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
    $installations += [ordered]@{Root=$root;Files=@($files);RootFiles=$rootFiles;Compatibility=$compatibility;Package=$manifest;PendingLAN=$journal;Session=$status}
}
$report = [ordered]@{
    CollectorVersion=2
    CollectedUtc=[DateTime]::UtcNow.ToString('o')
    Note='Run while the ordinal error is open; close it only after collecting. No save contents or network credentials are collected.'
    OS=[Environment]::OSVersion.VersionString
    Collector64Bit=[Environment]::Is64BitProcess
    Processes=$processes
    LoadedModules=$modules
    MappedImages=$images
    Installations=$installations
    SystemLibraries=@(foreach ($folder in @('System32','SysWOW64')) { foreach ($name in @('xlive.dll','msidcrl40.dll','wlidcli.dll')) { FileFacts (Join-Path $env:WINDIR ($folder+'/'+$name)) } })
    Errors=@($issues.ToArray())
}
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
Write-Host ('Report saved: '+$OutputPath)
if (!$roots.Count) { Write-Warning 'No running game/launcher was found. Keep the error dialog and launcher open, or rerun with -GameRoot pointing to the game folder.' }
if (!$NoOpen) { & notepad.exe $OutputPath }
