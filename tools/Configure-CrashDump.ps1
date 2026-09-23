# Opt-in Windows Error Reporting capture for dirt2_game.exe only.
# Run Enable/Disable from an administrator PowerShell on the affected PC.
param([ValidateSet('Enable','Disable','Status')][string]$Mode = 'Status')
$ErrorActionPreference = 'Stop'
$subkey = 'SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\dirt2_game.exe'
$marker = 'DiRT2VRDiagnosticOwner'
$expected = @{
    $marker = 'DiRT2VR-PC3-20260923'
    DumpCount = 1
    DumpType = 2
}
# Leave DumpFolder unset: WER uses the crashing user's LOCALAPPDATA\CrashDumps.
$views = @([Microsoft.Win32.RegistryView]::Registry32)
if ([Environment]::Is64BitOperatingSystem) { $views += [Microsoft.Win32.RegistryView]::Registry64 }
$roots = @()

function Assert-Owned($key) {
    if ($key.GetValue($marker) -ne $expected[$marker]) {
        throw 'An existing dirt2_game.exe dump configuration was preserved. Do not replace it; use its current capture settings.'
    }
    if ($key.GetSubKeyNames().Count) { throw 'Dump configuration has unexpected subkeys; preserved.' }
    foreach ($name in $key.GetValueNames()) {
        if (-not $expected.ContainsKey($name) -or $key.GetValue($name) -ne $expected[$name]) {
            throw "Dump configuration changed outside this tool ($name); preserved."
        }
        $kind = if ($name -eq $marker) { [Microsoft.Win32.RegistryValueKind]::String } else { [Microsoft.Win32.RegistryValueKind]::DWord }
        if ($key.GetValueKind($name) -ne $kind) { throw "Dump configuration value type changed ($name); preserved." }
    }
}

try {
    if ($Mode -ne 'Status') {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object Security.Principal.WindowsPrincipal($identity)
        if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'Windows requires administrator rights for crash-dump settings. Open PowerShell as administrator and run this command again. Run the game normally, without elevation.'
        }
    }
    # Check both views before changing either. Refuse existing third-party settings.
    foreach ($view in $views) {
        $root = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $view)
        $roots += $root
        $key = $root.OpenSubKey($subkey)
        try {
            if ($Mode -eq 'Status') {
                if ($null -eq $key) { Write-Output "$view`: no application-specific dump settings" }
                else {
                    foreach ($name in $key.GetValueNames()) { Write-Output "$view`: $name = $($key.GetValue($name))" }
                }
            } elseif ($null -ne $key) { Assert-Owned $key }
        } finally { if ($null -ne $key) { $key.Dispose() } }
    }
    if ($Mode -eq 'Enable') {
        foreach ($root in $roots) {
            $key = $root.CreateSubKey($subkey)
            try {
                # Owner first: interrupted setup remains recoverable with Disable.
                if ($key.GetValueNames().Count) { Assert-Owned $key }
                $key.SetValue($marker, $expected[$marker], [Microsoft.Win32.RegistryValueKind]::String)
                $key.Flush()
                $key.SetValue('DumpCount', 1, [Microsoft.Win32.RegistryValueKind]::DWord)
                $key.SetValue('DumpType', 2, [Microsoft.Win32.RegistryValueKind]::DWord)
                $key.Flush()
            } finally { $key.Dispose() }
        }
        Write-Output 'Enabled: launch DiRT2VR normally and reproduce the crash once.'
        Write-Output 'After the game exits, look for dirt2_game.exe.<pid>.dmp in %LOCALAPPDATA%\CrashDumps.'
        Write-Output 'One full dump is retained (it can be large). Then run this script with -Mode Disable.'
    } elseif ($Mode -eq 'Disable') {
        foreach ($root in $roots) {
            $key = $root.OpenSubKey($subkey)
            try { if ($null -ne $key) { Assert-Owned $key } }
            finally { if ($null -ne $key) { $key.Dispose() } }
            # Not recursive: an unexpected child key prevents deletion.
            $root.DeleteSubKey($subkey, $false)
        }
        Write-Output 'Removed this tool''s per-game dump settings. Existing dump files were kept.'
    }
} catch {
    Write-Error $_ -ErrorAction Continue
    Write-Output 'If setup was interrupted, run -Mode Disable to remove any settings owned by this tool.'
    exit 1
} finally {
    foreach ($root in $roots) { $root.Dispose() }
}
