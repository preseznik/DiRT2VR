param([switch]$ReplayExperiment, [switch]$ReducedEffects, [switch]$LowPost,
    [switch]$SerialRender, [switch]$NoAmbientOcclusion, [switch]$NoMotionBlur, [switch]$SkipWater,
    [switch]$Cockpit, [ValidateRange(-0.25,0.25)][double]$CameraOffset=0, [switch]$InnerReplay,
    [switch]$ContinuousReplay,
    [ValidateRange(0,4096)][int]$RenderWidth=0, [ValidateRange(0,4096)][int]$RenderHeight=0,
    [ValidateRange(-0.25,0.25)][double]$ProjectionShift=0,
    [switch]$Headset, [switch]$Interactive, [switch]$CaptureDiagnostics, [switch]$QuietTrace, [switch]$TraceLights, [switch]$WideVisibility=$true,
    [ValidateRange(0.25,4)][double]$WorldScale=1,
    [string]$Runtime='C:\Program Files (x86)\Steam\steamapps\common\SteamVR\steamxr_win32.json')
$ErrorActionPreference = 'Stop'
if ($QuietTrace -and $CaptureDiagnostics) { throw 'Choose -QuietTrace or -CaptureDiagnostics, not both' }
if ($Interactive) { $Headset=$true }
if ($Headset) {
    if (!(Test-Path -LiteralPath $Runtime)) { throw "SteamVR x86 manifest not found: $Runtime" }
    $ContinuousReplay=$true; $SerialRender=$true; $Cockpit=$true; $ReducedEffects=$true
    $LowPost=$true; $NoAmbientOcclusion=$true; $NoMotionBlur=$true
    if (!$RenderWidth -and !$RenderHeight) { $RenderWidth=1600; $RenderHeight=1200 }
}
if (($RenderWidth -eq 0) -ne ($RenderHeight -eq 0) -or
    ($RenderWidth -ne 0 -and ($RenderWidth -lt 320 -or $RenderHeight -lt 240))) {
    throw 'Set both render dimensions, at least 320 by 240, or leave both zero'
}
if ($ContinuousReplay) {
    if (!$SerialRender) { throw '-ContinuousReplay requires -SerialRender' }
    $ReplayExperiment = $true; $InnerReplay = $true
}
if ($InnerReplay -and !$ReplayExperiment) { throw '-InnerReplay requires -ReplayExperiment' }
if ($InnerReplay -and $CameraOffset -ne 0 -and !$SerialRender) {
    throw 'Inline cockpit camera translation requires -SerialRender'
}
$root = Split-Path $PSScriptRoot -Parent
$game = Join-Path $root 'artifacts\game'
$expected = '49B1E00EA1D4BD02E633CEED63390B5CAE07601333F673C4EFD8D2F0EB54FE48'
if (!(Test-Path -LiteralPath (Join-Path $game 'dirt2_game.exe'))) {
    throw 'Copy your licensed game into artifacts\game first. See README.md.'
}
if ((Get-FileHash -LiteralPath (Join-Path $game 'dirt2_game.exe') -Algorithm SHA256).Hash -ne $expected) {
    throw 'Unsupported executable. No diagnostic files were deployed.'
}
if (Get-Process dirt2* -ErrorAction SilentlyContinue) {
    throw 'A DiRT 2 process is running. Close it before starting a new diagnostic.'
}
$proxy = Join-Path $root 'build\ninja\bin\d3d11.dll'
if (!(Test-Path -LiteralPath $proxy)) { throw 'Build the Win32 targets first.' }
$output = Join-Path $root ('artifacts\trace-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $output | Out-Null
[ordered]@{
    replay = [bool]$ReplayExperiment; reducedEffects = [bool]$ReducedEffects
    lowPost = [bool]$LowPost; serialRender = [bool]$SerialRender
    noAmbientOcclusion = [bool]$NoAmbientOcclusion; noMotionBlur = [bool]$NoMotionBlur
    skipWater = [bool]$SkipWater; cockpit = [bool]$Cockpit
    cameraOffsetGameUnits = $CameraOffset; innerReplay = [bool]$InnerReplay
    continuousReplay = [bool]$ContinuousReplay
    renderWidth = $RenderWidth; renderHeight = $RenderHeight
    projectionShift = $ProjectionShift
    headset = [bool]$Headset; interactive = [bool]$Interactive; worldScale = $WorldScale
    captureDiagnostics = !$QuietTrace -and (!$Interactive -or [bool]$CaptureDiagnostics)
    traceLights = [bool]$TraceLights
    wideVisibility = [bool]($Headset -and $WideVisibility)
    executableSha256 = $expected; proxySha256 = (Get-FileHash -LiteralPath $proxy -Algorithm SHA256).Hash
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'diagnostic-options.json')
Copy-Item -LiteralPath $proxy -Destination (Join-Path $game 'd3d11.dll')
Copy-Item -LiteralPath (Join-Path $root 'config\vr_benchmark.xml') -Destination (Join-Path $game 'vr_benchmark.xml')
$previousOutput = $env:DIRT2VR_OUTPUT
$previousLogging = $env:DIRT2VR_LOGGING
$previousActive = $env:DIRT2VR_ACTIVE
$previousReplay = $env:DIRT2VR_REPLAY_PROBE
$previousWater = $env:DIRT2VR_SKIP_WATER
$previousOffset = $env:DIRT2VR_CAMERA_OFFSET
$previousInner = $env:DIRT2VR_INNER_REPLAY
$previousContinuous = $env:DIRT2VR_CONTINUOUS_REPLAY
$previousProjection = $env:DIRT2VR_PROJECTION_SHIFT
$previousHeadset = $env:DIRT2VR_HEADSET
$previousInteractive = $env:DIRT2VR_INTERACTIVE
$previousCapture = $env:DIRT2VR_CAPTURE_DIAGNOSTICS
$previousLights = $env:DIRT2VR_TRACE_LIGHTS
$previousVisibility = $env:DIRT2VR_WIDE_VISIBILITY
$previousScale = $env:DIRT2VR_WORLD_SCALE
$previousRuntime = $env:XR_RUNTIME_JSON
$settings = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'My Games\DiRT2\hardwaresettings\hardware_settings_config.xml'
$originalSettings = $null
$effects = Join-Path $game 'postprocess\effects.xml'
$originalEffects = $null
$cameras = Join-Path $game 'cars\sti\cameras.xml'
$originalCameras = $null
try {
    if ($Cockpit) {
        $converter = Join-Path $root 'artifacts\xml-convert\xml-convert.exe'
        if (!(Test-Path -LiteralPath $converter)) { throw 'Build tools/xml-convert first.' }
        $originalCameras = [IO.File]::ReadAllBytes($cameras)
        [IO.File]::WriteAllBytes((Join-Path $output 'cameras-original.bin'), $originalCameras)
        $decodedCamera = Join-Path $output 'cameras-cockpit.xml'
        & $converter decode $cameras $decodedCamera
        if ($LASTEXITCODE -ne 0) { throw 'Camera decode failed' }
        [xml]$cameraXml = [IO.File]::ReadAllText($decodedCamera)
        $head = $cameraXml.SelectSingleNode("//View[@ident='head-cam']")
        $chase = $cameraXml.SelectSingleNode("//View[@ident='chase_close']")
        if (!$head -or !$chase) { throw 'Expected Subaru camera definitions not found' }
        $replacement = $head.CloneNode($true)
        $replacement.SetAttribute('ident','chase_close')
        if ($Headset) { $replacement.SelectSingleNode("Parameter[@name='fov']").SetAttribute('value','120.0') }
        foreach ($p in $replacement.SelectNodes("AccelerationBasedShake/Parameter[@type='scalar']")) { $p.SetAttribute('value','0.0') }
        foreach ($name in @('maxBodyOffset','maxHeadOffset','maxHeadLook')) {
            $replacement.SelectSingleNode("Parameter[@name='$name']").SetAttribute('value','0.0')
        }
        $replacement.SelectSingleNode("Parameter[@name='headBuffeting']").SetAttribute('value','false')
        if ($Interactive) {
            $interactiveHead=$replacement.CloneNode($true)
            $interactiveHead.SetAttribute('ident','head-cam')
            $head.ParentNode.ReplaceChild($interactiveHead,$head) | Out-Null
        }
        $chase.ParentNode.ReplaceChild($replacement,$chase) | Out-Null
        $cameraXml.Save($decodedCamera)
        $encodedCamera = Join-Path $output 'cameras-cockpit.bin'
        & $converter encode $decodedCamera $encodedCamera
        if ($LASTEXITCODE -ne 0) { throw 'Camera encode failed' }
        Copy-Item -LiteralPath $encodedCamera -Destination $cameras
        Write-Host 'Isolated Subaru chase view uses its cockpit camera; interactive mode also adjusts the head camera.'
    }
    if ($NoMotionBlur) {
        $converter = Join-Path $root 'artifacts\xml-convert\xml-convert.exe'
        if (!(Test-Path -LiteralPath $converter)) { throw 'Build tools/xml-convert first. See rendering notes.' }
        $originalEffects = [IO.File]::ReadAllBytes($effects)
        [IO.File]::WriteAllBytes((Join-Path $output 'effects-original.bin'), $originalEffects)
        $decoded = Join-Path $output 'effects-no-blur.xml'
        & $converter decode $effects $decoded
        if ($LASTEXITCODE -ne 0) { throw 'Effects decode failed' }
        [xml]$effectXml = [IO.File]::ReadAllText($decoded)
        $blurParameters = $effectXml.SelectNodes("//ParameterGroup[@name='MotionBlur']/Param[@name='blurLength']")
        if ($blurParameters.Count -eq 0) { throw 'No known motion blur parameters found' }
        foreach ($parameter in $blurParameters) { $parameter.InnerText = '0.0' }
        $effectXml.Save($decoded)
        $encoded = Join-Path $output 'effects-no-blur.bin'
        & $converter encode $decoded $encoded
        if ($LASTEXITCODE -ne 0) { throw 'Effects encode failed' }
        Copy-Item -LiteralPath $encoded -Destination $effects
        Write-Host "Zeroed $($blurParameters.Count) motion-blur parameters in isolated assets."
    }
    if ($ReducedEffects -or $LowPost -or $SerialRender -or $NoAmbientOcclusion -or $RenderWidth) {
        $originalSettings = [IO.File]::ReadAllBytes($settings)
        [IO.File]::WriteAllBytes((Join-Path $output 'settings-original.xml'), $originalSettings)
        [xml]$configuration = [IO.File]::ReadAllText($settings)
        if ($ReducedEffects) {
            foreach ($effect in @('crowd','particles','shadows')) {
                $configuration.hardware_settings_config.$effect.SetAttribute('enabled','false')
            }
        }
        if ($LowPost) { $configuration.hardware_settings_config.postprocess.SetAttribute('quality','0') }
        if ($SerialRender) { $configuration.hardware_settings_config.cpu.threadStrategy.SetAttribute('parallelUpdateRender','false') }
        if ($NoAmbientOcclusion) { $configuration.hardware_settings_config.dynamic_ambient_occ.SetAttribute('enabled','false') }
        if ($RenderWidth) {
            $resolution = $configuration.hardware_settings_config.graphics_card.resolution
            $resolution.SetAttribute('width',$RenderWidth.ToString())
            $resolution.SetAttribute('height',$RenderHeight.ToString())
            $resolution.SetAttribute('fullscreen','false')
        }
        if ($Headset) { $configuration.hardware_settings_config.graphics_card.resolution.SetAttribute('vsync','0') }
        $configuration.Save($settings)
        Copy-Item -LiteralPath $settings -Destination (Join-Path $output 'settings-applied.xml')
        Write-Host 'Temporary graphics settings applied; original bytes will be restored when the game exits.'
    }
    $env:DIRT2VR_OUTPUT = $output
    $env:DIRT2VR_LOGGING = '1'
    $env:DIRT2VR_ACTIVE = '1'
    $env:DIRT2VR_REPLAY_PROBE = if ($ReplayExperiment) { '1' } else { '0' }
    $env:DIRT2VR_SKIP_WATER = if ($SkipWater) { '1' } else { '0' }
    $env:DIRT2VR_CAMERA_OFFSET = $CameraOffset.ToString([Globalization.CultureInfo]::InvariantCulture)
    $env:DIRT2VR_INNER_REPLAY = if ($InnerReplay -and $ReplayExperiment) { '1' } else { '0' }
    $env:DIRT2VR_CONTINUOUS_REPLAY = if ($ContinuousReplay) { '1' } else { '0' }
    $env:DIRT2VR_PROJECTION_SHIFT = $ProjectionShift.ToString([Globalization.CultureInfo]::InvariantCulture)
    $env:DIRT2VR_HEADSET = if ($Headset) { '1' } else { '0' }
    $env:DIRT2VR_INTERACTIVE = if ($Interactive) { '1' } else { '0' }
    $env:DIRT2VR_CAPTURE_DIAGNOSTICS = if (!$QuietTrace -and (!$Interactive -or $CaptureDiagnostics)) { '1' } else { '0' }
    $env:DIRT2VR_TRACE_LIGHTS = if ($TraceLights) { '1' } else { '0' }
    $env:DIRT2VR_WIDE_VISIBILITY = if ($Headset -and $WideVisibility) { '1' } else { '0' }
    $env:DIRT2VR_WORLD_SCALE = $WorldScale.ToString([Globalization.CultureInfo]::InvariantCulture)
    if ($Headset) { $env:XR_RUNTIME_JSON = (Resolve-Path -LiteralPath $Runtime).Path }
    Write-Host "Diagnostic started. Trace: $output"
    if ($Interactive) { Write-Host 'Interactive VR: menus start on a virtual screen. Select the Subaru STI cockpit. F9 switches screen/cockpit, F10 recenters. Use screen mode for pause, replays and menus. Quit the game normally and let this window finish restoring settings.' }
    elseif ($Headset) { Write-Host 'Experimental headset benchmark. F9 switches screen/cockpit, F10 recenters. Visibility and world scale are unvalidated.' }
    else { Write-Host 'This is desktop instrumentation, not VR. The replay experiment is unvalidated.' }
    $launch = @{FilePath=(Join-Path $game 'dirt2.exe'); WorkingDirectory=$game;
        ArgumentList='-benchmark vr_benchmark.xml'; WindowStyle='Hidden'}
    if ($Interactive) { $launch.Remove('ArgumentList'); $launch.WindowStyle='Normal' }
    if ($originalSettings -or $originalEffects -or $originalCameras) { $launch.Wait = $true }
    Start-Process @launch | Out-Null
} finally {
    if ($originalCameras) { [IO.File]::WriteAllBytes($cameras, $originalCameras) }
    if ($originalEffects) { [IO.File]::WriteAllBytes($effects, $originalEffects) }
    if ($originalSettings) {
        [IO.File]::WriteAllBytes($settings, $originalSettings)
        Write-Host 'Original graphics settings restored.'
    }
    $env:DIRT2VR_OUTPUT = $previousOutput
    $env:DIRT2VR_LOGGING = $previousLogging
    $env:DIRT2VR_ACTIVE = $previousActive
    $env:DIRT2VR_REPLAY_PROBE = $previousReplay
    $env:DIRT2VR_SKIP_WATER = $previousWater
    $env:DIRT2VR_CAMERA_OFFSET = $previousOffset
    $env:DIRT2VR_INNER_REPLAY = $previousInner
    $env:DIRT2VR_CONTINUOUS_REPLAY = $previousContinuous
    $env:DIRT2VR_PROJECTION_SHIFT = $previousProjection
    $env:DIRT2VR_HEADSET = $previousHeadset
    $env:DIRT2VR_INTERACTIVE = $previousInteractive
    $env:DIRT2VR_CAPTURE_DIAGNOSTICS = $previousCapture
    $env:DIRT2VR_TRACE_LIGHTS = $previousLights
    $env:DIRT2VR_WIDE_VISIBILITY = $previousVisibility
    $env:DIRT2VR_WORLD_SCALE = $previousScale
    $env:XR_RUNTIME_JSON = $previousRuntime
}
