# Implementation status — 2026-09-21

## Decision

The native x86 OpenXR route is proven on this machine. The DX11 stereo route remains unproven. **The planned first milestone is not complete.** Do not advance to the playable-alpha/release stages or describe the current binaries as a VR mod.

The reduced-effects route now has a repeatable high-resolution cockpit scene and a successful lateral-camera geometry experiment at one sampled frame. Repeating the outer scene still consumes interior work; repeating the prepared inner scene preserves it. The remaining gates include headset eye poses/projections, per-eye visibility, full-race state integrity and actual headset presentation.

## Reduced-effects experiment, September 21

All runs use the isolated Subaru/Baja benchmark at 800 × 600. Settings/assets are restored after each run. The retained profile disables crowds, particles, shadows, ambient occlusion and motion blur, selects low postprocessing and serial rendering, and suppresses two specifically identified water pixel shaders. Required depth rendering remains enabled. This is a deliberately reduced visual baseline, not proof that every disabled feature is inherently incompatible with VR.

| Receipt under `artifacts/` | Result at frame 3000 |
|---|---|
| `trace-20260921-123539-482` | Crowd/particle/shadow settings alone: 497/499 calls; AO/water differences remain |
| `trace-20260921-123753-160` | Also low postprocessing, serial render, no AO: 457/461 calls; four extra water calls |
| `trace-20260921-124212-683` | Also zero motion blur and skip identified water shaders: exterior 487/487 matching signatures, almost identical pixels |
| `trace-20260921-124520-276` | Genuine cockpit camera with outer replay: 555/458; 97 interior draws disappear |
| `trace-20260921-125142-177` | Prepared inner replay with genuine cockpit: **577/577**, identical signatures; 532/480,000 pixels change, mean absolute channel difference **0.001051/255** |
| `trace-20260921-125629-889` | Offset camera copies by 0.064 game units: 499/499 signatures, but cockpit depth/colour disagree because colour reads the inline camera directly; rejected |
| `trace-20260921-125912-150` | Inline camera offset: 571/571 signatures, full interior responds and camera records restore exactly; uploaded matrices identify this initial axis as longitudinal, so it is not a stereo pair |
| `trace-20260921-130251-711` | Corrected lateral inline offset: **571/571** matching signatures; cockpit pillar/wheel/dashboard move horizontally and reveal previously obscured scenery; both camera records restore exactly |

The cockpit camera is materially different from the exterior view: `head-cam` has `highResInterior="true"`. The test clones this camera definition into the benchmark-selected `chase_close` slot, retaining its interior geometry and disabling camera shake/buffeting. The successful same-camera cockpit pair retains dashboard, hands, wheel and pillars. All changed pixels in that pair are above row 300; the lower half is byte-identical. The remaining small image discrepancy is not fully classified.

The current camera experiment temporarily changes only the positions in the two verified inline camera records, then restores them. This addresses the observed split between cockpit depth and colour consumers; it does not solve visibility, asymmetric projections or world scale. Full render state is never copied back speculatively.

The lateral result's `camera-offset-comparison.json` compares matching main-camera constant uploads: view translation changes by `(-0.064003, 0, -0.000003815)` with an unchanged projection. This verifies a lateral camera displacement at the renderer, not a screen-space image shift. Its `before.png` and `after.png` show the high-resolution interior intact and more fence/scenery visible beside the pillar. Prepared lists still use the original visibility result, so this single successful disocclusion does not prove all newly visible objects render correctly. The original HUD remains at desktop screen coordinates and mirrors still use the existing game's reflection. These are diagnostic views, not finished headset eye images.

The reproducible command and restoration procedure are in the README. XML preparation uses [EgoEngineModding's MIT-licensed library](https://github.com/EgoEngineModding/Ego-Engine-Modding), pinned at `f3fe9ee0f6e8379c64bc4a29e1d1b586bba5d1c1`, with a minimal noninteractive wrapper. Its build passed with zero warnings/errors; the native build and both existing CTest suites passed. The runtime captures, rather than those unit tests, establish the rendering observations.

The final lateral-offset benchmark exited normally. `trace-20260921-130251-711/restoration-check.json` confirms byte-identical restoration of shared graphics settings and the isolated effects/camera assets, unchanged supported installed executable hash, no proxy in the Steam installation, matching `xlive.dll`, and a deployed proxy matching the final build. No DiRT 2 process remained. Only one frame was double-rendered in these runs; benchmark completion does not prove sustained two-eye stability or race timing.

## Implemented and checked

| Component | Evidence | Limit |
|---|---|---|
| x86 MSVC build with pinned dependencies | `tools/build.cmd`; two CTest suites pass | No release packaging |
| D3D11 proxy and full export forwarding | Game creates feature-level 0xb000 device; standalone probe passes through same proxy | Single game device/swapchain diagnostic design |
| Executable/prologue guards | SHA-256 plus scene-entry byte match | Only this exact 1.1.0.0 build |
| Shader reflection and camera discovery | `artifacts/trace-006/shaders`, camera binaries | A camera layout is not yet a supported game camera hook |
| Standalone stereo OpenXR rendering | `artifacts/xr-render-visible.txt`: 1,733 submitted frames, states VISIBLE/FOCUSED, user saw cube | No game rendering or PSVR2 test |
| Runtime selection | Final preflight `artifacts/xr-20260916-195621-288/probe.txt` | SteamVR manifest override applies only to child process |
| Frame lifecycle handling | Deterministic tests cover paired eye submission, predicted display time, hidden/tracking-invalid frames, stopping, one-eye acquire failure, draw exception and invalid image index | Not exhaustive runtime/device-loss acceptance |
| Reproducible trace and XR launch scripts | Unique artifact folders, supported hash required, replay opt-in | Built-in game benchmark still uses shared Documents settings |

Final normal-operation smoke test: `tools/run-trace.ps1` with replay disabled completed the built-in benchmark and exited both isolated game processes. Receipt: `artifacts/trace-20260916-195926-163`; benchmark result `Documents/My Games/DiRT2/benchmarks/DiRT2VR_Diagnostic_20-01-16_on_16-09-2026.xml` contains 9,154 benchmark samples. No `EXPERIMENT` entry appears in this trace. This verifies normal desktop benchmark operation with the final diagnostic DLL; it is not a full interactive race or VR acceptance test.

The mock frame test exercises the real frame loop but bypasses graphics allocation. The live cube run separately exercised runtime negotiation, swapchain textures, explicit typed RTV creation, per-eye projection and submission. SteamVR exposes typeless D3D11 textures: creating an RTV with a null descriptor failed; the implementation now uses the negotiated format explicitly.

## Original game experiments, September 16

`trace-001` established actual DX11 use and completed the built-in benchmark. `trace-003` repeated an offscreen scene and is not evidence about the main view. `trace-004` had a deployment/file-lock problem and must not be used as validation.

`trace-005` restricted replay to main-view caller RVA `0x2889c0`. The original pass made 466 draw calls, the repeat 394. Image differences already rejected naive replay.

`trace-006` added shader-labelled draw sequences. At frame 3000 the first main pass made **436 calls**, the repeat **332**. Comparing signatures finds 161 removed and 57 added calls (some correspond to changes in batching rather than missing objects):

- 56 crowd instanced calls become 56 ordinary indexed calls with the same VS/PS signatures. Instance counts differ; this requires investigation of the crowd/instancing render queues.
- 14 particle calls with PS `f4f9cbe04cdd6a93` disappear. Reflection exposes `depthFadeScaleAndBias` and particle colour constants.
- 49 calls with a null pixel shader disappear, consistent with depth-only work. Precise shadow/depth pass classification remains to be established.
- Other alpha-tested/depth and screen-effect calls differ.

Both screenshots show the same car and timer. The repeat has visibly different dust and motion blur. This does **not** establish unchanged physics, animation or GPU history; it only bounds the immediate experiment.

Local artifacts:

- `artifacts/trace-006/replay-before.png`, `replay-after.png`
- `artifacts/trace-006/draws-pass-1.csv`, `draws-pass-2.csv`
- `artifacts/trace-006/pass-comparison.json`
- `artifacts/trace-006/trace.log`, `stacks.txt`, `frames.csv`

The trace contains 9,733 presented frames with peak measured process private usage **784.85 MiB**. This is a desktop diagnostic run at **800 × 600**, VSync interval 1, not VR. Private usage is not total reserved virtual address space and does not establish room for stereo resources. No 90 Hz headset performance claim is supported. The executable remains non-LAA; no 4 GB patch was applied.

An attempted alternate benchmark hardware-settings file did not take effect: captures remained 800 × 600. The delivered benchmark XML therefore omits that ineffective option. Resolution/VSync changes must be verified at actual swapchain creation, not inferred from an XML file.

## Preserved boundaries

The installed executable still hashes to the supported SHA-256. No `d3d11.dll` was deployed into the installed Steam game. The copied and installed `xlive.dll` hashes match (`5BBE4CD8EE97FBD22EB5AB457963DB73C5D7889BA77952EEA318CF2DA6D934C8`). No global OpenXR registry setting was changed. Test-generated benchmark files exist under the game's normal Documents directory.

## Next gate

1. Validate the prepared inner boundary beyond the single captured frame and across other cars/stages. Check simulation timing, animation and resource history over sustained two-eye rendering. Do not restore whole opaque engine objects or replay simulation updates speculatively.
2. Keep the reduced-effects baseline while classifying the residual image differences. Reintroduce optional effects individually only after their eye-dependent work is understood.
3. Establish camera handedness, units, projection asymmetry and CPU visibility. Render both eyes from one captured simulation state. Per-object transforms and culling must agree with the eye camera.
4. Only then connect that renderer to `XrFrames`, test a stationary cockpit pillar lean, and measure frame time and virtual address-space headroom at recorded eye dimensions.
5. Complete a race with timing, particles, animation and transitions checked before starting alpha features.

If a safe DX11 render boundary cannot be found, pause this route and scope a separate DX9 prototype. Existing geometry-stereo precedent makes DX9 worth investigating, but this code cannot simply be retargeted: DX9 has different hooks/resources and OpenXR has no native DX9 graphics binding. A DX9 route would need a proven D3D11-compatible presentation/interoperability design and new game-specific stereo work. No DX9 implementation or fallback was enabled.

## Acceptance still outstanding

True cockpit stereo and 6DOF, correct newly revealed geometry, proper world scale, game frame pacing, full-race state integrity, menus/HUD/replays/flashbacks, pause tracking, recenter and seat controls, wheel/gamepad coverage, optional comfort effects, actual address-space measurements, disconnect/reconnect, broad content checks and PSVR2 hardware testing all remain outstanding.
