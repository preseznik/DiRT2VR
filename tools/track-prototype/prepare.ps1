param([string]$SourceGame = (Join-Path $PSScriptRoot '../../artifacts/game'))
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source = [IO.Path]::GetFullPath($SourceGame)
$lab = Join-Path $repo 'artifacts/track-prototype'
$game = Join-Path $lab 'game'
if (Test-Path -LiteralPath $game) { throw "Test installation already exists: $game. It will not be overwritten." }
if ((Get-FileHash -LiteralPath (Join-Path $source 'dirt2_game.exe')).Hash -ne '49B1E00EA1D4BD02E633CEED63390B5CAE07601333F673C4EFD8D2F0EB54FE48') { throw 'Unsupported source executable' }
if (Test-Path -LiteralPath (Join-Path $source 'DiRT2VR/backups/pending.json')) { throw 'Recover the source game session first' }
if (Get-Process dirt2,dirt2_game -ErrorAction SilentlyContinue) { throw 'Close DiRT 2 before preparing the test copy' }
if ((Get-Item -LiteralPath $source).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Source must be a real directory' }
if (Get-ChildItem -LiteralPath $source -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } | Select-Object -First 1) { throw 'Source contains linked assets' }
New-Item -ItemType Directory -Path $lab -Force | Out-Null
& robocopy $source $game /E /COPY:DAT /DCOPY:DAT /XJ /R:0 /W:0 /NFL /NDL /NP /NJH
if ($LASTEXITCODE -ge 8) { throw "Game copy failed: $LASTEXITCODE" }
$donor = Join-Path $game 'tracks/london/battersea'
$target = Join-Path $game 'tracks/london/d2vr_test'
New-Item -ItemType Directory -Path $target | Out-Null
Get-ChildItem -LiteralPath $donor -File | Copy-Item -Destination $target
Copy-Item -LiteralPath (Join-Path $donor 'route_1') -Destination (Join-Path $target 'route_0') -Recurse
$files = @(Get-ChildItem -LiteralPath $target -Recurse -File | Sort-Object FullName | ForEach-Object {
    [ordered]@{ Path = [IO.Path]::GetRelativePath($target, $_.FullName); Sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
})
[ordered]@{ Source = $source; Game = $game; Donor = 'london/battersea/route_1'; Target = 'london/d2vr_test/route_0'; Files = $files } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $lab 'clone.json') -Encoding utf8
Write-Host "Isolated copy ready: $game"
