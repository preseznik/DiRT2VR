# DX11 rendering notes

All addresses below are **RVAs for the fingerprinted executable**, not absolute live addresses. ASLR changes the module base. These are reverse-engineering observations, not public game APIs. Keep the executable SHA and instruction guards when adding hooks.

## Observed entry points

| RVA | Observation |
|---|---|
| `0x288930` | Main-view wrapper; calls the common scene with two inline camera records |
| `0x2889bb` / return `0x2889c0` | Main driving-view call to `0x33ad10`; used to exclude reflection/cube scenes |
| `0x33ad10` | Common scene entry: x86 thiscall, five stack arguments, `ret 0x14` |
| `0x330b70` | Hooked camera conversion/constant setup; selects the two verified inline eye cameras |
| `0x336be0` | Inner render sequence: camera setup, optional effects, virtual render method and postprocessing; six stack arguments |
| `0xba12d0` | Hooked camera upload boundary; overrides angular projection terms before engine upload |
| `0x331796` / `0xba1904` | Earlier leads around camera constant upload |
| `0xae4cde` | NaturalPoint initialization string reference |
| `0x11f28dc` | NP_GetData function pointer storage relative to module base; not yet used |

Scene prologue bytes currently guarded: `81 EC F8 00 00 00 56 8B F1`.

The main wrapper supplies arguments at `self+0x5e0`, `self+0x650`, `self->0x5dc`, a global renderer object, and `self->0x6c0`. The wrapper releases the last object after rendering. Its ownership cannot be assumed to extend to another engine frame.

`0x336be0` calls `0x330b70`, `0x325ab0`, optional `0x960770`, camera setup again, optional `0x3317a0`/`0x325bb0`, a virtual method at slot `0x14`, and optional `0x3318b0`. The opt-in inner replay hooks this boundary while prepared lists are still alive. Its guarded prologue is `53 55 8B 6C 24 14 56 57 8B 7C 24 20`; the six stack arguments are lists, camera A, camera B, context, scene, flags.

## High-resolution cockpit and prepared lists

The Subaru's `head-cam` camera requests `highResInterior="true"`. An exterior-camera result cannot validate its interior. The diagnostic clones the actual head camera into the benchmark-selected chase slot, rather than only moving an exterior camera into the car.

The outer scene builds a local 0xf4-byte list structure using `0x3260b0`. Its virtual preparation method `0x3257e0` partitions lists through `0xb5dda0`, including interior lists at offsets `0x8c` and `0x90`. Repeating that outer preparation loses 97 cockpit calls in the sampled reduced-effects run. Repeating `0x336be0` with the already prepared lists retains all 577 draw signatures and nearly identical pixels. This is evidence for that frame, not an exhaustive lifetime/state proof.

Cockpit depth passes reach `0x325ab0` via the inner renderer. Cockpit colour reaches `0x2929a0`, which supplies **`self+0x650` directly** to camera setup. Supplying translated camera copies only through scene arguments therefore misaligns cockpit depth and colour. Inner camera translation now requires `cameraA == self+0x5e0` and `cameraB == self+0x650`, temporarily translates their three position floats, and restores those floats with a scoped guard. The full 112-byte records are compared after restoration and the result logged. This bounded diagnostic requires serial rendering; it is not a concurrency-safe general camera API.

The water filter skips only PS hashes `08192877abdf068a` and `122478b22e69d9fa`, identified by reflected normal/reflection maps and wave/water parameters. It applies to both sampled passes and records suppressed calls separately. Generic null-PS/depth work is retained. Shader lookup on every draw and synchronous captures add substantial diagnostic overhead.

## Camera data

Reflection from the actual game shaders exposes `CameraParamsConstantBuffer` at **slot 3, 400 bytes**:

| Byte offset | Field | Bytes |
|---|---|---|
| 0 | view | 64 |
| 64 | projection | 64 |
| 128 | viewProjection | 64 |
| 192 | viewI | 64 |
| 256 | projectionI | 64 |
| 320 | viewIT | 64 |
| 384 | eyePositionWS | 16 |

Shaders also use model/modelViewProj data in other buffers. Changing only slot 3 will not establish correct stereo across all objects or CPU visibility.

The two scene camera records occupy 0x70 bytes. Position is at `0x40`. Comparing the uploaded main-view matrix to the records identifies the vectors at `0x10`, negative `0x20`, and negative `0x30` as camera Y, X, Z respectively. An initial offset along `0x30` moved the cockpit longitudinally, with uploaded view translation changing only in Z; it must not be interpreted as eye separation. The desktop `DIRT2VR_CAMERA_OFFSET` translates along **negative basis at `0x20`**, bounded to ±0.25 game units. Zero is the default. The four floats at `0x50` include observed values approximately `(0.907571, 0.075, 1000, 0)` for the 52-degree cockpit camera. Units are not yet calibrated to metres. Prepared visibility is still from the original camera, so newly exposed geometry remains an unresolved gate.

## In-game OpenXR integration

`DIRT2VR_HEADSET=1`, configured through `run-trace.ps1 -Headset`, supplies both inline camera records with each predicted OpenXR pose relative to an initial/F10 recenter reference. Only XYZ components of the three basis vectors and position change; metadata and W components remain intact. The scoped guard restores these fields after each eye and compares both full records. A failed comparison, missing eye projection upload or failed image copy stops submission.

Camera setup at `0x330b70` has signature `thiscall(self, context, camera, float nearPlane, bool upload)` and guarded prologue `55 8B EC 83 E4 F0 81 EC E4 00 00 00`. Upload at `0xba12d0` is `thiscall(self, context)` with guard `55 8B EC 83 E4 F0 83 EC 54`. Thread-local eligibility restricts the override to setup calls for the current renderer's `+0x5e0` or `+0x650` camera. Other renderers/reflections are excluded.

The upload context holds projection at `+0x120`, view at `+0x160`, viewProjection at `+0x1a0`, and inverse view at `+0x90`. Matrices use row-vector multiplication. The observed projection uses `m[11] = -1`, `m[15] = 0`, and a GL-like depth mapping. Replace only the angular terms `m[0]`, `m[5]`, `m[8]`, `m[9]` using the runtime FOV tangents, preserve engine depth, and recompute viewProjection. The original upload computes the projection inverse. Do not substitute a generic D3D depth convention.

Desktop diagnostic `trace-20260921-134907-445` used ±0.15 horizontal projection terms with no camera separation. All 8,280 pairs retained matching counts/restored cameras. Uploaded constant buffers contain the shifts, and captures move world/cockpit geometry by the expected total 120 pixels at width 800. This establishes the projection upload boundary; it does not validate all effects or head rotations.

The XR session uses the actual game D3D11 device after adapter-LUID/feature-level checks. `xrWaitFrame` and `xrLocateViews` run once for the pair, then each eye repeats only the prepared inner scene, captures the completed backbuffer into its own GPU texture and blits to its acquired OpenXR image. The blit decodes the game's gamma-encoded output to linear and uses a separate D3D11.1 context-state object. `SwapDeviceContextState` restores game bindings afterwards. The WARP test checks output and restored viewport/render target. Runtime colour appearance still needs headset inspection.

The game's 1600 × 1200 intermediate render is resized to the runtime's half-resolution eye dimensions. It is geometry stereo followed by a resize, without depth reconstruction. The original 120-degree visibility camera is only a conservative preparation experiment, not per-eye visibility rebuilding. Session resources are process-owned to avoid OpenXR calls under DLL detach's loader lock; explicit runtime failure/exit is handled on the render thread. Graceful game shutdown, session restart and device replacement need dedicated lifecycle work.

## Interactive screen, camera filtering and timing

Interactive mode launches the game without benchmark arguments and initializes OpenXR at the first desktop Present, making startup videos and menus available. The screen uses an `XrCompositionLayerQuad` in LOCAL space, with both-eye visibility and a fixed pose two metres along the recentered forward direction. Its width is 2.4 metres; height follows the desktop aspect ratio. Only one swapchain image is acquired/copied/released for a screen frame. Stereo frames retain the paired projection layer. No extra instance/session is created when switching.

F9 changes requested screen/cockpit mode; F10 recenters. The final implementation subclasses the single game window, consumes both normal/system key-down and key-up messages for these two keys, and ignores repeated down events. Other messages are forwarded to the previous procedure. Atomic pending actions are consumed once per presented frame. A per-frame XR marker prevents both the inner hook and Present from starting a second XR frame. If the inner hook does not submit, Present supplies the screen from the completed desktop image, including ordinary game UI. This does not keep game UI updating during a blocked game render thread.

Earlier `GetAsyncKeyState` polling observed F10 without consuming it. Windows' default key handler produces `SC_KEYMENU` on F10 release, entering menu handling and stopping the render loop until dismissed. The user reproduced that freeze during the follow-up. The native hidden-window regression test first verifies that unhooked F10 reaches `SC_KEYMENU`, then verifies two successive hooked presses each produce one recenter action with no forwarded key/menu event. It also tests repeat suppression, system-key messages and forwarding an ordinary key. Installation is limited to a window owned by this process; the callback is process-owned and its state is cleared on window destruction. Hot DLL unloading remains unsupported.

The first interactive test showed that manually selecting full 3D in the trailer was incorrect. Its captured records at frames 1200/3000 have near planes of 0.2 in both cameras; previous verified cockpit records and the Subaru `head-cam` XML use 0.075. Bumper XML uses 0.1 and sampled replay definitions use about 0.05. `CockpitCameraCandidate` now requires both near planes to match 0.075 within 0.00001, otherwise the scene renders once normally and is shown on the screen. This is a conservative prototype filter, not definitive semantic camera detection. Cockpit replays or overlays on the cockpit can still pass it. Do not advertise automatic handling of every pause/flashback/replay state.

The user confirmed screen readability/stability and the initial controls, then reported hitching and incorrect trailer scenes only in full 3D. Interactive mode now skips synchronous shader reflection dumps, per-draw/constant-buffer capture and screenshots unless `-CaptureDiagnostics` is explicitly selected. Shader hashes remain active for the two-water-shader filter. `-QuietTrace` applies the same reduced instrumentation to benchmarks. Frame/camera-check logs and periodic address-space traversal remain enabled, so this is not yet a release-performance build.

`GpuTimer` uses eight slots, each holding timestamp-start, timestamp-end and timestamp-disjoint queries. It starts immediately before the first eye's inner render and ends after the second eye's GPU copy/blit, before `xrEndFrame`. Previous slots are checked once per scene with `D3D11_ASYNC_GETDATA_DONOTFLUSH`; unavailable slots remain pending and a full ring skips sampling. Partial pairs close as invalid. Valid samples require both timestamp results, a non-disjoint clock, nonzero frequency and monotonic timestamps. This measures elapsed GPU timeline for that interval, including possible CPU submission gaps; it excludes outer scene preparation, compositor work and transport. The WARP test verifies query retirement, sample identity and no duplicated result. It does not establish headset performance.

## Per-eye local lighting

The user reproduced head-following headlights with the Subaru Impreza STI Group N at Battersea Bridge at night. `trace-20260921-144834-482` shows local-light preparation on a worker before main rendering, with no calls during either eye. Shader reflection identifies `lightPosVs`, `lightDirVs` and `mViewToLightClipSpace`; leaving these in the original camera's space explains the moving beam.

For the fingerprinted executable, three guarded parameter builders are replayed:

| RVA | thiscall arguments after self | Purpose |
|---|---|---|
| `0x7c0d30` | light, context | Point-light view-space position |
| `0x7c7d70` | light, context, material | Spotlight view-space position/direction |
| `0x7c7fa0` | light, context, material | Projected spotlight, including beam-mask transform |

The original calls are recorded with the current Present counter. A mutex transfers records from the preparation worker; the supported serial-render configuration joins that work before rendering. Each main scene snapshots only records from the exact frame and context. Neither old nor future records are reused. The 512-call bound rejects the entire set on overflow, causing screen fallback. All three hooks must install before cockpit stereo is admitted. The unit test covers these pointer-lifetime/context boundaries independently of the game.

After the first eligible camera setup/upload in each eye, call the original parameter builders with the updated context. They read its view at `+0x160` and inverse view at `+0x90`, and write material parameters through the engine setters. They do not rebuild visibility lists, place lights, simulate the car, or rerun shadow rendering. The builders' context reads lie within an aligned `0x1a0`-byte prefix; an original copy supplies restoration after the XR tick, including handled acquire/draw failure. Camera record restoration remains separate. This implementation still assumes the existing single main context and serial renderer.

Follow-up `trace-20260921-145248-255` recorded the same light refresh count for eye 1, eye 2 and restoration (for example 19/19/19 at frame 5040). The user confirmed the headlights stay with the car. Projected shadow maps use additional parameters; detailed shadow alignment and other events have not been accepted by this beam-position check.

`run-trace.ps1 -Interactive -TraceLights` enables sampled world/view matrices in `lights.csv`, first-call stacks and per-eye refresh counts. Normal operation keeps the correction enabled with those captures off.

## All-directions scenery visibility

The user reports scenery, vegetation and buildings disappearing when looking sideways or behind; cars and cockpit appear unaffected. The existing eye-camera changes happen in the inner scene, after the game has prepared directional scenery lists from the original camera. Widening that camera to 120 degrees cannot cover a full head turn.

For the supported executable, renderer preparation at RVA `0x292b60` builds a frustum from context `+0x1a0` through `0xd26c40`, then copies it into renderer `+0x340` through `0x2b7db0` (return address `0x292b99`). Worker preparation at `0x288c40` subsequently passes this volume to scenery routines, including `0x96a650`. The volume contains six planes followed by eight XYZ corners in 16-byte slots, occupying `0xe0` bytes. The constructor leaves the fourth corner lanes untouched, so the replacement storage is zero-initialized. Position/FOV metadata following it is populated separately and remains unchanged.

VR launches hook that copy, restricted to the exact main-renderer caller and the existing cockpit-camera signature. The hook uses the game's guarded constructor to generate coherent planes/corners from an orthographic world-to-clip box enclosing both original camera positions. Each axis extends by the larger original far distance plus an 8-unit translation margin; negative Z scale preserves the original clip handedness. Non-finite camera input, far distances outside 25–5000 units, or non-finite generated volumes retain the original frustum. Eye projections, reflection/shadow frustums and original distance/LOD metadata are unchanged. `-WideVisibility:$false` disables the hook for comparison; desktop-only diagnostics do not enable it.

This deliberately retains scenery in all directions rather than using a possibly stale headset pose during preparation. The first original/replacement volumes are saved in the trace as `visibility-original.bin` and `visibility-expanded.bin`. Automated maths checks cover all six directions, the finite boundary and invalid far distance. Receipt `trace-20260921-151802-020` confirms six inward-facing planes, with the camera centre approximately 1008 units inside each plane. The user reported: "That worked great. Everything remained visible." Other stages, occlusion paths and long-session performance still require coverage.

## Ground-cover instances in the second eye

Novigrad road rubble was visible in only the first eye. The stopped-car capture in launcher session `20260921-164513-570`, frame 8820, contains 575 draw calls in each eye, but ground-cover draws 430–432 (zero-based) change from 18/13/16 instances, starting at 0/18/31, to zero instances and zero starts. Mesh bindings and shaders match. The captured images show stones in the first eye and bare road in the second. Equal draw-call totals therefore missed the defect.

The correction is confined to vertex shader `5277336be52ad8fd` with pixel shader `1292783744ff6ba8`. During one OpenXR stereo pair, it records the ordered first-eye indexed-instance draws and restores their instance count/start only when the second eye supplies zero for both. Matching includes all 32 vertex stream bindings/strides/offsets, index buffer/format/offset, input layout, topology, index count/start and base vertex. Nonzero second-eye draws are unchanged. A mismatch or the 64-batch limit invalidates further correction for that pair; no previous-frame records are reused. The unit test covers pair lifetime, mismatches, nonempty draws and overflow.

This repairs draw arguments, not the engine's unidentified batch-clearing routine. It depends on the same-frame instance-buffer contents remaining valid; binding identity alone does not prove that. The ground-cover vertex shader already uses the per-eye camera view-projection and eye position. Other shader variants are outside this correction and require separate coverage. No rubble asset is removed and no simulation is repeated.

Follow-up launcher session `20260921-165532-601` used proxy SHA-256 `2C8E5DA361E50BB7D467122AE1A37B9B7E3D4CB0EE04299DFDE8FC8B8CB5E4C8`. Asked whether the rubble now appeared in both eyes, the tester reported: "Looks good now. No need for you to check result. This is solved." No follow-up image capture was requested after that confirmation. This establishes visual acceptance for the reported Novigrad defect, not a complete survey of ground cover or a performance measurement. All eight native CTests passed, including the new ground-cover pair test.

For an opt-in stopped-car diagnostic, launch `DiRT2VR.exe --launch --no-ui --diagnostic-capture` (add `--game "<game folder>"` when running a developer launcher elsewhere). Once in cockpit VR, create `capture.request` in that session's log directory. The next eligible frame divisible by 60 consumes the request. Up to four captures per process write `capture-<frame>-eye-1.csv` / `eye-2.csv` and PPM images numbered 920002/920003 onwards. CSV columns are draw kind, element count, instance count, VS hash, PS hash, index/vertex start, base vertex, first instance, topology, vertex slot-0 buffer/stride/offset, index buffer/format/offset. The opt-in flag also enables shader reflection dumps and ground-cover draw diagnostics. Normal launches disable it. Synchronous capture stalls must be excluded from performance acceptance.

## Instrumentation limitations

- Default tracing does not replay. `DIRT2VR_REPLAY_PROBE=1` repeats one main-view call at frame 3000; `DIRT2VR_INNER_REPLAY=1` selects the prepared-inner boundary. The separate `DIRT2VR_CONTINUOUS_REPLAY=1` mode repeats eligible main scenes from frame 300 onwards, with symmetric offsets and persistent GPU copies. Use the launcher so serial rendering and restoration are configured together.
- Both diagnostic modes copy the completed desktop backbuffer; the desktop retains the second view. Headset mode additionally submits the resized eye images to OpenXR. Direct rendering into headset-sized targets is not implemented.
- The three sampled image pairs use file numbers 900001/900002 (frame 3000), 903001/903002 (4500), and 906001/906002 (6000). Only frame 3000 collects full draw signatures. Other frames compare counts and camera restoration, not every shader/buffer value.
- `address-space.csv` uses `VirtualQuery` to cover the process application address range every 120 frames, distinguishing committed, reserved and free regions. An incomplete traversal is marked explicitly. These values include more than process-private usage and include OpenXR resources in headset-mode samples after session initialization.
- Trace output calls are synchronous. Camera/shader files and screenshots significantly perturb timing.
- Interactive runs disable those detailed captures by default; `screen-frames.csv`, `headset-frames.csv` and `gpu-frames.csv` distinguish screen submissions, stereo integrity and measured GPU intervals. The summary reports them separately.
- Draw signatures contain call type, element count, instance count, VS and PS hash; they are not a full GPU capture. Buffer contents, hull/domain shaders, blend/depth state and command-list effects need separate investigation.
- A hash of zero means no known shader mapping, including a null shader. The shader registry does not track lifetime beyond the bounded diagnostic process.
- Manual stack candidates are filtered possible CALL return addresses, not reliable unwound call stacks.
- One device/immediate context/swapchain is assumed. Resize, device replacement and long-running memory overhead are not release-ready.
- Shared state effects may be on CPU and GPU. Equality of draw counts alone would not pass stereo acceptance.

## Background references

- [Codemasters/AMD DiRT 2 DX11 rendering presentation](https://media.gdcvault.com/gdc10/slides/Story_Jon_AdvancedVisualEffectsWithDirect3D_DiRT2DirectX11Technology.pdf)
- [vorpX developer discussion of DiRT 2 geometry stereo](https://www.vorpx.com/forums/topic/codemasters-dirt-23grid/)
- [OpenXR frame timing](https://registry.khronos.org/OpenXR/specs/1.1/man/html/xrWaitFrame.html)
- [D3D11 context-state creation](https://learn.microsoft.com/en-us/windows/win32/api/d3d11_1/nf-d3d11_1-id3d11device1-createdevicecontextstate)
- [D3D11 context-state switching](https://learn.microsoft.com/en-us/windows/win32/api/d3d11_1/nf-d3d11_1-id3d11devicecontext1-swapdevicecontextstate)
- [OpenXR quad composition layer](https://registry.khronos.org/OpenXR/specs/1.1/man/html/XrCompositionLayerQuad.html)
- [D3D11 timestamp/disjoint queries](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ne-d3d11-d3d11_query)
- [D3D11 non-flushing query reads](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ne-d3d11-d3d11_async_getdata_flag)
- [Windows F10 and SC_KEYMENU behavior](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-keydown)
- [openRBRVR](https://github.com/Detegr/openRBRVR)

These are background leads. The measured results and addresses above come from the isolated local diagnostic, not from assuming another mod's hooks apply.
