[CmdletBinding()]
param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
[xml]$project=Get-Content tools/aspen-converter/AspenConverter.csproj
$version=[string]$project.Project.PropertyGroup.Version
if($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid converter version' }
if(!$OutputDirectory) { $OutputDirectory=Join-Path $root ('artifacts/aspen-packages/'+$version+'-'+(Get-Date -Format yyyyMMdd-HHmmss)) }
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output) { throw 'Package output must be new; existing artifacts are preserved' }
$stage=Join-Path $output 'stage'
New-Item -ItemType Directory -Path (Join-Path $stage 'licenses') -Force | Out-Null
& dotnet publish tools/aspen-converter/AspenConverter.csproj -c Release -o (Join-Path $output 'publish')
if($LASTEXITCODE) { throw 'Standalone Aspen converter publish failed' }
Copy-Item -LiteralPath (Join-Path $output 'publish/AspenConverter.exe') -Destination $stage
Get-ChildItem -LiteralPath tools/aspen-converter/licenses -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $stage 'licenses') }
Copy-Item -LiteralPath .deps/Ego-Engine-Modding/LICENSE -Destination (Join-Path $stage 'licenses/EGO-MIT.txt')
Copy-Item -LiteralPath licenses/MiscUtil.txt -Destination (Join-Path $stage 'licenses/MiscUtil.txt')
$assets=Get-Content tools/aspen-converter/obj/project.assets.json -Raw | ConvertFrom-Json
$attributions=@()
foreach($entry in $assets.libraries.PSObject.Properties | Where-Object {$_.Value.type -eq 'package' -and $_.Name -notlike 'Microsoft.NET.ILLink.Tasks/*'}) {
    $folder=$assets.packageFolders.PSObject.Properties.Name | ForEach-Object {Join-Path $_ $entry.Name.ToLowerInvariant()} | Where-Object {Test-Path -LiteralPath $_} | Select-Object -First 1
    if(!$folder) {throw "Dependency package missing: $($entry.Name)"}
    $notices=@(Get-ChildItem -LiteralPath $folder -Recurse -File | Where-Object {$_.Name -match '^(LICENSE|ThirdPartyNotices|THIRD-PARTY-NOTICES)(\.(txt|md))?$'})
    foreach($notice in $notices) {
        Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $stage ('licenses/'+$entry.Name.Replace('/','-')+'-'+$notice.Name))
    }
    [xml]$spec=Get-Content (Get-ChildItem -LiteralPath $folder -Filter '*.nuspec' | Select-Object -First 1).FullName
    $attributions+="$($entry.Name) — $($spec.package.metadata.authors); $($spec.package.metadata.projectUrl)"
}
$runtime=@($assets.project.frameworks.'net10.0'.downloadDependencies | Where-Object name -eq 'Microsoft.NETCore.App.Runtime.win-x64')
if($runtime.Count -ne 1) {throw 'Exact runtime dependency not found'}
$runtimeVersion=($runtime[0].version.Trim('[',']').Split(',')[0]).Trim()
$runtimeFolder=$assets.packageFolders.PSObject.Properties.Name | ForEach-Object {Join-Path $_ ('microsoft.netcore.app.runtime.win-x64/'+$runtimeVersion)} | Where-Object {Test-Path -LiteralPath $_} | Select-Object -First 1
foreach($name in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) {
    $path=Join-Path $runtimeFolder $name
    if(!(Test-Path -LiteralPath $path)) {throw "Runtime notice missing: $name"}
    Copy-Item -LiteralPath $path -Destination (Join-Path $stage ('licenses/DotNet-'+$name))
}
$attributions | Set-Content -LiteralPath (Join-Path $stage 'licenses/DEPENDENCIES.txt') -Encoding utf8
$sources=@(Get-Content tools/aspen-converter/sources.json -Raw | ConvertFrom-Json)
$layouts=@(
    [ordered]@{Id='aspen-lakeside';Name='Lakeside';Folder='d2vr_aspen';Condition='Night'},
    [ordered]@{Id='aspen-lake-view';Name='Lake View';Folder='d2vr_aspen_lv';Condition='Morning sun'},
    [ordered]@{Id='aspen-snowmass-sprint';Name='Snowmass Sprint';Folder='d2vr_aspen_ss';Condition='Evening sun'},
    [ordered]@{Id='aspen-snowmass-loop';Name='Snowmass Loop';Folder='d2vr_aspen_sl';Condition='Overcast'}
)
[ordered]@{Schema=1;Id='aspen-rallycross';Version=$version;MinimumLauncher='0.17.4';Layouts=$layouts;Sources=$sources} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $stage 'package.json') -Encoding utf8
@'
Aspen Rallycross conversion tools — Experimental

Use DiRT2VR 0.17.4 or later. In Launcher, enable CUSTOM tracks, choose
Install Aspen, and select your own DiRT 3 Complete Edition installation.
The launcher checks your original DiRT 2 and DiRT 3 files, builds all four
layouts, verifies them and installs them. No separate .NET or SDK is needed.

Installed tracks work offline. DiRT 3 is needed again only to rebuild/update.
Supported play: desktop Direct practice, solo Subaru STI, one lap.
Lakeside: night; Lake View: morning; Snowmass Sprint: evening;
Snowmass Loop: overcast. VR, AI racing, LAN, ski-lift animation, full snowfall
and deformable snow are not supported. Handling is adapted for DiRT 2.
Snowmass Sprint omits 12 decorative beams on its ski hillside to avoid
flickering. Towers, lamp faces, ground lighting and other beams remain.

This package contains conversion code and metadata, not game assets.
The source installations are never modified. Generated assets stay local.
Do not run the converter as administrator. Use the launcher to install tracks.
Third-party license texts and notices are included in licenses/.
'@ | Set-Content -LiteralPath (Join-Path $stage 'README.txt') -Encoding utf8
$zip=Join-Path $output "AspenConverter-$version.zip"
# Explicit files: no directory/link entries, development tools or source assets.
$archive=[IO.Compression.ZipFile]::Open($zip,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach($file in Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName) {
        if($file.Attributes -band [IO.FileAttributes]::ReparsePoint) {throw 'Package links are forbidden'}
        $relative=[IO.Path]::GetRelativePath($stage,$file.FullName).Replace('\','/')
        if($relative -notin @('AspenConverter.exe','README.txt','package.json') -and $relative -notmatch '^licenses/[^/]+\.(txt|md)$') {throw "Unexpected package content: $relative"}
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,$relative,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally {$archive.Dispose()}
$hash=(Get-FileHash -LiteralPath $zip).Hash
$offer=[ordered]@{Id='aspen-rallycross';Name='Aspen Rallycross';Version=$version;MinimumLauncher='0.17.4';
    ArchiveName="AspenConverter-$version.zip";
    ArchiveBytes=(Get-Item -LiteralPath $zip).Length;Sha256=$hash;StagingBytes=4L*1024*1024*1024;InstalledBytes=800L*1024*1024;
    Modes=@('desktop-solo');Layouts=$layouts;Sources=$sources}
[ordered]@{Schema=1;Tracks=@($offer)} | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath (Join-Path $output 'aspen-package.json') -Encoding utf8
Write-Output "Bundled Aspen tools: $output. Include the ZIP and aspen-package.json inside the app payload; do not upload them as release assets."
