[CmdletBinding()]
param([string]$InnoCompiler="$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe",[switch]$SkipNativeBuild,[ValidateSet('Patch','Minor','Major')][string]$VersionBump='Patch',[ValidateSet('Stable','Experimental')][string]$Channel='Stable',
    [string]$FinalizeReservedVersion,[string]$ReservedPackageDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
$projectPath=Join-Path $root 'launcher\DiRT2VR.vbproj'
$projectText=Get-Content -LiteralPath $projectPath -Raw
[xml]$project=$projectText
$current=[string]$project.Project.PropertyGroup.Version
# Both channels share one numeric sequence. Include already reserved source archives.
$known=@([version]$current)
foreach ($tag in (git tag --list 'v*')) {
    if ($tag -match '^v([0-9]+\.[0-9]+\.[0-9]+)$') { $known += [version]$Matches[1] }
}
foreach ($archive in (Get-ChildItem -LiteralPath (Join-Path $root 'source-archives/lan') -Filter '*-LAN-source.zip')) {
    if ($archive.Name -match '^DiRT2VR-([0-9]+\.[0-9]+\.[0-9]+)-LAN-source.zip$') { $known += [version]$Matches[1] }
}
# Retained local candidates also reserve numbers, even when never tagged or uploaded.
# Other worktrees may own those packages; read their manifests without modifying them.
foreach ($line in (git worktree list --porcelain)) {
    if (!$line.StartsWith('worktree ')) { continue }
    $packages=Join-Path $line.Substring(9) 'artifacts/packages'
    if (!(Test-Path -LiteralPath $packages)) { continue }
    foreach ($directory in (Get-ChildItem -LiteralPath $packages -Directory)) {
        $manifestPath=Join-Path $directory.FullName 'stage/DiRT2VR/package.json'
        if (!(Test-Path -LiteralPath $manifestPath)) { continue }
        $reservation=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ($reservation.Version -match '^[0-9]+\.[0-9]+\.[0-9]+$') { $known += [version]$reservation.Version }
    }
}
$base=($known | Sort-Object -Descending | Select-Object -First 1).ToString()
if ($FinalizeReservedVersion) {
    # Explicitly authorized finalization of an unpublished reservation; ordinary
    # packages still reserve a fresh number. Never alter an existing release/tag.
    if ($Channel -ne 'Experimental' -or $FinalizeReservedVersion -ne $current -or $base -ne $current -or !$ReservedPackageDirectory) { throw 'Finalization requires the current highest reserved experimental version and its original package directory.' }
    $reserved=Get-Content -LiteralPath (Join-Path $ReservedPackageDirectory 'stage/DiRT2VR/package.json') -Raw | ConvertFrom-Json
    if ($reserved.Version -ne $current -or $reserved.Channel -ne 'Experimental') { throw 'Original package does not match the reservation.' }
    & (Join-Path $PSScriptRoot 'verify-release.ps1') -PackageDirectory $ReservedPackageDirectory
    if ($LASTEXITCODE) { throw 'Original reserved package validation failed.' }
    $localTag=@(git tag --list "v$current")
    if ($LASTEXITCODE -or $localTag.Count) { throw 'Cannot finalize a locally tagged version.' }
    $remoteTag=@(git ls-remote --tags origin "refs/tags/v$current")
    if ($LASTEXITCODE -or $remoteTag.Count) { throw 'Cannot finalize a remotely tagged version, or remote lookup failed.' }
    $remoteText=gh api repos/preseznik/DiRT2VR/releases --paginate --slurp
    if ($LASTEXITCODE) { throw 'Cannot verify GitHub release absence; reservation was preserved.' }
    $releasePages=$remoteText | ConvertFrom-Json
    foreach ($page in $releasePages) { foreach ($release in $page) { if ($release.tag_name -eq "v$current") { throw 'This version already has a GitHub release or draft. It cannot be finalized again.' } } }
    $version=$FinalizeReservedVersion
} else {
    $version=& (Join-Path $PSScriptRoot 'next-version.ps1') -Current $base -Bump $VersionBump
}
# Reserve the version before building. Failed attempts keep their number; retries advance it.
[IO.File]::WriteAllText($projectPath,$projectText.Replace('<Version>'+$current+'</Version>','<Version>'+$version+'</Version>'))
Write-Host "Build version: $current -> $version"
if (!$SkipNativeBuild) {
    & (Join-Path $PSScriptRoot 'build-distribution.cmd')
    if ($LASTEXITCODE) { throw 'Native distribution build/tests failed' }
    & (Join-Path $PSScriptRoot 'lan/build.cmd')
    if ($LASTEXITCODE) { throw 'Native LAN build/tests failed' }
    & (Join-Path $PSScriptRoot 'driving-input/build.cmd')
    if ($LASTEXITCODE) { throw 'Driving input helper build failed' }
}
$output=Join-Path $root ('artifacts\packages\'+$version+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
$stage=Join-Path $output 'stage'
$publish=Join-Path $output 'publish'
New-Item -ItemType Directory -Path "$stage\DiRT2VR\payload","$stage\DiRT2VR\licenses" -Force | Out-Null
$buildUtc=[DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
& dotnet publish launcher/DiRT2VR.vbproj -c Release -o $publish --nologo "-p:BuildUtc=$buildUtc" "-p:ReleaseChannel=$Channel"
if ($LASTEXITCODE) { throw 'Launcher publish failed' }
Copy-Item -LiteralPath "$publish\DiRT2VR.exe" -Destination $stage
& (Join-Path $PSScriptRoot 'stage-aspen-licenses.ps1') -LicenseDirectory (Join-Path $stage 'DiRT2VR/licenses')
Copy-Item -LiteralPath 'build\distribution\bin\d3d11.dll','build\distribution\bin\xr_probe.exe' -Destination "$stage\DiRT2VR\payload"
Copy-Item -LiteralPath 'build\driving-input\driving_input.dll' -Destination "$stage\DiRT2VR\payload"
$sourceStage=Join-Path $output 'lan-source'
& (Join-Path $PSScriptRoot 'lan/Stage-LauncherPayload.ps1') -Stage $stage -SourceStage $sourceStage -Version $version
$sourceName="DiRT2VR-$version-LAN-source.zip"
$sourceArchive=Join-Path $root 'source-archives/lan'
New-Item -ItemType Directory -Path $sourceArchive -Force | Out-Null
$sourceZip=Join-Path $sourceArchive $sourceName
if (Test-Path -LiteralPath $sourceZip) {
    if (!$FinalizeReservedVersion) { throw "Refusing to replace an existing source archive: $sourceZip" }
    # Reuse the reserved LAN binary and its matching source archive. Check every
    # source file; only checkout line-ending differences are permitted. Preserve
    # the original archive, build attribution and Controls-only package.
    $existing=[IO.Compression.ZipFile]::OpenRead($sourceZip)
    try {
        $entries=@($existing.Entries | Where-Object {$_.Name -ne ''})
        $sourceFiles=@(Get-ChildItem -LiteralPath $sourceStage -Recurse -File -Force)
        if ($entries.Count -ne $sourceFiles.Count) { throw 'Reserved LAN source inventory changed; archive preserved.' }
        foreach ($file in $sourceFiles) {
            $relative=[IO.Path]::GetRelativePath($sourceStage,$file.FullName).Replace('\','/')
            $entry=@($entries | Where-Object FullName -CEQ $relative)
            if ($entry.Count -ne 1) { throw "Reserved LAN source path differs: $relative" }
            $stream=$entry[0].Open()
            $memory=[IO.MemoryStream]::new()
            try {$stream.CopyTo($memory);$bytes=$memory.ToArray()} finally {$stream.Dispose();$memory.Dispose()}
            if ($relative -eq 'source-info.json') { $reservedInfoBytes=$bytes; continue }
            $digest=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
            if ($digest -ne (Get-FileHash -LiteralPath $file.FullName).Hash) {
                $utf8=[Text.UTF8Encoding]::new($false,$true)
                if ([IO.Path]::GetExtension($relative) -notin @('.c','.cpp','.h','.patch') -or
                    $utf8.GetString($bytes).Replace("`r`n","`n") -cne [IO.File]::ReadAllText($file.FullName,$utf8).Replace("`r`n","`n")) { throw "Reserved LAN source differs: $relative" }
                [IO.File]::WriteAllBytes($file.FullName,$bytes)
            }
        }
        $oldInfo=[Text.Encoding]::UTF8.GetString($reservedInfoBytes).TrimStart([char]0xFEFF) | ConvertFrom-Json
        $newInfo=Get-Content -LiteralPath (Join-Path $sourceStage 'source-info.json') -Raw | ConvertFrom-Json
        $reservedLan=Join-Path $ReservedPackageDirectory 'stage/DiRT2VR/payload/xlive-lan.dll'
        $reservedLanHash=(Get-FileHash -LiteralPath $reservedLan).Hash
        if ($oldInfo.Version -ne $version -or $oldInfo.UpstreamCommit -ne $newInfo.UpstreamCommit -or
            $oldInfo.BinarySha256 -ne $reservedLanHash -or $reserved.Files.'DiRT2VR/payload/xlive-lan.dll' -ne $reservedLanHash -or
            $oldInfo.IntegrationPatchSha256 -ne (Get-FileHash -LiteralPath (Join-Path $sourceStage 'tools/lan/xlln-integration.patch')).Hash) { throw 'Reserved LAN binary/source attribution does not match; original package preserved.' }
        Copy-Item -LiteralPath $reservedLan -Destination "$stage/DiRT2VR/payload/xlive-lan.dll" -Force
        [IO.File]::WriteAllBytes((Join-Path $sourceStage 'source-info.json'),$reservedInfoBytes)
    } finally {$existing.Dispose()}
} else {
    # ZipFile includes dot-directories such as .deps; do not use a wildcard archive input.
    [IO.Compression.ZipFile]::CreateFromDirectory($sourceStage,$sourceZip)
}
$sourceHash=(Get-FileHash -LiteralPath $sourceZip).Hash.ToLowerInvariant()
@"
DiRT2VR $version LAN library source (XLiveLessNess, LGPL 2.1)

Matching source and build instructions:
https://github.com/preseznik/DiRT2VR/raw/refs/tags/v$version/source-archives/lan/$sourceName
SHA-256: $sourceHash

Release page: https://github.com/preseznik/DiRT2VR/releases/tag/v$version
Source is preserved in the repository; it is not a release asset or needed to play.
License: XLLN-LGPL-2.1.txt in this directory.
"@ | Set-Content -LiteralPath "$stage/DiRT2VR/licenses/LAN-source.txt" -Encoding utf8
# Ship user-facing guidance only; development notes remain local.
foreach ($name in @('README.md','CHANGELOG.md')) {
    $text=Get-Content -LiteralPath $name -Raw
    [IO.File]::WriteAllText((Join-Path "$stage\DiRT2VR" $name),$text)
}
@'
@echo off
start "" /wait "%~dp0DiRT2VR.exe" --launch --no-ui
exit /b %errorlevel%
'@ | Set-Content -LiteralPath "$stage\Start-DiRT2VR.cmd" -Encoding ascii
Copy-Item -LiteralPath '.deps\Ego-Engine-Modding\LICENSE' -Destination "$stage\DiRT2VR\licenses\EGO-MIT.txt"
Copy-Item -LiteralPath '.deps\minhook\LICENSE.txt' -Destination "$stage\DiRT2VR\licenses\MinHook.txt"
Copy-Item -LiteralPath '.deps\OpenXR-SDK\LICENSE' -Destination "$stage\DiRT2VR\licenses\OpenXR-Apache-2.0.txt"
Copy-Item -LiteralPath '.deps\OpenXR-SDK\src\external\jsoncpp\LICENSE' -Destination "$stage\DiRT2VR\licenses\JsonCpp.txt"
Copy-Item -LiteralPath 'licenses\MiscUtil.txt' -Destination "$stage\DiRT2VR\licenses\MiscUtil.txt"
# Single-file publishing does not copy runtime license files into publish/.
# Use the exact runtime packs selected by restore, not the newest installed SDK.
$assets=Get-Content -LiteralPath 'launcher\obj\project.assets.json' -Raw | ConvertFrom-Json
$packs=$assets.project.frameworks.'net10.0-windows'.downloadDependencies
foreach ($name in @('Microsoft.NETCore.App.Runtime.win-x64','Microsoft.WindowsDesktop.App.Runtime.win-x64')) {
    $pack=$packs | Where-Object name -EQ $name
    $packVersion=($pack.version.Trim('[',']').Split(',')[0]).Trim()
    $packPath=$assets.packageFolders.PSObject.Properties.Name | ForEach-Object {
        Join-Path $_ ($name.ToLowerInvariant()+'\'+$packVersion)
    } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (!$packPath) { throw "Runtime license pack not found: $name $packVersion" }
    $license=Get-ChildItem -LiteralPath $packPath -File | Where-Object Name -Match '^LICENSE(\.TXT)?$' | Select-Object -First 1
    if (!$license) { throw "Runtime license missing in $packPath" }
    Copy-Item -LiteralPath $license.FullName -Destination "$stage\DiRT2VR\licenses\$name-LICENSE.txt"
    $notices=Join-Path $packPath 'THIRD-PARTY-NOTICES.TXT'
    if ($name -eq 'Microsoft.NETCore.App.Runtime.win-x64' -and !(Test-Path -LiteralPath $notices)) { throw 'Missing .NET third-party notices' }
    if (Test-Path -LiteralPath $notices) { Copy-Item -LiteralPath $notices -Destination "$stage\DiRT2VR\licenses\$name-NOTICES.txt" }
}
$hashes=[ordered]@{}
if (Test-Path -LiteralPath "$stage\DiRT2VR\docs") { throw 'Developer docs must not be included in end-user packages.' }
if (Test-Path -LiteralPath "$stage\DiRT2VR\lan-source") { throw 'LAN source must be distributed as a separate release asset.' }
Get-ChildItem -LiteralPath $stage -File -Recurse | Sort-Object FullName | ForEach-Object {
    $relative=[IO.Path]::GetRelativePath($stage,$_.FullName).Replace('\','/')
    $hashes[$relative]=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
}
[ordered]@{Version=$version;Channel=$Channel;Files=$hashes} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$stage\DiRT2VR\package.json" -Encoding utf8
$zip=Join-Path $output "DiRT2VR-$version.zip"
Compress-Archive -LiteralPath "$stage\DiRT2VR.exe","$stage\Start-DiRT2VR.cmd","$stage\DiRT2VR" -DestinationPath $zip
if (!(Test-Path -LiteralPath $InnoCompiler)) { throw "Inno compiler not found: $InnoCompiler. ZIP is at $zip" }
& $InnoCompiler "/DStage=$stage" "/DPackageVersion=$version" (Join-Path $root 'installer\DiRT2VR.iss')
if ($LASTEXITCODE) { throw 'Installer compile failed' }
Get-ChildItem -LiteralPath $output -File | Get-FileHash | Format-Table -AutoSize
# Upload these exact versioned assets to a GitHub Release tagged v<PackageVersion>.
Get-Item -LiteralPath (Join-Path $output "DiRT2VR-$version-Setup.exe"),$zip | ForEach-Object {
    (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()+'  '+$_.Name
} | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
Write-Host "Package output: $output"
Write-Host "Commit matching LAN source before tagging the release: $sourceZip"

# Publication instructions travel with the package, not the installed game.
[ordered]@{tag_name="v$version";name=("DiRT2VR $version"+$(if($Channel -eq 'Experimental'){' — Experimental'}else{''}));prerelease=($Channel -eq 'Experimental');make_latest=$(if($Channel -eq 'Experimental'){'false'}else{'true'})} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'release.json') -Encoding utf8
& (Join-Path $PSScriptRoot 'verify-release.ps1') -PackageDirectory $output
