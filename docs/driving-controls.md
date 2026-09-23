# Driving controls

## Existing-profile loading

The demo startup route skips `press_start` and its profile load. `DirectMenus.LoadDrivingProfile` redirects only `c/skip_no_garage` through the existing `create_protected_data_context`, `load_profile_dataset_to_ep`, `enumerateprofiles` and `auto_load_profile` states before returning to `2FG` (the original direct-event preparation). It does not create, reset or save a profile. Missing/cancelled/failed loads return to the event with defaults. The normal and LAN flow files remain untouched.

These changes share the existing guarded `states.bin` / `flow.bin` preparation and recovery journal. Native profile protection APIs are retained. No encrypted-save parser, imported career, or alternate end-user Documents location is introduced.

Local evidence on 2026-09-23: `artifacts/controls-probe-20260923-193426` uses a disposable existing test profile, with the proven LAN test backend redirecting Documents before entry. An explicit diagnostic waypoint records `d2vr_profile_loaded` before the race. `save-hashes-before.json` and `save-hashes-after.json` cover the isolated save files. The earlier real-Documents test was not used as the profile-success proof; automatic review required moving further testing to an isolated profile.

The local original backend currently exits before rendering/control initialization; this is separate from the PC3 VR crash investigation. The controls diagnostic therefore temporarily uses the known LAN backend only in the isolated fixture, with hash-checked DLL restoration. It is not a proposal to replace PC3's genuine GFWL installation. PC3/Fanatec testing is deferred at the user's request.

## Optional launcher bindings

`DrivingControls.vb` stores opt-in, versioned settings in the installation's LocalAppData folder, and generates a bounded ActionMap XML file. Unassigned actions are not overridden. Each assigned action can have one keyboard and one controller binding; together these replace that action's loaded axes. This is deliberately not a separate career. The game may subsequently save the applied controls through its normal profile save.

`src/driving_controls.cpp` hooks the fingerprinted executable's Action parser at RVA `0xb095b0`. Only named configured actions receive an alternate reader. The game's own XML parser and normal input handling remain responsible for device lookup, calibration and driving. The temporary XML document remains alive for the game-held strings. Normal launches with no enabled overrides install no input hook. Desktop overrides activate a controls-only DX11 path, without render hooks, VR hotkeys or OpenXR. The exact GFWL compatibility guard also covers these hooks when the genuine supported DLL is present.

The XML constructor/parser (`0xaf3740` / `0xb01a80`), attributes (`0xaf39e0`), iterator (`0xaf82e0`) and Axis lookup (`0xafd490`) are guarded by the supported executable identity. A real-game test in `artifacts/controls-probe-20260923-193626` loaded two overrides and parsed the assigned `win_key_i` / `win_key_k` controls, including during profile loading. This proves the application route; it does not prove physical wheel response.

## Capture and validation

`tools/driving-input/input.cpp` is a separate x64 static-runtime DLL used by the launcher capture dialog. It enumerates DirectInput product/instance names, reads `DIJOYSTATE2` non-exclusively in the background and uses XInput for Xbox devices. It never injects keys, installs a driver, grabs devices exclusively, or changes game Raw Input registrations. The helper is loaded by absolute path only after checking its package-manifest hash.

Axis capture records movement relative to the released/centered baseline. Disconnection cancels capture; reconnecting does not assign an input. Separate wheels, pedals and button shifters are supported by the model. Native game device lookup uses product names, so identical product names remain ambiguous. POV/hat capture is not included. Hardware acceptance is still required for Fanatec CSL and Logitech devices.

Build the helper with `tools/driving-input/build.cmd` before launcher tests. Packaging includes it as `DiRT2VR/payload/driving_input.dll`. Launcher tests cover settings/XML, invalid and duplicate assignments, axis direction, held/disconnected input, Xbox trigger separation, real helper loading/polling, desktop/VR activation and direct-flow recovery. A rendered editor artifact is produced without computer-use automation. The native parser trace is explicit diagnostic opt-in; normal logging remains off by default.

## Final candidate checks

The static-runtime Release DLL was checked in `artifacts/controls-probe-20260923-200248` with the production `DirectMenus` assets. After profile loading, the live event configuration remained Baja / `baja_iron` / `route_0` / Subaru STI. The native parser accepted the configured actions, all isolated save hashes were unchanged, and the original proxy, LAN backend and flow/state files were restored. No physical wheel or headset acceptance is implied.

The release-source launcher suite passed 492 checks in each Windows theme; the native suite passed all 15 tests. The genuine-GFWL integrity regression also passed. The optional capture helper loaded successfully, but no controller was attached during that local check; axis/button cases used synthetic samples.
