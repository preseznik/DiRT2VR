$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dependencies = @(
    @{ Name='OpenXR-SDK'; Url='https://github.com/KhronosGroup/OpenXR-SDK.git'; Tag='release-1.1.53'; Commit='75c53b6e853dc12c7b3c771edc9c9c841b15faaa' },
    @{ Name='minhook'; Url='https://github.com/TsudaKageyu/minhook.git'; Tag='v1.3.4'; Commit='c3fcafdc10146beb5919319d0683e44e3c30d537' }
)
New-Item -ItemType Directory -Force -Path (Join-Path $root '.deps') | Out-Null
foreach ($dependency in $dependencies) {
    $path = Join-Path $root ('.deps\' + $dependency.Name)
    if (!(Test-Path -LiteralPath $path)) {
        & git -c http.sslBackend=openssl clone --depth 1 --branch $dependency.Tag $dependency.Url $path
        if ($LASTEXITCODE -ne 0) { throw "Clone failed: $($dependency.Name)" }
    }
    $actual = & git -C $path rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $actual -ne $dependency.Commit) {
        throw "Unexpected dependency revision: $path. Existing files were preserved."
    }
    $dirty = & git -C $path status --porcelain
    if ($LASTEXITCODE -ne 0 -or $dirty) { throw "Dependency has local changes: $path" }
    Write-Host "$($dependency.Name): verified $actual"
}

# XML support library used by the launcher.
$source = Join-Path $root '.deps\Ego-Engine-Modding'
$safeSource = $source.Replace('\','/')
$revision = 'f3fe9ee0f6e8379c64bc4a29e1d1b586bba5d1c1'
if (!(Test-Path -LiteralPath $source)) {
    & git -c http.sslBackend=openssl clone --no-checkout --filter=blob:none https://github.com/EgoEngineModding/Ego-Engine-Modding.git $source
    if ($LASTEXITCODE -ne 0) { throw 'EGO source clone failed' }
    & git -c "safe.directory=$safeSource" -C $source -c http.sslBackend=openssl checkout --detach $revision
    if ($LASTEXITCODE -ne 0) { throw 'Pinned EGO checkout failed' }
}
if ((& git -c "safe.directory=$safeSource" -C $source rev-parse HEAD) -ne $revision) { throw 'Unexpected EGO revision; existing source preserved' }
Write-Host "Ego-Engine-Modding: verified $revision"

# LAN runtime, including the pinned integration patch.
$upstream=Join-Path $root '.deps/xlivelessness'
$revision='0b4ca99727566f835cc5aaca6b0a9dc4aced28b9'
if (!(Test-Path -LiteralPath $upstream)) {
    git clone --depth 1 --branch v1.6.2.1 https://gitlab.com/GlitchyScripts/xlivelessness.git $upstream
    if ($LASTEXITCODE) { throw 'XLLN source download failed' }
}
if ((git -C $upstream rev-parse HEAD) -ne $revision) { throw 'Wrong XLLN source revision' }
$patch=Join-Path $PSScriptRoot 'lan/xlln-integration.patch'
git -C $upstream apply --reverse --check $patch 2>$null
if ($LASTEXITCODE) {
    git -C $upstream apply --check $patch
    if ($LASTEXITCODE) { throw 'XLLN source differs from expected integration patch' }
    git -C $upstream apply $patch
    if ($LASTEXITCODE) { throw 'XLLN integration patch failed' }
}
Write-Host "XLiveLessNess: verified $revision with integration patch"
