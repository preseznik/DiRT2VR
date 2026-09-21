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
