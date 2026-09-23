$ErrorActionPreference='Stop'
$lab=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts/track-prototype'))
$receipt=Get-Content (Join-Path $lab 'installed.json') -Raw | ConvertFrom-Json
$route=[IO.Path]::GetFullPath([string]$receipt.Route)
$backup=[IO.Path]::GetFullPath([string]$receipt.Backup)
$archive=Join-Path $lab ('retired-candidate-'+[Guid]::NewGuid().ToString('N'))
if($route -ne (Join-Path $lab 'game/tracks/london/d2vr_test/route_0')) { throw 'Unexpected installed route' }
foreach($path in @($route,$backup,$archive)) {
    if(!$path.StartsWith($lab+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Restore path escaped lab' }
    $ancestor=$path
    while($ancestor -and $ancestor.Length -ge $lab.Length) {
        if((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked restore path' }
        $ancestor=[IO.Path]::GetDirectoryName($ancestor)
    }
}
if(Get-Process dirt2,dirt2_game -ErrorAction SilentlyContinue) { throw 'Close DiRT 2 before restoring' }
if(!(Test-Path -LiteralPath (Join-Path $route 'prototype.json')) -or !(Test-Path -LiteralPath $backup)) { throw 'Prototype or donor backup missing' }
Move-Item -LiteralPath $route -Destination $archive
try { Move-Item -LiteralPath $backup -Destination $route }
catch { Move-Item -LiteralPath $archive -Destination $route; throw }
Write-Host "Donor clone restored and hidden from the launcher. Candidate retained at $archive"
