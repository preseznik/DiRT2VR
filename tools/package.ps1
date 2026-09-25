param([string]$InnoCompiler="$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe",[switch]$SkipNativeBuild,[ValidateSet('Patch','Minor','Major')][string]$VersionBump='Patch',[switch]$LanLab)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
$projectPath=Join-Path $root 'launcher\DiRT2VR.vbproj'
$projectText=Get-Content -LiteralPath $projectPath -Raw
[xml]$project=$projectText
$current=[string]$project.Project.PropertyGroup.Version
$version=& (Join-Path $PSScriptRoot 'next-version.ps1') -Current $current -Bump $VersionBump
# Reserve the version before building. Failed attempts keep their number; retries advance it.
[IO.File]::WriteAllText($projectPath,$projectText.Replace('<Version>'+$current+'</Version>','<Version>'+$version+'</Version>'))
Write-Host "Build version: $current -> $version"
if ($LanLab) {
    & (Join-Path $PSScriptRoot 'lan/Package-LanTest.ps1') -Version $version -SkipNativeBuild:$SkipNativeBuild
    return
}
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
& dotnet publish launcher/DiRT2VR.vbproj -c Release -o $publish --nologo "-p:BuildUtc=$buildUtc"
if ($LASTEXITCODE) { throw 'Launcher publish failed' }
Copy-Item -LiteralPath "$publish\DiRT2VR.exe" -Destination $stage
Copy-Item -LiteralPath 'build\distribution\bin\d3d11.dll','build\distribution\bin\xr_probe.exe' -Destination "$stage\DiRT2VR\payload"
Copy-Item -LiteralPath 'build\driving-input\driving_input.dll' -Destination "$stage\DiRT2VR\payload"
$sourceStage=Join-Path $output 'lan-source'
& (Join-Path $PSScriptRoot 'lan/Stage-LauncherPayload.ps1') -Stage $stage -SourceStage $sourceStage -Version $version
$sourceName="DiRT2VR-$version-LAN-source.zip"
$sourceZip=Join-Path $output $sourceName
# ZipFile includes dot-directories such as .deps; do not use a wildcard archive input.
[IO.Compression.ZipFile]::CreateFromDirectory($sourceStage,$sourceZip)
$sourceHash=(Get-FileHash -LiteralPath $sourceZip).Hash.ToLowerInvariant()
@"
DiRT2VR $version LAN library source (XLiveLessNess, LGPL 2.1)

Matching source and build instructions:
https://github.com/preseznik/DiRT2VR/releases/download/v$version/$sourceName
SHA-256: $sourceHash

Release page: https://github.com/preseznik/DiRT2VR/releases/tag/v$version
Source is an optional download; it is not needed to play or installed by setup.
License: XLLN-LGPL-2.1.txt in this directory.
"@ | Set-Content -LiteralPath "$stage/DiRT2VR/licenses/LAN-source.txt" -Encoding utf8
# Ship user-facing guidance only. Technical documentation stays in the repository;
# versioned web links keep the packaged Markdown useful without a local docs folder.
foreach ($name in @('README.md','CHANGELOG.md')) {
    $text=Get-Content -LiteralPath $name -Raw
    $text=$text.Replace('](docs/', "](https://github.com/preseznik/DiRT2VR/blob/v$version/docs/")
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
[ordered]@{Version=$version;Files=$hashes} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$stage\DiRT2VR\package.json" -Encoding utf8
$zip=Join-Path $output "DiRT2VR-$version.zip"
Compress-Archive -LiteralPath "$stage\DiRT2VR.exe","$stage\Start-DiRT2VR.cmd","$stage\DiRT2VR" -DestinationPath $zip
if (!(Test-Path -LiteralPath $InnoCompiler)) { throw "Inno compiler not found: $InnoCompiler. ZIP is at $zip" }
& $InnoCompiler "/DStage=$stage" "/DPackageVersion=$version" (Join-Path $root 'installer\DiRT2VR.iss')
if ($LASTEXITCODE) { throw 'Installer compile failed' }
Get-ChildItem -LiteralPath $output -File | Get-FileHash | Format-Table -AutoSize
# Upload these exact versioned assets to a GitHub Release tagged v<PackageVersion>.
Get-ChildItem -LiteralPath $output -File | Where-Object Extension -In '.exe','.zip' | ForEach-Object {
    (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()+'  '+$_.Name
} | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
Write-Host "Package output: $output"
