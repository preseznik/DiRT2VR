[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageDirectory,[string]$GitHubReleaseJson)
$ErrorActionPreference='Stop'
$manifest=Get-Content -LiteralPath (Join-Path $PackageDirectory 'stage/DiRT2VR/package.json') -Raw | ConvertFrom-Json
$release=Get-Content -LiteralPath (Join-Path $PackageDirectory 'release.json') -Raw | ConvertFrom-Json
if ($manifest.Channel -notin @('Stable','Experimental') -or $manifest.Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'Invalid package channel/version' }
$experimental=$manifest.Channel -eq 'Experimental'
if ($release.tag_name -cne "v$($manifest.Version)" -or $release.prerelease -isnot [bool] -or $release.prerelease -ne $experimental -or $release.make_latest -cne $(if($experimental){'false'}else{'true'})) { throw 'Release designation does not match the packaged channel' }
foreach ($name in @("DiRT2VR-$($manifest.Version)-Setup.exe","DiRT2VR-$($manifest.Version).zip",'SHA256SUMS.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $PackageDirectory $name))) { throw "Release asset missing: $name" }
}
if ($GitHubReleaseJson) {
    # Supply the draft release JSON from: gh api repos/preseznik/DiRT2VR/releases/<id>
    $remote=Get-Content -LiteralPath $GitHubReleaseJson -Raw | ConvertFrom-Json
    if ($remote.tag_name -cne $release.tag_name -or $remote.prerelease -isnot [bool] -or $remote.prerelease -ne $experimental) { throw 'GitHub release channel/tag differs from the package; do not publish' }
    if (@($remote.assets).Count -ne 3) { throw 'Only Setup, ZIP and SHA256SUMS.txt belong in the release.' }
    foreach ($name in @("DiRT2VR-$($manifest.Version)-Setup.exe","DiRT2VR-$($manifest.Version).zip",'SHA256SUMS.txt')) {
        $asset=@($remote.assets | Where-Object name -CEQ $name)
        if ($asset.Count -ne 1 -or $asset[0].state -ne 'uploaded' -or $asset[0].digest -cne ('sha256:'+(Get-FileHash -LiteralPath (Join-Path $PackageDirectory $name)).Hash.ToLowerInvariant())) { throw "Uploaded release asset differs: $name" }
    }
}
Write-Output "PASS release $($release.tag_name): $($manifest.Channel), prerelease=$($release.prerelease), make_latest=$($release.make_latest)"
