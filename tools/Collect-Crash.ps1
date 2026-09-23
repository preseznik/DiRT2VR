# Read-only evidence collection. No registry, driver, game or save changes.
param(
    [string]$GameRoot,
    [ValidateRange(1,168)][int]$Hours = 24,
    [string]$OutputPath = (Join-Path ([Environment]::GetFolderPath('Desktop')) 'DiRT2VR-crash.json')
)
$ErrorActionPreference = 'Stop'
$issues = New-Object 'System.Collections.Generic.List[string]'
$report = [ordered]@{ CollectedUtc=[DateTime]::UtcNow.ToString('o'); Since=(Get-Date).AddHours(-$Hours).ToString('o') }
try {
    $report.Gpu = @(Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion,DriverDate,PNPDeviceID)
    $report.Windows = Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber
} catch { $issues.Add('Hardware information: ' + $_.Exception.Message) }
try {
    $events = @(Get-WinEvent -FilterHashtable @{LogName='Application';Id=1000,1001,1002;StartTime=(Get-Date).AddHours(-$Hours)} -ErrorAction SilentlyContinue)
    $report.Events = @($events | Where-Object { $_.ToXml() -match '(?i)dirt2|DiRT2VR|vrserver|vrcompositor' } | Select-Object -First 50 | ForEach-Object {
        $xml = [xml]$_.ToXml()
        [ordered]@{ TimeUtc=$_.TimeCreated.ToUniversalTime().ToString('o'); Id=$_.Id; Provider=$_.ProviderName; Message=$_.Message; Data=@($xml.Event.EventData.Data | ForEach-Object { [ordered]@{Name=$_.Name;Value=$_.'#text'} }) }
    })
} catch { $issues.Add('Windows crash events: ' + $_.Exception.Message) }
if ($GameRoot) {
    $root=[IO.Path]::GetFullPath($GameRoot)
    $report.GameFiles=@('dirt2.exe','dirt2_game.exe','d3d11.dll','xlive.dll','DiRT2VR.exe') | ForEach-Object {
        $path=Join-Path $root $_
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $file=Get-Item -LiteralPath $path
            [ordered]@{Name=$_.ToString();Size=$file.Length;Version=$file.VersionInfo.FileVersion;Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
        }
    }
}
$report.Errors=@($issues)
$output=[IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $output) { throw "Output already exists; choose a different OutputPath: $output" }
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $output -Encoding UTF8
Write-Output "Saved $output"
