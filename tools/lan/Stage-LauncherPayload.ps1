param([Parameter(Mandatory)][string]$Stage)
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
# Corresponding source in its buildable repository layout. No game/profile/SDK files.
$source=Join-Path $Stage 'DiRT2VR/lan-source'
New-Item -ItemType Directory -Path "$source/.deps/xlivelessness","$source/tools","$source/src","$source/tests" -Force | Out-Null
foreach ($name in @('xlivelessness','cmake','CMakeLists.txt','README.md','LICENSE.md')) {
    Copy-Item -LiteralPath (Join-Path $upstream $name) -Destination "$source/.deps/xlivelessness" -Recurse
}
Copy-Item -LiteralPath $PSScriptRoot -Destination "$source/tools/lan" -Recurse
Copy-Item -LiteralPath "$repo/src/common.cpp","$repo/src/common.h" -Destination "$source/src"
Copy-Item -LiteralPath "$repo/tests/lan_profile_test.cpp","$repo/tests/lan_intro_test.cpp","$repo/tests/lan_files.ps1","$repo/tests/lan_settings.ps1" -Destination "$source/tests"
