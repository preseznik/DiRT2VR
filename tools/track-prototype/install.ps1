param([Parameter(Mandatory)][string]$Candidate)
$ErrorActionPreference = 'Stop'
$lab = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts/track-prototype'))
$candidatePath = [IO.Path]::GetFullPath($Candidate)
$trackRoot = Join-Path $lab 'game/tracks/london/d2vr_test'
$route = Join-Path $trackRoot 'route_0'
foreach($path in @($candidatePath,$trackRoot,$route)) {
    if (!$path.StartsWith($lab+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Prototype paths must stay inside the isolated lab' }
    $ancestor=$path
    while($ancestor -and $ancestor.Length -ge $lab.Length) {
        if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Linked path: $ancestor" }
        $ancestor=[IO.Path]::GetDirectoryName($ancestor)
    }
}
if(Get-Process dirt2,dirt2_game -ErrorAction SilentlyContinue) { throw 'Close DiRT 2 before installing the prototype' }
if(Test-Path (Join-Path $route 'prototype.json')) { throw 'A candidate is already installed; run restore.ps1 before installing another candidate' }
$validation=Get-Content (Join-Path $candidatePath 'validation.json') -Raw | ConvertFrom-Json
if($validation.Passed -ne $true) { throw 'Candidate static validation did not pass' }
$staged = Join-Path $trackRoot ('route-staging-'+[Guid]::NewGuid().ToString('N'))
$backup = Join-Path $trackRoot ('donor-route-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
$required=@('routesplit.pssg','track.jpk','grids.pssg','ai_track.xml','dev_ai_track.xml','ai_vehicle_track.xml','progress_track.xml','boundarylines.cqtc','resetlines.cqtc','cameralines.cqtc','route_overrides.xml','track.vis','objects.ens','ornaments.xml','ornaments.bin')
foreach($name in $required) {
    $asset=Join-Path $candidatePath $name
    if ((Get-Item -LiteralPath $asset).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked candidate asset: $name" }
    if (!$validation.Files -or $validation.Files.$name -ne (Get-FileHash -LiteralPath $asset).Hash) { throw "Candidate changed after validation: $name" }
}
Copy-Item -LiteralPath $route -Destination $staged -Recurse
foreach($name in $required) { Copy-Item -LiteralPath (Join-Path $candidatePath $name) -Destination (Join-Path $staged $name) }
$hashes=[ordered]@{}
foreach($name in $required) { $hashes[$name]=(Get-FileHash -LiteralPath (Join-Path $staged $name)).Hash }
[ordered]@{Schema=1;TrackId='d2vr_test';Files=$hashes} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $staged 'prototype.json') -Encoding utf8
# All three absolute paths are under the checked lab; moves retain the donor for rollback.
foreach($path in @($staged,$backup,$route)) {
    if(![IO.Path]::GetFullPath($path).StartsWith($lab+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Move escaped lab' }
}
Move-Item -LiteralPath $route -Destination $backup
try { Move-Item -LiteralPath $staged -Destination $route }
catch { Move-Item -LiteralPath $backup -Destination $route; throw }
[ordered]@{Candidate=$candidatePath;Route=$route;Backup=$backup;RuntimeValidated=$false;Files=$hashes} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $lab 'installed.json') -Encoding utf8
Write-Host 'Prototype staged for manual desktop solo validation. Original tracks are unchanged.'
