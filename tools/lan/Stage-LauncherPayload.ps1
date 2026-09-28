param([Parameter(Mandatory)][string]$Stage,[Parameter(Mandatory)][string]$SourceStage,[Parameter(Mandatory)][string]$Version)
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$upstream=Join-Path $repo '.deps/xlivelessness'
if ((git -C $upstream rev-parse HEAD) -ne '0b4ca99727566f835cc5aaca6b0a9dc4aced28b9') { throw 'Wrong LAN source revision' }
git -C $upstream apply --reverse --check (Join-Path $PSScriptRoot 'xlln-integration.patch')
if ($LASTEXITCODE) { throw 'LAN source integration patch is missing or changed' }
Copy-Item -LiteralPath "$upstream/bin/xlive.dll" -Destination "$Stage/DiRT2VR/payload/xlive-lan.dll"
Copy-Item -LiteralPath "$upstream/LICENSE.md" -Destination "$Stage/DiRT2VR/licenses/XLLN-LGPL-2.1.txt"
Copy-Item -LiteralPath "$repo/build/lan/_deps/opus-src/COPYING" -Destination "$Stage/DiRT2VR/licenses/Opus.txt"
Copy-Item -LiteralPath "$repo/build/lan/_deps/rapidxml-src/license.txt" -Destination "$Stage/DiRT2VR/licenses/RapidXML.txt"
Copy-Item -LiteralPath "$repo/build/lan/_deps/rapidjson-src/license.txt" -Destination "$Stage/DiRT2VR/licenses/RapidJSON.txt"
# Corresponding source is archived in the repository, never in the player package or release assets.
$source=[IO.Path]::GetFullPath($SourceStage)
$payloadRoot=[IO.Path]::GetFullPath($Stage).TrimEnd('\','/')
if ($source -eq $payloadRoot -or $source.StartsWith($payloadRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'LAN source staging must be outside the installed payload.' }
New-Item -ItemType Directory -Path "$source/.deps/xlivelessness","$source/tools","$source/src" -Force | Out-Null
foreach ($name in @('xlivelessness','cmake','CMakeLists.txt','README.md','LICENSE.md')) {
    Copy-Item -LiteralPath (Join-Path $upstream $name) -Destination "$source/.deps/xlivelessness" -Recurse
}
New-Item -ItemType Directory -Path "$source/tools/lan" -Force | Out-Null
# Only files required to rebuild the library, independent of local test kits.
foreach ($name in @('CMakeLists.txt','build.cmd','profile.cpp','profile.h','integration.cpp','intro.cpp','intro.h','browser.cpp','diagnostics.cpp','diagnostics.h','xlln-integration.patch')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination "$source/tools/lan"
}
Copy-Item -LiteralPath "$repo/src/common.cpp","$repo/src/common.h" -Destination "$source/src"
Copy-Item -LiteralPath "$PSScriptRoot/SOURCE-README.md" -Destination "$source/README.md"
[ordered]@{
    Version=$Version
    ProjectCommit=(git -C $repo rev-parse HEAD)
    UpstreamCommit=(git -C $upstream rev-parse HEAD)
    BinarySha256=(Get-FileHash -LiteralPath "$Stage/DiRT2VR/payload/xlive-lan.dll").Hash
    IntegrationPatchSha256=(Get-FileHash -LiteralPath "$PSScriptRoot/xlln-integration.patch").Hash
} | ConvertTo-Json | Set-Content -LiteralPath "$source/source-info.json" -Encoding utf8
