# Water and straight-line scenery pop-in investigation

## Report and scope

The user reports water appearing at close range and different reflections between eyes on the default Baja / Ensenada Sprint benchmark route. Trackside scenery also pops while driving straight, independently of head direction. They explicitly want water retained while investigating. These observations have not yet been reproduced in a new stopped-car headset capture.

## Findings, 2026-09-23

- `Session.VrStartInfo` previously always enabled `DIRT2VR_SKIP_WATER`. The filter suppresses only pixel shaders `08192877abdf068a` and `122478b22e69d9fa`. Shader reflection identifies normal/reflection maps and wave parameters. Seven other water shader variants are not suppressed, including interactive ripple/depth/foam variants. Switching between suppressed and unsuppressed variants is a plausible contributor to abrupt appearance, not a proven explanation for every puddle.
- The game's low-water preset (`update=false`, `detail=0`, `tessellation=false`) still draws `122478b22e69d9fa`, with `ReflectionMap`. Desktop run `artifacts/hud-elements-20260923-125035` confirms it. This is not a demonstrated replacement for the reflection pass; no water-quality override was added.
- Desktop diagnostic `artifacts/trace-20260923-130331-189` uses the Subaru cockpit, Baja `baja_iron/route_0`, serial rendering, reduced effects, no motion blur, 800x600, **water suppression off**, and a same-camera prepared-inner-scene replay. At frame 3000 both passes have 436 matching draw signatures, including one 39-index draw of `122478b22e69d9fa`. Only 409 of 480,000 pixels differ; mean absolute channel difference is 0.000672/255, maximum 8. Original camera/effects/graphics bytes were verified restored after exit.
- That capture looks down **dry road** near the cruise ship. It does not establish near-puddle correctness, positional stereo, reflection parallax or headset acceptance. Nevertheless, it no longer reproduces the earlier reason for suppressing this shader in the outer-scene prototype. Normal launcher VR sessions now omit the legacy suppression flag; the explicit developer `-SkipWater` diagnostic remains available. No water surface files or physics are changed.
- The all-directions visibility fix retains the original distance and LOD decisions. The game exposes object/tree `lod` values from 0.5 to 1.5 in `system/hardware_settings_options.xml`; current VR preparation leaves these preferences unchanged. Route override data also includes distance and object-size thresholds. Straight-line popping can therefore survive the directional visibility fix. No global culling disable, track-asset edit or unmeasured distance increase was applied.

## Next proof required

Capture both eyes at a stationary puddle, with the legacy filter disabled, using the existing opt-in `--diagnostic-capture` and `capture.request` workflow described in rendering notes. Compare the full water shader family, depth/reflection SRVs, camera constants, and first/second-eye draw counts at the same simulation instant. Separate an absent surface from a present surface sampling stale reflection or depth data. Fix or regenerate the identified per-eye inputs rather than hiding water or suppressing generic depth passes.

For scenery, compare the same stopped position and approach at the game's existing object/tree quality presets; record the exact disappearing asset and threshold before deciding whether the cause is LOD, object-size rejection or streaming. Any increased visibility distance needs frame-time and address-space measurements. Headset testing was deferred; no claim of a complete water/scenery fix is made.
