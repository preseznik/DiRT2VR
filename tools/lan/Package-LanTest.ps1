param([Parameter(Mandatory)][string]$Version,[switch]$SkipNativeBuild)
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Set-Location -LiteralPath $repo
$upstream=Join-Path $repo '.deps/xlivelessness'
$revision='0b4ca99727566f835cc5aaca6b0a9dc4aced28b9'
if (!(Test-Path -LiteralPath $upstream)) {
    git clone --depth 1 --branch v1.6.2.1 https://gitlab.com/GlitchyScripts/xlivelessness.git $upstream
    if ($LASTEXITCODE) { throw 'XLLN source download failed' }
}
if ((git -C $upstream rev-parse HEAD) -ne $revision) { throw 'Wrong XLLN source revision' }
$patch=Join-Path $PSScriptRoot 'xlln-integration.patch'
git -C $upstream apply --reverse --check $patch 2>$null
if ($LASTEXITCODE) {
    git -C $upstream apply --check $patch
    if ($LASTEXITCODE) { throw 'XLLN source differs from expected integration patch' }
    git -C $upstream apply $patch
    if ($LASTEXITCODE) { throw 'XLLN integration patch failed' }
}
if (!$SkipNativeBuild) {
    & (Join-Path $PSScriptRoot 'build.cmd')
    if ($LASTEXITCODE) { throw 'LAN native build/tests failed' }
}
& (Join-Path $repo 'tests/lan_files.ps1')
$output=Join-Path $repo ('artifacts/lan-packages/'+$Version+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
$kit=Join-Path $output 'DiRT2VR-LAN-Test'
New-Item -ItemType Directory -Path $kit,"$kit/licenses","$kit/source" -Force | Out-Null
Copy-Item -LiteralPath "$upstream/bin/xlive.dll" -Destination "$kit/xlive-lan.dll"
Copy-Item -LiteralPath "$PSScriptRoot/LanFiles.ps1","$PSScriptRoot/Start-LanTest.ps1","$PSScriptRoot/Start-LanTest.cmd","$PSScriptRoot/README.txt" -Destination $kit
Copy-Item -LiteralPath "$upstream/LICENSE.md" -Destination "$kit/licenses/XLLN-LGPL-2.1.txt"
Copy-Item -LiteralPath 'build/lan/_deps/opus-src/COPYING' -Destination "$kit/licenses/Opus.txt"
Copy-Item -LiteralPath 'build/lan/_deps/rapidxml-src/license.txt' -Destination "$kit/licenses/RapidXML.txt"
Copy-Item -LiteralPath 'build/lan/_deps/rapidjson-src/license.txt' -Destination "$kit/licenses/RapidJSON.txt"
# Include modified upstream source and our additions; no game files or SDK binaries.
foreach ($name in @('xlivelessness','cmake','CMakeLists.txt','README.md','LICENSE.md')) {
    Copy-Item -LiteralPath (Join-Path $upstream $name) -Destination "$kit/source" -Recurse
}
Copy-Item -LiteralPath $PSScriptRoot -Destination "$kit/source/DiRT2VR-lan" -Recurse
Copy-Item -LiteralPath 'src/common.cpp','src/common.h','tests/lan_profile_test.cpp' -Destination "$kit/source/DiRT2VR-lan"
$files=[ordered]@{}
Get-ChildItem -LiteralPath $kit -File | ForEach-Object { $files[$_.Name]=(Get-FileHash -LiteralPath $_.FullName).Hash }
@{Version=$Version;Upstream=$revision;Files=$files} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$kit/lab-package.json" -Encoding utf8
Compress-Archive -LiteralPath $kit -Destination (Join-Path $output "DiRT2VR-$Version-LAN-Test.zip")
Write-Host "LAN test package: $output"
