$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
. "$repo/tools/lan/LanFiles.ps1"
. "$repo/tools/lan/LanSettings.ps1"
$root=Join-Path $repo ('artifacts/lan-settings-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$path=Join-Path $root 'lan-settings.json'
if ((Read-LanSettings $path).SkipIntroduction) { throw 'Intro bypass must default off' }
foreach ($enabled in @($true,$false)) {
    Write-LanAtomic $path ([Text.Encoding]::UTF8.GetBytes((@{Version=1;SkipIntroduction=$enabled} | ConvertTo-Json)))
    if ((Read-LanSettings $path).SkipIntroduction -ne $enabled) { throw 'Intro toggle did not round-trip' }
}
foreach ($bad in @('{"Version":1,"SkipIntroduction":"false"}','{"Version":2,"SkipIntroduction":true}','{}')) {
    Write-LanAtomic $path ([Text.Encoding]::UTF8.GetBytes($bad))
    $rejected=$false
    try { Read-LanSettings $path | Out-Null } catch { $rejected=$true }
    if (!$rejected) { throw 'Invalid intro setting was accepted' }
}
Write-Host 'LAN settings: default off, on/off persistence and invalid settings rejection passed'
