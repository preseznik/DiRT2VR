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
foreach ($name in @('CMakeLists.txt','build.cmd','profile.cpp','profile.h','profile_names.cpp','profile_names.h','SaveRoutePath.h','integration.cpp','intro.cpp','intro.h','browser.cpp','diagnostics.cpp','diagnostics.h','filecheck.cpp','filecheck.h','xlln-integration.patch')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination "$source/tools/lan"
}
Copy-Item -LiteralPath "$repo/src/common.cpp","$repo/src/common.h","$repo/src/pikes_scope.cpp","$repo/src/pikes_scope.h","$repo/src/pikes_layouts.h" -Destination "$source/src"
# Every file the LAN build compiles, and every local header those files include,
# must be in the archive: filecheck.cpp/.h were missing from 0.18.6 to 0.18.10.
$built=[regex]::Matches((Get-Content -LiteralPath "$PSScriptRoot/CMakeLists.txt" -Raw),'\$\{(CMAKE_CURRENT_LIST_DIR|REPO)\}/([^"\s)]+\.(?:cpp|c))(?=["\s)])') | ForEach-Object {
    if ($_.Groups[1].Value -eq 'REPO') { $_.Groups[2].Value } else { "tools/lan/$($_.Groups[2].Value)" }
}
foreach ($relative in $built) {
    if (!(Test-Path -LiteralPath (Join-Path $source $relative))) { throw "LAN source archive is missing $relative, which tools/lan/CMakeLists.txt builds." }
}
foreach ($file in Get-ChildItem -LiteralPath "$source/tools/lan","$source/src" -File | Where-Object Extension -in '.cpp','.c','.h') {
    foreach ($include in [regex]::Matches((Get-Content -LiteralPath $file.FullName -Raw),'#include "([^"]+)"')) {
        $name=$include.Groups[1].Value
        if (!(@($file.DirectoryName,"$source/tools/lan","$source/src","$source/.deps/xlivelessness/xlivelessness") | Where-Object { Test-Path -LiteralPath (Join-Path $_ $name) })) {
            throw "LAN source archive is missing $name, which $($file.Name) includes."
        }
    }
}
Copy-Item -LiteralPath "$PSScriptRoot/SOURCE-README.md" -Destination "$source/README.md"
[ordered]@{
    Version=$Version
    ProjectCommit=(git -C $repo rev-parse HEAD)
    UpstreamCommit=(git -C $upstream rev-parse HEAD)
    BinarySha256=(Get-FileHash -LiteralPath "$Stage/DiRT2VR/payload/xlive-lan.dll").Hash
    IntegrationPatchSha256=(Get-FileHash -LiteralPath "$PSScriptRoot/xlln-integration.patch").Hash
} | ConvertTo-Json | Set-Content -LiteralPath "$source/source-info.json" -Encoding utf8
