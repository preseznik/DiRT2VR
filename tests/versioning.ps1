$ErrorActionPreference='Stop'
$next=Join-Path (Split-Path $PSScriptRoot -Parent) 'tools/next-version.ps1'
foreach ($case in @(
    @('0.1.0','Patch','0.1.1'),
    @('0.1.1','Patch','0.1.2'),
    @('0.1.99','Patch','0.1.100'),
    @('0.1.99','Minor','0.2.0'),
    @('0.9.99','Major','1.0.0')
)) {
    $actual=& $next -Current $case[0] -Bump $case[1]
    if ($actual -ne $case[2]) { throw "Incorrect version: $actual" }
}
foreach ($bad in @('0.1.0-alpha.5','0.1','01.1.0','-1.0.0','invalid')) {
    $rejected=$false
    try { & $next -Current $bad | Out-Null } catch { $rejected=$true }
    if (!$rejected) { throw "Invalid version accepted: $bad" }
}
'10 versioning checks passed.'
