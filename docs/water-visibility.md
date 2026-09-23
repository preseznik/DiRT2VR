# Water and straight-line scenery pop-in investigation

## Report and scope

The user reports water appearing at close range and different reflections between eyes on Baja / Ensenada Sprint. Trackside scenery also pops while driving straight, independently of head direction. They explicitly want water retained while investigating. A stopped-car headset capture now reproduces the reflection mismatch; the user reports that the water pop-in appears resolved.

## Findings, 2026-09-23

- `Session.VrStartInfo` previously always enabled `DIRT2VR_SKIP_WATER`. The filter suppresses only pixel shaders `08192877abdf068a` and `122478b22e69d9fa`. Shader reflection identifies normal/reflection maps and wave parameters. Seven other water shader variants are not suppressed, including interactive ripple/depth/foam variants. Switching between suppressed and unsuppressed variants is a plausible contributor to abrupt appearance, not a proven explanation for every puddle.
- The game's low-water preset (`update=false`, `detail=0`, `tessellation=false`) still draws `122478b22e69d9fa`, with `ReflectionMap`. Desktop run `artifacts/hud-elements-20260923-125035` confirms it. This is not a demonstrated replacement for the reflection pass; no water-quality override was added.
- Desktop diagnostic `artifacts/trace-20260923-130331-189` uses the Subaru cockpit, Baja `baja_iron/route_0`, serial rendering, reduced effects, no motion blur, 800x600, **water suppression off**, and a same-camera prepared-inner-scene replay. At frame 3000 both passes have 436 matching draw signatures, including one 39-index draw of `122478b22e69d9fa`. Only 409 of 480,000 pixels differ; mean absolute channel difference is 0.000672/255, maximum 8. Original camera/effects/graphics bytes were verified restored after exit.
- That capture looks down **dry road** near the cruise ship. It does not establish near-puddle correctness, positional stereo, reflection parallax or headset acceptance. Nevertheless, it no longer reproduces the earlier reason for suppressing this shader in the outer-scene prototype. Normal launcher VR sessions now omit the legacy suppression flag; the explicit developer `-SkipWater` diagnostic remains available. No water surface files or physics are changed.
- The all-directions visibility fix retains the original distance and LOD decisions. The game exposes object/tree `lod` values from 0.5 to 1.5 in `system/hardware_settings_options.xml`; current VR preparation leaves these preferences unchanged. Route override data also includes distance and object-size thresholds. Straight-line popping can therefore survive the directional visibility fix. No global culling disable, track-asset edit or unmeasured distance increase was applied.

## Stopped-puddle capture, 0.9.1

`artifacts/water-capture-20260923-133216` preserves requested frame 10440 from the user's Quest 3 run, looking left at a puddle in the Subaru Group N cockpit. Both eyes issue matching water batches using VS `b765f31b7f97f0c4` and PS `08192877abdf068a`. This is present water with inconsistent reflected content, not a missing second-eye water draw.

- Reflection SRV 2 is the same 400x300 `R16G16B16A16_FLOAT` resource in both eyes. Its captured bytes are identical. The 1600x1200 depth input differs between eyes.
- The camera's off-centre X projection term changes from -0.24251264 to +0.24251264. Eye positions are about 0.064 game units apart. The water's depth constants also update with the respective near-plane values (0.15 and 0.10); this alone is not evidence of a stale depth input.
- Shader disassembly shows VS output 8 carrying half-biased **current-eye clip coordinates**. The PS divides by W, flips Y, adds normal-map distortion, then samples ReflectionMap. Thus the same world location samples substantially different parts of a shared original-camera reflection. The horizontal projection terms alone account for approximately 24% of the reflection texture width. Regenerating depth alone cannot fix this alignment.
- Read-only executable inspection identifies the dedicated reflection renderer (vtable RVA `0xf262f8`), main-camera source at `+0x940`, reflected camera at `+0x8d0`, and clipping projection/combined matrix at `+0xd70` / `+0xdb0`. Preparation is RVA `0x2e22d0`; render/wait/queue cleanup is `0x2d1d40`, which calls the common outer renderer at return address `0x2d1daf`. The reflection draw at `0x2c9e90` explicitly restores those prepared matrices after ordinary camera setup. Applying the main-eye projection hook alone is therefore insufficient.

A dedicated per-eye reflection pass must rebuild its reflected camera, clipping projection and visibility lists from the same simulation state. It must also preserve queue ownership and render state. Do not patch only the reflection UVs and advertise that as correct positional stereo.

### Rejected direct reflection replay

An isolated desktop prototype called reflection preparation and rendering before each main-eye pass. The initial asymmetric comparison stopped at frame 993 (315/316 draws); an identical-camera comparison also failed at frame 1505 (322/320). Repeating with the existing VR ground-cover preservation did not resolve it.

The decisive trace is `artifacts/trace-20260923-135449-106`, frame 1411, 330/328 draws. `water-probe-failed-eye-1.txt` versus eye 2 identifies exactly two missing **reflection-pass** draws: 1416 indices with VS `32e0b25e0d372b3a` / PS `dc7f46680372089e`, and 168 indices with the same VS / PS `563d389c2ecd97c6`. The main-scene signatures match. Shader metadata identifies skinned vehicle materials (matrix palette, part states, damage/dust/ghost-car parameters). This rejects blindly rerunning the reflection preparer: vehicle reflection batches do not survive identically even with an unchanged camera.

The prototype was removed from runtime source; `artifacts/water-reflection-probe-trace.cpp` preserves it locally for investigation. Normal rendering remains unchanged. Next work is to locate the reflected vehicle queue ownership/frame stamps, retain those batches across the pair, and rebuild the reflected camera/clipping matrices without consuming them. The reflection draw copies prepared matrices over ordinary camera setup, so both concerns must be handled together. All diagnostic games exited and their wrapper restored the original proxy, camera/effects assets and graphics settings.

## Retained-list implementation, 2026-09-23

The new diagnostic keeps the reflection renderer's original prepared draw lists. The copied 0xf4-byte header originates on the outer renderer's stack; its linked batches remain owned by the reflection renderer. Capture and replay require the same frame, renderer, and source camera, with no pending preparation. No simulation update is invoked.

A guarded detour at RVA `0x2e2975` makes an explicit camera-only preparation exit through the original epilogue at `0x2e2a5d`. It balances the preceding FPU load and preserves registers/flags. Only the invoking render thread and identified reflection renderer take this branch. Ordinary preparation still follows its original trampoline. This retains reflected-camera/frustum/clipping calculations while avoiding queue construction and worker dispatch. The original prepared inner renderer then draws the reflection immediately before its corresponding main eye. The renderer's render serial is restored after each extra pass.

Evidence from isolated desktop tests:

- `trace-20260923-141228-940`: identical-camera full preparation still loses two reflected vehicle draws. Renderer-state snapshots and draw stacks are saved. Restoring the render serial alone (`trace-20260923-141735-246`) does not resolve it.
- `trace-20260923-141847-583`: queue diagnostics show different reflected-vehicle draw objects between passes. Rebuilding the full reflection twice is unsafe. The exact shared vehicle-cache mutation is not yet established.
- `trace-20260923-142008-264`: moving both full preparations before the main pair avoids unequal draw counts, but does not establish a safe retained scene. This ordering experiment is not used.
- `trace-20260923-142355-516`: camera-only preparation and retained lists complete **5,582 pairs** through frame 6000, with matching draw counts and restored main camera records. At frame 3000, both 200x150 RGBA16F reflection inputs are byte-identical for identical cameras. Main colour images still have small rendering differences (mean absolute channel difference 0.485/255, maximum 59); whole-image pixel identity is not claimed. An apparent doubled HUD glyph is also present in the earlier baseline and is not evidence of new queue corruption.
- `trace-20260923-142624-694`: **5,583 pairs** pass with 0.064 game-unit eye separation and projection shifts -0.24/+0.24. Both water batches remain present; reflection input bytes differ (9,070 bytes in the captured padded buffers), and camera constants carry the selected offsets. This proves a regenerated per-eye input, not headset alignment or reflection parallax acceptance.

The final thread/renderer-guarded hook repeats the asymmetric test in `trace-20260923-142929-422`: **5,584 matching pairs** through frame 6000. Native checks and 405 launcher checks pass, including default-off activation.

These tests temporarily used serial rendering, reduced effects, no motion blur, the isolated Subaru cockpit and an 800x600 window. Capture/readback stalls invalidate performance conclusions. The wrappers restore the original proxy, camera/effects files and graphics XML on normal exit.

### Quest 3 check and normal activation

The user tested local build 0.9.3 in the saved Ensenada Sprint / Subaru Group N practice session and reported: “water puddle reflections look good now.” Trace `logs/20260923-143255-393` records a visible OpenXR session, successful camera-only hook installation and per-eye reflection draws. The game exited normally and the session manager reported original files restored. No new stopped-puddle capture was made before exit, so this is user visual acceptance plus runtime receipts, not a new image-based comparison.

Following that check, normal launcher VR sessions enable `DIRT2VR_WATER_REFLECTIONS=1`; desktop launches clear inherited VR flags. No additional user setting is needed. Native standalone forwarding remains inactive without session activation. Developer desktop comparisons can opt in to the same flag; the old `--diagnostic-water` candidate switch is no longer needed. Normal play does not write capture files. Use `--diagnostic-capture` and `capture.request` only when gathering a requested pair.

Original reflection visibility lists are retained. Adequate reflected-object coverage when looking far away from the original camera is not yet established. Test other tracks, water shaders, night lighting and reflected vehicles before describing the whole reflection system as verified. A separate reflection-frustum expansion may still be needed. The captures used synchronous readbacks; no VR performance claim is made from those runs.

## Broader coverage still required

The original report combined close-range pop-in with mismatched eye reflections. The user now reports improved pop-in and accepts the tested reflection fix. Keep surfaces visible while extending coverage.

Requested stereo captures now also write `water-<frame>-eye-<1|2>-draw-<0..3>` files for up to four known water draws per eye. Text metadata identifies pixel shader, reflection slot and bound SRV dimensions/formats/resources. Binary buffers contain VS/PS slots 0â€“3. Simple single-mip, single-sample 2D reflection/depth inputs are read back with row pitch recorded; unsupported cube/array/multisample inputs are metadata only. These synchronous readbacks are diagnostic stalls, not performance measurements. The normal launch path never calls the water-input capture.

Capture both eyes at a stationary puddle, with the legacy filter disabled, using the existing opt-in `--diagnostic-capture` and `capture.request` workflow described in rendering notes. Compare the full water shader family, depth/reflection SRVs, camera constants, and first/second-eye draw counts at the same simulation instant. Separate an absent surface from a present surface sampling stale reflection or depth data. Fix or regenerate the identified per-eye inputs rather than hiding water or suppressing generic depth passes.

For scenery, compare the same stopped position and approach at the game's existing object/tree quality presets; record the exact disappearing asset and threshold before deciding whether the cause is LOD, object-size rejection or streaming. Any increased visibility distance needs frame-time and address-space measurements. The tested Ensenada reflection now has user headset acceptance; broad water/scenery coverage remains incomplete.
