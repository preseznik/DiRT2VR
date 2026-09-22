param([string]$GameRoot=(Split-Path $PSScriptRoot -Parent),[switch]$RecoverOnly)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'LanFiles.ps1')
$GameRoot=[IO.Path]::GetFullPath($GameRoot)
$guard=New-Object Threading.Mutex($false,'Global\DiRT2VR.Session')
$held=$false
function Assert-GameClosed {
    if (Get-Process dirt2,dirt2_game -ErrorAction SilentlyContinue) { throw 'Close DiRT 2 before LAN preparation or recovery.' }
}
try {
    try { $held=$guard.WaitOne(0) } catch [Threading.AbandonedMutexException] { $held=$true }
    if (!$held) { throw 'Another DiRT2VR session or recovery is running.' }
    Assert-GameClosed
    Assert-LanPath $GameRoot
    if ((Get-LanHash (Join-Path $GameRoot 'dirt2_game.exe')) -ne '49B1E00EA1D4BD02E633CEED63390B5CAE07601333F673C4EFD8D2F0EB54FE48') { throw 'Select the supported DiRT 2 1.1 installation.' }
    Restore-LanShim $GameRoot
    if ($RecoverOnly) { Write-Host 'Original xlive.dll restored.'; return }
    foreach ($pending in @('DiRT2VR/backups/pending.json')) { if (Test-Path (Join-Path $GameRoot $pending)) { throw 'Use the normal launcher to restore its pending session first.' } }
    $manifest=Get-Content (Join-Path $PSScriptRoot 'lab-package.json') -Raw | ConvertFrom-Json
    foreach ($entry in $manifest.Files.PSObject.Properties) {
        if ($entry.Name -match '[/\\:]|^\.\.$' -or (Get-LanHash (Join-Path $PSScriptRoot $entry.Name)) -ne $entry.Value) { throw 'LAN test package is incomplete or changed.' }
    }
    $payload=Join-Path $PSScriptRoot 'xlive-lan.dll'
    $user=Join-Path $PSScriptRoot 'user'
    $documents=Join-Path $user 'Documents'
    Assert-LanPath $documents
    [IO.Directory]::CreateDirectory($documents) | Out-Null
    $config=Join-Path $user 'xlln.ini'
    if (!(Test-Path -LiteralPath $config)) {
        $name='LAN-'+[guid]::NewGuid().ToString('N').Substring(0,8)
        $text="[XLLN-Config-Version:1.6.2.1]`r`nxlive_username_p1 = $name`r`nxlive_user_live_enabled_p1 = 0`r`nxlive_user_online_enabled_p1 = 0`r`nxlive_user_auto_login_p1 = 1`r`nxlive_fps_limit = 0`r`nxlive_net_disable = 0`r`nxlive_xhv_engine_enabled = 0`r`nxlln_debug_log_level = 0x00000000`r`n"
        Write-LanAtomic $config ([Text.Encoding]::ASCII.GetBytes($text))
    }
    $receipt=Join-Path $documents 'profile-ready.txt'
    if (Test-Path -LiteralPath $receipt) { [IO.File]::Delete($receipt) }
    Install-LanShim $GameRoot $payload $manifest.Files.'xlive-lan.dll'
    $start=New-Object Diagnostics.ProcessStartInfo
    $start.FileName=Join-Path $GameRoot 'dirt2.exe'; $start.WorkingDirectory=$GameRoot; $start.UseShellExecute=$false
    foreach ($key in @($start.EnvironmentVariables.Keys)) { if ($key -like 'DIRT2VR_*') { $start.EnvironmentVariables.Remove($key) } }
    $start.EnvironmentVariables['DIRT2VR_ACTIVE']='0'
    $start.EnvironmentVariables['DIRT2VR_LAN_CONFIG']=$config
    $start.EnvironmentVariables['DIRT2VR_LAN_DOCUMENTS']=$documents
    Write-Host 'LAN desktop test: use the native Multiplayer/LAN menus. This is a separate fresh profile.'
    Write-Host 'Keep this window open. Quit DiRT 2 normally to restore xlive.dll.'
    $child=[Diagnostics.Process]::Start($start)
    $deadline=[DateTime]::UtcNow.AddSeconds(45); $seen=$false
    try {
        do {
            $game=@(Get-Process dirt2_game -ErrorAction SilentlyContinue)
            if ($game.Count) { $seen=$true }
            $wrapper=@(Get-Process dirt2 -ErrorAction SilentlyContinue)
            if ($seen -and !$game.Count -and !$wrapper.Count) { break }
            if (!$seen -and [DateTime]::UtcNow -gt $deadline -and !$game.Count -and !$wrapper.Count) { throw 'DiRT 2 did not start.' }
            Start-Sleep -Milliseconds 500
        } while ($true)
    } finally { $child.Dispose() }
    if (!(Test-Path -LiteralPath $receipt)) { throw 'The game exited before profile isolation was confirmed.' }
} finally {
    if ($held) {
        try { Assert-GameClosed; Restore-LanShim $GameRoot } finally { $guard.ReleaseMutex() }
    }
    $guard.Dispose()
}
