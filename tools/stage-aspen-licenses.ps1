[CmdletBinding()]
param([Parameter(Mandatory)][string]$LicenseDirectory)
$ErrorActionPreference='Stop'
Get-ChildItem -LiteralPath tools/aspen-converter/licenses -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $LicenseDirectory }
Copy-Item -LiteralPath .deps/Ego-Engine-Modding/LICENSE -Destination (Join-Path $LicenseDirectory 'EGO-MIT.txt')
Copy-Item -LiteralPath licenses/MiscUtil.txt -Destination (Join-Path $LicenseDirectory 'MiscUtil.txt')
$assets=Get-Content launcher/obj/project.assets.json -Raw | ConvertFrom-Json
$attributions=@()
foreach($entry in $assets.libraries.PSObject.Properties | Where-Object {$_.Value.type -eq 'package' -and $_.Name -notlike 'Microsoft.NET.ILLink.Tasks/*'}) {
    $folder=$assets.packageFolders.PSObject.Properties.Name | ForEach-Object {Join-Path $_ $entry.Name.ToLowerInvariant()} | Where-Object {Test-Path -LiteralPath $_} | Select-Object -First 1
    if(!$folder) {throw "Dependency package missing: $($entry.Name)"}
    $notices=@(Get-ChildItem -LiteralPath $folder -Recurse -File | Where-Object {$_.Name -match '^(LICENSE|ThirdPartyNotices|THIRD-PARTY-NOTICES)(\.(txt|md))?$'})
    foreach($notice in $notices) {
        Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $LicenseDirectory ('Aspen-'+$entry.Name.Replace('/','-')+'-'+$notice.Name))
    }
    [xml]$spec=Get-Content (Get-ChildItem -LiteralPath $folder -Filter '*.nuspec' | Select-Object -First 1).FullName
    $attributions+="$($entry.Name) — $($spec.package.metadata.authors); $($spec.package.metadata.projectUrl)"
}

$attributions | Set-Content -LiteralPath (Join-Path $LicenseDirectory 'Aspen-DEPENDENCIES.txt') -Encoding utf8
