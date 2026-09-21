$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
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
$output = Join-Path $root 'artifacts\ego-converter'
& dotnet build (Join-Path $source 'src\EgoEngineLibrary\EgoEngineLibrary.csproj') -c Release -o $output --nologo "-p:RestorePackagesPath=$root\.deps\nuget"
if ($LASTEXITCODE -ne 0) { throw 'EGO library build failed' }
& dotnet build (Join-Path $PSScriptRoot 'xml-convert\xml-convert.csproj') -c Release -o (Join-Path $root 'artifacts\xml-convert') --nologo
if ($LASTEXITCODE -ne 0) { throw 'XML wrapper build failed' }
Copy-Item -LiteralPath (Join-Path $source 'LICENSE') -Destination (Join-Path $root 'artifacts\xml-convert\EGO-LICENSE.txt')
