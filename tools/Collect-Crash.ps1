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
    $events = @(Get-WinEvent -FilterHashtable @{LogName='Application';Id=1000,1001,1002,1026;StartTime=(Get-Date).AddHours(-$Hours)} -ErrorAction SilentlyContinue)
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
    try {
        $sha=[Security.Cryptography.SHA256]::Create()
        try { $id=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($root.TrimEnd('\').ToUpperInvariant())))).Replace('-','').Substring(0,24) }
        finally { $sha.Dispose() }
        $userRoot=Join-Path $env:LOCALAPPDATA ('DiRT2VR\'+$id)
        $workerPath=Join-Path $userRoot 'worker-error.json'
        $failures=if (Test-Path -LiteralPath $workerPath) { @(Get-Content -LiteralPath $workerPath -Raw | ConvertFrom-Json) } else { @() }
        $targets=@((Join-Path $root 'postprocess\effects.xml'),(Join-Path $root 'cars\sti\cameras.xml'),(Join-Path $root 'DiRT2VR\backups'))
        foreach ($failure in $failures) {
            if ($failure.Target) {
                $target=[IO.Path]::GetFullPath($failure.Target)
                if ($target.StartsWith($root.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { $targets += $target }
            }
        }
        $report.Preparation=[ordered]@{
            InstallationId=$id
            LastWorkerFailure=$failures
            AssetRecoveryPending=(Test-Path -LiteralPath (Join-Path $root 'DiRT2VR\backups\pending.json'))
            GraphicsRecoveryPending=(Test-Path -LiteralPath (Join-Path $userRoot 'graphics-pending.json'))
            Targets=@($targets | Select-Object -Unique | ForEach-Object {
                $target=$_
                $item=[ordered]@{Path=$target;Exists=(Test-Path -LiteralPath $target)}
                if ($item.Exists) {
                    $item.Attributes=(Get-Item -LiteralPath $target -Force).Attributes.ToString()
                    try { $acl=Get-Acl -LiteralPath $target; $item.Owner=$acl.Owner; $item.Sddl=$acl.Sddl }
                    catch { $item.AclError=$_.Exception.Message }
                }
                $item
            })
        }
    } catch { $issues.Add('Preparation information: ' + $_.Exception.Message) }
}
$report.Errors=@($issues)
$output=[IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $output) { throw "Output already exists; choose a different OutputPath: $output" }
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $output -Encoding UTF8
Write-Output "Saved $output"
