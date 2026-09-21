# Implementation status — 2026-09-21

## Decision

The native x86 OpenXR route now receives game-rendered stereo frames on this machine. The user reports the headset run looked very good and both turning and leaning looked correct. **The planned first milestone is not complete.** Runtime submission and camera/projection checks pass, with positive subjective head-tracking confirmation; calibrated scale, broader visibility checks and a complete interactive race still need acceptance. Do not advance to playable-alpha/release claims.

The reduced-effects route renders the prepared inner scene for each predicted headset eye pose/projection and presents independent images through OpenXR. Repeating the outer scene still consumes interior work. Remaining gates include calibrated scale, systematic per-eye visibility coverage, simulation integrity, proper menus/HUD and measured GPU performance.

## In-game OpenXR, September 21

The game proxy now creates an OpenXR session on the actual game D3D11 device, verifies the runtime adapter requirements, and renders both eyes for one predicted display time. It transforms both verified inline cameras, applies asymmetric projections before the engine's camera upload, captures each eye independently and restores graphics bindings around presentation. F10 recenters. The launcher configures a reduced-effects cockpit benchmark, a wider original visibility camera and desktop VSync off. Four CTest suites pass, including pose/projection maths, frame preparation/failure paths and WARP GPU copy/state-restoration checks.

The SteamVR x86 preflight at `xr-20260921-135943-703` created the instance, HMD system and session successfully. Runtime: SteamVR/OpenXR in Meta compatibility mode 2.17.10; orientation and position tracking advertised. The game session negotiates 1536 × 1632 eye images from a 3072 × 3264 recommendation, while each game-rendered source is 1600 × 1200.

| Receipt under `artifacts/` | Observed result |
|---|---|
| `trace-20260921-134907-445` | Desktop asymmetric projection control: 8,280 pairs, matching counts/restored cameras, confirmed uploaded projection shifts and expected image movement |
| `trace-20260921-140254-729` | First game-to-OpenXR benchmark: VISIBLE/FOCUSED, at least 8,416 accepted pairs in periodic logs, normal benchmark exit; old absolute-frame capture triggers missed the scene |
| `trace-20260921-140522-838` | Instrumented run: **8,462 submitted scene pairs**, 8,461 marked visible; zero camera-restoration failures, missing submitted projections or incomplete submitted pairs; no render-thread shutdown/error |
| `trace-20260921-140830-532` | Final build with later capture triggers: runtime remained non-visible; one startup pair, zero visible pairs, no late captures. This run does **not** pass the headset diagnostic's 60-visible-pair threshold |

The instrumented run's per-eye draw counts also match, but this does not prove simulation integrity or correct visibility. Early captured pairs show actual introductory game scenes with different eye frustums; these samples do not establish cockpit geometry or comfort. The final source additionally samples pairs 1800 and 3600 to reach driving after the intro. The user watched the in-game headset run, reported that it looked very good, and explicitly confirmed that turning and leaning both looked correct when asked about cockpit response and scenery around the pillar. This is the first positive in-headset game and head-tracking acceptance report. It does not establish all viewing angles, physical scale, a stationary geometric measurement or a full interactive race. The earlier confirmed cube remains separate evidence.

With OpenXR active, `trace-20260921-140522-838` measured peak committed address space **1,122,258,944 bytes**, minimum free space **822,829,056 bytes**, and minimum largest free block **253,165,568 bytes**. All sampled traversals completed under the unchanged 2 GB ceiling. These measurements include allocated XR resources but do not establish long-session/stage-transition headroom. Tick duration includes runtime waiting; there is no measured GPU frame-time budget or 90 Hz acceptance.

This is an automatic benchmark with a script launcher, not an end-user executable or interactive VR race. Menus/no eligible scene are black; forced benchmark introductory cameras still render in 3D. HUD, reflection correctness, world scale (default one game unit per metre), visibility outside original prepared lists, device/session recovery and graceful session shutdown remain unfinished. The current process-owned diagnostic session avoids runtime calls from DLL detach's loader lock. The script restores shared settings and isolated camera/effects assets on exit.

The practical next step is interactive race launch and a virtual menu screen, retaining this reduced-effects baseline. Validate recentering, stationary pillar disocclusion, clipping and full-race timing during that work. The intended end-user shape remains a small launcher/configuration application plus an in-process renderer DLL; the current PowerShell benchmark is a development entry point.

Final restoration checks at `trace-20260921-140830-532/restoration-check.json` pass: shared settings and isolated camera/effects bytes restored, installed executable and `xlive.dll` unchanged, no installed-game proxy, deployed isolated proxy matching the final build, and all DiRT 2 processes exited. The last source change adds later capture triggers; those triggers still need a visible headset run. The summary tool deliberately returns failure for insufficient visible submissions instead of treating a hidden session as visual success.

## Continuous rendering, September 21

`-ContinuousReplay` opts into two renders for every eligible main scene, with symmetric fixed eye positions. Both images are copied before subsequent rendering can overwrite them. Incomplete or resized pairs cannot be used, and a failed capture, camera restoration or draw-count comparison disables further replay. Each camera position is restored immediately after its eye renders. Geometry visibility still comes from the original prepared list.

The initial continuous tests started at frame 3000. The current build starts at frame 300 to cover more of the benchmark, rather than just its later section. This remains the built-in benchmark with one supported car/stage, not interactive full-race acceptance.

| Receipt under `artifacts/` | Dimensions per retained view | Result |
|---|---|---|
| `trace-20260921-131334-749` | 800 × 600 | Identical cameras: **3,815** consecutive pairs, frames 3000–6814; no draw-count, restoration or pair failures |
| `trace-20260921-131648-390` | 800 × 600 | Separation 0.064 game units: **664** consecutive pairs, frames 3000–3663; no failures |
| `trace-20260921-132006-406` | 1600 × 1200 | Initial larger-size check: **128** consecutive pairs, frames 3000–3127; no failures |
| `trace-20260921-132334-304` | 1600 × 1200 | Final build, separation 0.064 game units: **3,290** consecutive pairs, frames **300–3589**; no draw-count, camera-restoration or pair failures; frame-3000 signatures match |

The identical-camera run's captured pairs at frames 3000, 4500 and 6000 remain nearly pixel-identical; mean absolute channel differences are all below 0.001/255. The separated-view images retain the interior and show lateral parallax. Different pair totals reflect different presented-frame rates before the benchmark exits; they are not proof of equal simulation progression.

At 800 × 600 with separated views, sampled address-space queries all completed. Peak committed virtual memory was 1,094,656,000 bytes; minimum free address space was 834,269,184 bytes (795.6 MiB), with a minimum largest free block of 442,892,288 bytes (422.4 MiB). The process address ceiling reported by Windows was 2,147,418,112 bytes. These are actual address-space measurements, unlike the earlier private-memory-only observations, but exclude future OpenXR allocations. CPU submission time was 1.401 ms median / 2.037 ms p95; this is neither GPU time nor a headset frame-rate claim.

Three CTest suites now pass. The added WARP GPU test verifies distinct left/right contents after the source is overwritten, rejects stale/incomplete/resized pairs, and checks recovery after resizing. Live benchmark images separately validate game-rendered contents. Commands and the summary tool are documented in the README.

The final 1600 × 1200 run completed the benchmark and retained the high-resolution cockpit in both captured views. It measured 1,112,580,096 bytes peak committed address space, 820,187,136 bytes minimum free space (782.2 MiB), and a minimum largest free block of 430,632,960 bytes (410.7 MiB). All address-space samples were complete. Neither this desktop resolution nor the CPU submission measurements establish a 90 Hz headset budget; GPU timing, OpenXR resources and game frame pacing are still outstanding.

The final receipt's `restoration-check.json` confirms byte-identical restoration of shared graphics settings and the isolated camera/effects assets. The installed executable and `xlive.dll` are unchanged, no proxy was installed in the Steam directory, and the isolated proxy matches the tested build. Both isolated game processes exited. No Large Address Aware patch was applied.

The source repository is `https://github.com/preseznik/DiRT2VR`, with work on `preseznik/native-vr-prototype`. Game files, extracted shaders, local captures, dependencies and binaries remain untracked.

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
| x86 MSVC build with pinned dependencies | `tools/build.cmd`; four CTest suites pass | No release packaging |
| D3D11 proxy and full export forwarding | Game creates feature-level 0xb000 device; standalone probe passes through same proxy | Single game device/swapchain diagnostic design |
| Executable/prologue guards | SHA-256 plus scene-entry byte match | Only this exact 1.1.0.0 build |
| Shader reflection and camera hooks | Camera setup/upload prologue guards, pose/FOV maths, live per-eye upload/restoration receipts | One executable; physical scale, culling and visual tracking still unvalidated |
| Standalone stereo OpenXR rendering | `artifacts/xr-render-visible.txt`: 1,733 submitted frames, states VISIBLE/FOCUSED, user saw cube | No game rendering or PSVR2 test |
| Runtime selection | Latest preflight `artifacts/xr-20260921-135943-703/probe.txt` | SteamVR manifest override applies only to child process |
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

1. Follow the positive look/lean report with stationary pillar/recenter and broader-angle checks during interactive race integration. Inspect the later eye captures; fix any scale or geometry defects before claiming full VR acceptance.
2. Establish correct CPU visibility for both eyes and head movement. The 120-degree original preparation camera is an experiment, not a complete solution.
3. Establish simulation timing, animation and resource-history integrity during a complete interactive race. Do not restore whole opaque engine objects or replay simulation updates speculatively.
4. Measure CPU/GPU timing and address-space headroom over stage changes at recorded game and headset dimensions. Keep the reduced-effects baseline while diagnosing defects; reintroduce optional effects individually.
5. Add a virtual menu screen, clear game-state transitions and interactive cockpit launch before packaging for end users. Validate wheel/gamepad controls and actual PSVR2 hardware separately.

If a safe DX11 render boundary cannot be found, pause this route and scope a separate DX9 prototype. Existing geometry-stereo precedent makes DX9 worth investigating, but this code cannot simply be retargeted: DX9 has different hooks/resources and OpenXR has no native DX9 graphics binding. A DX9 route would need a proven D3D11-compatible presentation/interoperability design and new game-specific stereo work. No DX9 implementation or fallback was enabled.

## Acceptance still outstanding

Systematic cockpit stereo/6DOF coverage beyond the positive first report, correct newly revealed geometry across viewing angles, calibrated world scale, GPU/game frame pacing, full-race state integrity, menus/HUD/replays/flashbacks, pause tracking, seat controls, wheel/gamepad VR bindings, optional comfort effects, long-session address-space stability, disconnect/reconnect, broad content checks and PSVR2 hardware testing remain outstanding. Keyboard recenter and short-run address-space measurements are implemented; their existence does not satisfy those wider acceptance gates.
