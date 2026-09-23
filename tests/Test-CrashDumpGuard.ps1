# Exercise the actual configuration ownership guard without touching the registry.
$ErrorActionPreference = 'Stop'
$path = Join-Path $PSScriptRoot '../tools/Configure-CrashDump.ps1'
$tokens=$null; $parseErrors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$parseErrors)
if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
$guard=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Assert-Owned'},$false)
if ($null -eq $guard) { throw 'Missing ownership guard' }
Invoke-Expression $guard.Extent.Text
$marker='DiRT2VRDiagnosticOwner'
$expected=@{ $marker='DiRT2VR-PC3-20260923'; DumpCount=1; DumpType=2 }

class FakeKey {
    [hashtable]$Values=@{}
    [hashtable]$Kinds=@{}
    [string[]]$Children=@()
    [object] GetValue([string]$name) { return $this.Values[$name] }
    [string[]] GetValueNames() { return @($this.Values.Keys) }
    [string[]] GetSubKeyNames() { return $this.Children }
    [Microsoft.Win32.RegistryValueKind] GetValueKind([string]$name) { return $this.Kinds[$name] }
}
function New-Owned {
    $key=[FakeKey]::new()
    $key.Values=@{ $marker=$expected[$marker]; DumpType=2; DumpCount=1 }
    $key.Kinds=@{ $marker=[Microsoft.Win32.RegistryValueKind]::String; DumpType=[Microsoft.Win32.RegistryValueKind]::DWord; DumpCount=[Microsoft.Win32.RegistryValueKind]::DWord }
    return $key
}
function Check([string]$name,$key,[bool]$reject) {
    $failed=$false
    try { Assert-Owned $key } catch { $failed=$true }
    if ($failed -ne $reject) { throw "Unexpected guard result: $name" }
    Write-Output "PASS $name"
}
Check 'complete owned configuration' (New-Owned) $false
$key=New-Owned; $key.Values.Remove('DumpCount'); $key.Values.Remove('DumpType')
Check 'interrupted setup can be recovered' $key $false
$key=New-Owned; $key.Values.Remove($marker)
Check 'existing unowned configuration preserved' $key $true
$key=New-Owned; $key.Values[$marker]='another tool'
Check 'foreign owner preserved' $key $true
$key=New-Owned; $key.Values.DumpCount=10
Check 'changed setting preserved' $key $true
$key=New-Owned; $key.Values.DumpFolder='C:\Elsewhere'
Check 'extra setting preserved' $key $true
$key=New-Owned; $key.Kinds.DumpType=[Microsoft.Win32.RegistryValueKind]::String
Check 'changed value type preserved' $key $true
$key=New-Owned; $key.Children=@('Unexpected')
Check 'unexpected child key preserved' $key $true
