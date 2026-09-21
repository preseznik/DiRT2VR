# DiRT 2 VR investigation

**Development prototype. This is not a playable VR mod.**

The repository currently provides a working native **32-bit OpenXR/D3D11 headset diagnostic** and a fingerprint-guarded **DiRT 2 DX11 instrumentation DLL**. The game renderer is not connected to OpenXR. With optional effects disabled, the prepared inner scene can render continuous pairs of high-resolution cockpit views into independent GPU textures. Headset eye poses/projections, visibility and simulation integrity still need validation before this becomes a VR mode.

On 2026-09-16, Quest 3 through SteamVR displayed the diagnostic cube; the user confirmed seeing it. The visible/focused run submitted 1,733 stereo frames at 1536 × 1632 per eye. This proves the local presentation route, not DiRT 2 VR, 90 Hz game performance, or PSVR2 compatibility.

See [implementation status and evidence](docs/implementation-status.md) and [rendering investigation](docs/rendering-notes.md).

## Build

Requires Windows x64, Visual Studio C++ x86 tools and Windows SDK, CMake 3.24+, Ninja, Git, and Python 3. The resulting programs are x86.

```powershell
.\tools\bootstrap.ps1
.\tools\build.cmd
```

Dependencies are pinned and verified; existing dependency changes are never reset. `DIRT2VR_VS_ROOT` selects a Visual Studio installation and `DIRT2VR_PYTHON` optionally selects Python. For this machine's already configured build:

```powershell
$env:DIRT2VR_VS_ROOT = 'C:\Program Files\Microsoft Visual Studio\2022\Community'
$env:DIRT2VR_PYTHON = 'C:\Path\To\python.exe' # Replace with your Python executable.
.\tools\build.cmd
```

Outputs: `build/ninja/bin/xr_probe.exe`, `d3d11.dll`, and the frame-lifecycle test. CTest runs after building. The system D3D11 export table generates all 51 named/ordinal forwarders on this machine. Build directories must be reconfigured when changing compilers.

## Headset diagnostic

Start SteamVR and connect the headset. Wear it before starting the visual test:

```powershell
.\tools\run-xr-probe.ps1           # Runtime/device/session preflight
.\tools\run-xr-probe.ps1 -Render   # Coloured cube, approximately 20 visible seconds
```

The script uses SteamVR's `steamxr_win32.json` through a process-local `XR_RUNTIME_JSON`; it restores the caller's environment afterwards. It does not change the global active OpenXR runtime. Pass `-Runtime` for a different SteamVR installation. A visible session and at least 60 submitted frames are required for the visual diagnostic to pass. The default resolution scale is 0.5 per dimension. Receipts go into timestamped `artifacts/xr-*` directories.

The cube is rendered separately for each headset eye using its predicted pose and projection. It is not captured from DiRT 2. No gamepad/wheel input is needed for this short test.

## Isolated game diagnostic

Only the installed 1.1.0.0 executable with this SHA-256 is supported:

```text
49B1E00EA1D4BD02E633CEED63390B5CAE07601333F673C4EFD8D2F0EB54FE48
```

Use a licensed local copy. The working copy on this machine already exists at `artifacts/game`. To prepare another copy, copy the original game's complete directory there, preserving its existing `xlive.dll`. Do not distribute the copied game or extracted shaders. `artifacts`, `.deps`, and `build` are ignored by version control.

```powershell
.\tools\run-trace.ps1
```

The script checks the game hash, requires the isolated game to be closed, copies the diagnostic proxy and benchmark XML into the isolated copy, and launches `dirt2.exe`. It never deploys into the Steam installation. The built-in benchmark uses a Subaru STI at Baja Iron and writes its normal results under Documents/My Games/DiRT2/benchmarks. The isolated copy still uses the game's shared Documents settings/profile. Diagnostics currently capture the game's ordinary 800 × 600 windowed output on this machine.

For the original, known-failing whole-scene replay experiment:

```powershell
.\tools\run-trace.ps1 -ReplayExperiment
python .\tools\compare_passes.py .\artifacts\trace-TIMESTAMP
```

This explicitly repeats the main scene once at diagnostic frame 3000. It is off by default and is **not a VR mode**. Without a camera offset, the captured before/after images use the same camera and engine frame. Do not use these traces as performance acceptance: shader dumping, per-draw tracing and synchronous screenshots introduce stalls.

The reduced-effects cockpit experiment instead repeats the prepared inner scene. Its asset preparation additionally requires .NET SDK 10 and the pinned MIT-licensed EGO XML library:

```powershell
.\tools\build-xml-converter.ps1
.\tools\run-trace.ps1 -ReplayExperiment -InnerReplay -Cockpit -ReducedEffects -LowPost -SerialRender -NoAmbientOcclusion -NoMotionBlur -SkipWater
```

This temporarily disables crowds, particles, shadows, ambient occlusion and motion blur; selects low postprocessing and serial rendering; and suppresses two identified water shaders. It keeps required geometry/depth passes. The benchmark's chase view is replaced with the Subaru's actual high-resolution `head-cam` definition. Settings and copied assets are backed up in the trace directory and restored byte-for-byte after game exit. **Let this command finish:** terminating its PowerShell host can bypass restoration. If interrupted, close the isolated game and restore `settings-original.xml` to the shared Documents hardware settings, `effects-original.bin` to the isolated `postprocess/effects.xml`, and `cameras-original.bin` to the isolated `cars/sti/cameras.xml`.

Add `-CameraOffset 0.064` for a one-frame lateral camera experiment. This is **game units, not calibrated metres or headset IPD**. In inner replay it temporarily translates both verified inline camera positions and restores them after the second pass, so cockpit depth and colour use the same camera. It does not recompute prepared visibility lists or implement headset projection. Receipts include the chosen options and proxy hash. Run `compare_passes.py` only after the benchmark has completed.

For sustained desktop two-view rendering:

```powershell
.\tools\run-trace.ps1 -ContinuousReplay -Cockpit -ReducedEffects -LowPost -SerialRender -NoAmbientOcclusion -NoMotionBlur -SkipWater -CameraOffset 0.064
python .\tools\summarize_continuous.py .\artifacts\trace-TIMESTAMP
```

This opt-in mode renders each eligible main scene twice starting at diagnostic frame 300. The offset is the **total separation**, applied symmetrically as -0.032/+0.032 game units. Use zero for an identical-camera control. Both eye images are copied to persistent independent GPU textures before the next frame overwrites the game's output. A failed capture, changed camera record or draw-count mismatch disables further continuous replay. The desktop shows the second eye; there is no headset output yet.

`stereo-frames.csv` records pair completeness, draw counts, camera restoration and CPU submission time. `address-space.csv` measures committed, reserved and free virtual memory plus the largest free region every 120 frames. CPU submission time is not GPU time. Image pairs are saved at frames 3000, 4500 and 6000 when reached. The summary script must run **after the launcher exits**, not while a receipt is still being written.

Add `-RenderWidth 1600 -RenderHeight 1200` to temporarily test a larger windowed render size. Actual captured dimensions, rather than requested XML values, establish whether the override took effect. The supported configuration still uses shared Documents settings; the same restoration requirements apply.

To remove instrumentation from the isolated copy, close it and remove only `artifacts/game/d3d11.dll`. The installed Steam game has no deployed proxy and remains independently launchable.

## Source map

| Path | Purpose |
|---|---|
| `src/proxy.cpp`, `tools/generate_exports.py` | System D3D11 forwarding, game identity gate |
| `src/trace.cpp` | DX11/shader/camera traces and opt-in scene replay |
| `src/eye_pair.*` | Independent GPU eye images with pair/size validation |
| `src/xr_frames.*` | Session events, predicted eye poses, swapchains and paired frame submission |
| `src/xr_probe.cpp`, `src/xr_render_probe.cpp` | Standalone native headset test |
| `tools/inspect_game.py` | Read-only PE/string/x86 inspection; optional `pefile` and `capstone` dependencies |
| `tools/compare_passes.py` | Draw-sequence and captured-image comparison |
| `tools/summarize_continuous.py` | Continuous pair, image and address-space evidence summary |
| `tools/build-xml-converter.ps1`, `tools/xml-convert/` | Pinned EGO library and minimal binary-XML converter |
| `tests/` | Export coverage, OpenXR lifecycle checks and WARP GPU eye-copy validation |

Game 6DOF camera integration, stereo visibility, HUD/menu composition, recenter/seat bindings, comfort settings, installer and PSVR2 acceptance are not implemented. The prepared scene result is a prototype rendering foothold, not completed stereo acceptance.
