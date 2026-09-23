param([ValidateSet('Start','Inspect','Stop')][string]$Action = 'Inspect', [string]$Track = 'd2vr_test', [string]$Route = 'route_0')
$ErrorActionPreference = 'Stop'
$lab = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts/track-prototype'))
$game = Join-Path $lab 'game'
$ownerFile = Join-Path $lab 'process.json'
if ($Action -eq 'Start') {
    if (Get-Process dirt2,dirt2_game -ErrorAction SilentlyContinue) { throw 'A game is already running' }
    if ($Track -notin @('d2vr_test','battersea') -or $Route -notin @('route_0','route_1')) { throw 'Unknown test selection' }
    if ((Get-FileHash (Join-Path $game 'dirt2_game.exe')).Hash -ne '49B1E00EA1D4BD02E633CEED63390B5CAE07601333F673C4EFD8D2F0EB54FE48') { throw 'Unsupported test executable' }
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
    $evidence = Join-Path $lab "runs/$stamp"
    New-Item -ItemType Directory -Path $evidence -Force | Out-Null
    $xml = '<config skipreplays="true"><track country="london" name="'+$Track+'" route="'+$Route+'"><car name="sti" number="1" /></track></config>'
    [IO.File]::WriteAllText((Join-Path $game 'p.xml'), $xml)
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $game 'dirt2.exe'))
    $start.UseShellExecute = $false; $start.WorkingDirectory = $game
    foreach ($key in @($start.Environment.Keys | Where-Object { $_ -like 'DIRT2VR_*' })) { $start.Environment.Remove($key) | Out-Null }
    $start.Environment['DIRT2VR_ACTIVE']='1'
    $start.Environment['DIRT2VR_DESKTOP_PRACTICE']='1'
    $start.Environment['DIRT2VR_DIRECT_PRACTICE']='1'
    $start.Environment['DIRT2VR_CAPTURE_DIAGNOSTICS']='0'
    $start.Environment['DIRT2VR_LOGGING']='1'
    $start.Environment['DIRT2VR_OUTPUT']=$evidence
    $start.ArgumentList.Add('-demo'); $start.ArgumentList.Add('p.xml')
    $wrapper = [Diagnostics.Process]::Start($start)
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 250
        $child = Get-Process dirt2_game -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $game 'dirt2_game.exe') } | Select-Object -First 1
    } until ($child -or [DateTime]::UtcNow -gt $deadline)
    if (!$child) { throw 'No owned game process appeared' }
    [ordered]@{ Pid=$child.Id; StartTime=$child.StartTime.ToUniversalTime().ToString('O'); Path=$child.Path; Track=$Track; Route=$Route; Evidence=$evidence } | ConvertTo-Json | Set-Content $ownerFile
    Get-Content $ownerFile
    return
}
$owner = Get-Content $ownerFile -Raw | ConvertFrom-Json
$child = Get-Process -Id $owner.Pid -ErrorAction SilentlyContinue
if (!$child -and $Action -eq 'Stop') { return }
if (!$child) { throw 'The owned test process has exited' }
if ($child.Path -ne (Join-Path $game 'dirt2_game.exe') -or $child.StartTime.ToUniversalTime().Ticks -ne ([DateTime]$owner.StartTime).ToUniversalTime().Ticks) { throw 'Owned process identity changed' }
if ($Action -eq 'Stop') {
    $child.CloseMainWindow() | Out-Null
    if (!$child.WaitForExit(5000)) { $child.Kill(); $child.WaitForExit() }
    return
}
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class TrackRead {
 [DllImport("kernel32.dll",SetLastError=true)] public static extern IntPtr OpenProcess(uint access,bool inherit,uint id);
 [DllImport("kernel32.dll",SetLastError=true)] public static extern bool ReadProcessMemory(IntPtr p,IntPtr address,byte[] data,UIntPtr size,out UIntPtr read);
 [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr p);
}
'@
$handle = [TrackRead]::OpenProcess(0x1010,$false,$child.Id)
if ($handle -eq [IntPtr]::Zero) { throw 'Cannot inspect owned process' }
try {
    $bytes=[byte[]]::new(0x1000); $read=[UIntPtr]::Zero
    if (![TrackRead]::ReadProcessMemory($handle,[IntPtr]($child.MainModule.BaseAddress.ToInt64()+0x1051130),$bytes,[UIntPtr]$bytes.Length,[ref]$read)) { throw 'Config memory read failed' }
    function S([int]$offset) { [Text.Encoding]::ASCII.GetString($bytes,$offset,32).Split([char]0)[0] }
    $result=[ordered]@{ Pid=$child.Id; Country=(S 0x8c); Track=(S 0xac); Route=(S 0xcc); Car=(S 0xec); Count=[BitConverter]::ToUInt32($bytes,0xef0); EvidenceLevel='Parsed configuration only; screenshot and driving required' }
    $result | ConvertTo-Json | Tee-Object -FilePath (Join-Path $owner.Evidence 'parsed.json')
    if ($result.Track -ne $owner.Track -or $result.Route -ne $owner.Route -or $result.Count -ne 1) { throw 'Selection mismatch' }
} finally { [TrackRead]::CloseHandle($handle) | Out-Null }
