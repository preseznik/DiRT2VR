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

The game's 1600 × 1200 intermediate render is resized to the runtime's half-resolution eye dimensions. It is geometry stereo followed by a resize, without depth reconstruction. The original 120-degree visibility camera is only a conservative preparation experiment, not per-eye visibility rebuilding. Introductory benchmark cameras still render as 3D; menus/no eligible scene submit black. Session resources are process-owned to avoid OpenXR calls under DLL detach's loader lock; explicit runtime failure/exit is handled on the render thread. Graceful game shutdown, session restart and device replacement need dedicated lifecycle work.

## Instrumentation limitations

- Default tracing does not replay. `DIRT2VR_REPLAY_PROBE=1` repeats one main-view call at frame 3000; `DIRT2VR_INNER_REPLAY=1` selects the prepared-inner boundary. The separate `DIRT2VR_CONTINUOUS_REPLAY=1` mode repeats eligible main scenes from frame 300 onwards, with symmetric offsets and persistent GPU copies. Use the launcher so serial rendering and restoration are configured together.
- Both diagnostic modes copy the completed desktop backbuffer; the desktop retains the second view. Headset mode additionally submits the resized eye images to OpenXR. Direct rendering into headset-sized targets is not implemented.
- The three sampled image pairs use file numbers 900001/900002 (frame 3000), 903001/903002 (4500), and 906001/906002 (6000). Only frame 3000 collects full draw signatures. Other frames compare counts and camera restoration, not every shader/buffer value.
- `address-space.csv` uses `VirtualQuery` to cover the process application address range every 120 frames, distinguishing committed, reserved and free regions. An incomplete traversal is marked explicitly. These values include more than process-private usage and include OpenXR resources in headset-mode samples after session initialization.
- Trace output calls are synchronous. Camera/shader files and screenshots significantly perturb timing.
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
- [openRBRVR](https://github.com/Detegr/openRBRVR)

These are background leads. The measured results and addresses above come from the isolated local diagnostic, not from assuming another mod's hooks apply.
