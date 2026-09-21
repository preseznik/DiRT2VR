# DiRT 2 VR investigation

**Development prototype. This is not a playable VR mod.**

The fingerprint-guarded **32-bit DiRT 2 DX11 proxy now connects the game renderer to OpenXR**. An experimental Subaru/Baja benchmark renders each eye from the runtime's predicted pose and asymmetric projection, with reduced effects and F10 recentering. SteamVR has accepted thousands of game-rendered pairs in a visible/focused session. The Quest 3 tester reports that the image looked very good and both turning and leaning looked correct. Calibrated world scale, broader visibility checks and full-race simulation integrity still need acceptance before this becomes a usable VR mode.

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

Outputs: `build/ninja/bin/xr_probe.exe`, `d3d11.dll`, and test executables. Four CTest suites run after building, covering exports, frame lifecycle, GPU copies/state restoration and camera maths. The system D3D11 export table generates all 51 named/ordinal forwarders on this machine. Build directories must be reconfigured when changing compilers.

## Headset diagnostic

Start SteamVR and connect the headset. Wear it before starting the visual test:

```powershell
.\tools\run-xr-probe.ps1           # Runtime/device/session preflight
.\tools\run-xr-probe.ps1 -Render   # Coloured cube, approximately 20 visible seconds
```

The script uses SteamVR's `steamxr_win32.json` through a process-local `XR_RUNTIME_JSON`; it restores the caller's environment afterwards. It does not change the global active OpenXR runtime. Pass `-Runtime` for a different SteamVR installation. A visible session and at least 60 submitted frames are required for the visual diagnostic to pass. The default resolution scale is 0.5 per dimension. Receipts go into timestamped `artifacts/xr-*` directories.

The cube is rendered separately for each headset eye using its predicted pose and projection. It is not captured from DiRT 2. No gamepad/wheel input is needed for this short test.

## Experimental in-game headset benchmark

Prepare the isolated game copy described below, build the proxy and XML converter, then connect and wear the headset through SteamVR:

```powershell
.\tools\run-trace.ps1 -Headset
python .\tools\summarize_headset.py .\artifacts\trace-TIMESTAMP # After the launcher exits.
```

This runs the automatic benchmark, not an interactive race. The script selects cockpit/reduced effects/serial rendering, disables desktop VSync, temporarily widens the original visibility camera to 120 degrees, and renders at 1600 × 1200 by default. Each geometry-rendered eye is copied into a separate OpenXR image at half the runtime's recommended dimensions (1536 × 1632 on the tested Quest 3 setup). There is no depth reconstruction or alternating-eye rendering. The image resize is not additional rendered detail.

Both eyes use the same prepared scene and predicted display time. **F10 recenters**; `-WorldScale` adjusts game units per metre, default 1 and not physically calibrated. `-Runtime` selects another SteamVR x86 manifest without changing the global runtime. The game must use the runtime's graphics adapter. This implementation requires D3D11.1 context-state support to preserve the game's graphics state around presentation.

Menus have no virtual screen yet. Frames without an eligible main scene are black; the benchmark's forced introductory cameras still render in 3D. HUD, mirrors, seat adjustment, pause/transition handling and visibility outside the original prepared lists are unfinished. A wider preparation camera reduces some clipping risks but does not establish correct per-eye culling. Session restart and device replacement are unsupported.

`headset-frames.csv` records successful submissions, visibility, eye draws, projection uploads and camera restoration. Its tick duration includes `xrWaitFrame`, so it is not GPU time. Captures at submitted scene pairs 120, 600, 1800 and 3600 use image numbers 900001 through 900008; the trace maps them to actual game frames. Earlier pairs can show the intro. `address-space.csv` includes the game's OpenXR allocations. Diagnostic capture and tracing stalls prevent these runs from proving a 90 Hz performance budget.

All temporary settings and copied camera/effects assets are restored when the launcher exits. **Let it finish**, as described in the restoration instructions below.

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

This desktop mode renders each eligible main scene twice starting at diagnostic frame 300. The offset is the **total separation**, applied symmetrically as -0.032/+0.032 game units. Use zero for an identical-camera control. Both eye images are copied to persistent independent GPU textures before the next frame overwrites the game's output. A failed capture, changed camera record or draw-count mismatch disables further continuous replay. The desktop shows the second eye; use the separate `-Headset` mode for OpenXR output.

`stereo-frames.csv` records pair completeness, draw counts, camera restoration and CPU submission time. `address-space.csv` measures committed, reserved and free virtual memory plus the largest free region every 120 frames. CPU submission time is not GPU time. Image pairs are saved at frames 3000, 4500 and 6000 when reached. The summary script must run **after the launcher exits**, not while a receipt is still being written.

Add `-RenderWidth 1600 -RenderHeight 1200` to temporarily test a larger windowed render size. Actual captured dimensions, rather than requested XML values, establish whether the override took effect. The supported configuration still uses shared Documents settings; the same restoration requirements apply.

To remove instrumentation from the isolated copy, close it and remove only `artifacts/game/d3d11.dll`. The installed Steam game has no deployed proxy and remains independently launchable.

## Source map

| Path | Purpose |
|---|---|
| `src/proxy.cpp`, `tools/generate_exports.py` | System D3D11 forwarding, game identity gate |
| `src/trace.cpp` | DX11/shader/camera traces and opt-in scene replay |
| `src/eye_pair.*` | Independent GPU eye images with pair/size validation |
| `src/eye_blit.*`, `src/game_xr.*` | Game-device OpenXR session and graphics-state-preserving eye presentation |
| `src/camera_math.*` | Recenter, six-axis eye poses and asymmetric frustum maths |
| `src/xr_frames.*` | Session events, predicted eye poses, swapchains and paired frame submission |
| `src/xr_probe.cpp`, `src/xr_render_probe.cpp` | Standalone native headset test |
| `tools/inspect_game.py` | Read-only PE/string/x86 inspection; optional `pefile` and `capstone` dependencies |
| `tools/compare_passes.py` | Draw-sequence and captured-image comparison |
| `tools/summarize_continuous.py` | Continuous pair, image and address-space evidence summary |
| `tools/summarize_headset.py` | In-game OpenXR submission, camera/projection and memory receipt summary |
| `tools/build-xml-converter.ps1`, `tools/xml-convert/` | Pinned EGO library and minimal binary-XML converter |
| `tests/` | Export coverage, OpenXR lifecycle checks and WARP GPU eye-copy validation |

Six-axis eye camera integration and keyboard recentering are experimental. Stereo visibility, HUD/menu composition, wheel/gamepad VR bindings, seat controls, full comfort settings, an end-user launcher/installer and PSVR2 acceptance remain outstanding. Runtime submission is not completed stereo or full-race acceptance.
