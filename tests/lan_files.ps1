$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
. (Join-Path $repo 'tools/lan/LanFiles.ps1')
$root=Join-Path $repo ('artifacts/lan-files-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$target=Join-Path $root 'xlive.dll'; $payload=Join-Path $root 'payload.dll'
[IO.File]::WriteAllText($target,'original'); [IO.File]::WriteAllText($payload,'LAN payload')
$original=Get-LanHash $target; $hash=Get-LanHash $payload
function Check($condition,$message) { if (!$condition) { throw $message }; Write-Output "PASS $message" }
Install-LanShim $root $payload $hash
Check ((Get-LanHash $target) -eq $hash) 'payload applied'
Restore-LanShim $root
Check ((Get-LanHash $target) -eq $original) 'original restored exactly'
$rejected=$false
try { Install-LanShim $root $payload $hash { throw 'interrupted' } } catch { $rejected=$true }
Check $rejected 'preparation interruption observed'
Restore-LanShim $root
Check ((Get-LanHash $target) -eq $original) 'journal-before-write recovery'
Install-LanShim $root $payload $hash
[IO.File]::WriteAllText($target,'outside edit')
$rejected=$false
try { Restore-LanShim $root } catch { $rejected=$true }
Check ($rejected -and [IO.File]::ReadAllText($target) -eq 'outside edit') 'external edit preserved as conflict'
[IO.File]::WriteAllBytes($target,[IO.File]::ReadAllBytes($payload))
Restore-LanShim $root
Install-LanShim $root $payload $hash
Restore-LanShim $root
Check ((Get-LanHash $target) -eq $original) 'repeated preparation retains original backup'
$empty=Join-Path $root 'without-original';[IO.Directory]::CreateDirectory($empty) | Out-Null
Install-LanShim $empty $payload $hash;Restore-LanShim $empty
Check (!(Test-Path (Join-Path $empty 'xlive.dll'))) 'absent original returns to absence'
$rejected=$false
try { Install-LanShim $root $payload ('0'*64) } catch { $rejected=$true }
Check ($rejected -and (Get-LanHash $target) -eq $original) 'invalid payload rejected before modification'
