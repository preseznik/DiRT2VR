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

### Revised capture after 0.11.0 feedback

The reported saved configuration contained `biDirectionalUpper` for both steering directions and `uniDirectionalNegative` for the Xbox accelerator. Sampling a displaced baseline and then its return could create these assignments. `DrivingCapture` now learns a stable rest for 600 ms, accepts deliberate axis movement held for 100 ms (buttons use a press edge), then requires 160 ms at rest before completion. Returning to an unknown DirectInput axis's measured baseline is the release condition; an endpoint resting at +1 is not inherently engaged. Existing held buttons are ignored until released and pressed again. Disconnection and focus loss discard incomplete captures. An unrelated held switch cannot prevent completion.

Xbox sticks must be centered and triggers released before learning completes. Trigger release cannot produce a negative calibration. The stock Xbox preset is checked against the installed game's `actionmap/Windows XInput.xml`, including its 20% stick dead zone. The capture preview displays travel relative to rest, not raw axis magnitude. The wizard accepts any connected device by default, supports keyboard mode and optional device/axis/button filters, and only returns staged assignments after review/Apply. Cancel never writes settings. Save and launch reject the known conflicting steering halves and inverted Xbox trigger configuration, while Load still allows opening and repairing old settings.

Input references: Microsoft's [XInput guidance](https://learn.microsoft.com/en-us/windows/win32/xinput/getting-started-with-xinput) explains resting thumbstick drift and application-defined dead zones. [DirectInput/XUSB guidance](https://learn.microsoft.com/en-us/windows/win32/xinput/directinput-and-xusb-devices) explains why Xbox triggers should be read separately through XInput. DirectInput axes do not share Xbox's known neutral position; the rest/move/release state machine is our application-level policy, not a claim that Windows calibrates the physical hardware for us.

`tools/driving-input/input.cpp` is a separate x64 static-runtime DLL used by the launcher capture dialog. It enumerates DirectInput product/instance names, reads `DIJOYSTATE2` non-exclusively in the background and uses XInput for Xbox devices. It never injects keys, installs a driver, grabs devices exclusively, or changes game Raw Input registrations. The helper is loaded by absolute path only after checking its package-manifest hash.

Axis capture records movement relative to the released/centered baseline. Disconnection cancels capture; reconnecting does not assign an input. Separate wheels, pedals and button shifters are supported by the model. Native game device lookup uses product names, so identical product names remain ambiguous. POV/hat capture is not included. Hardware acceptance is still required for Fanatec CSL and Logitech devices.

Build the helper with `tools/driving-input/build.cmd` before launcher tests. Packaging includes it as `DiRT2VR/payload/driving_input.dll`. Launcher tests cover settings/XML, invalid and duplicate assignments, axis direction, held/disconnected input, Xbox trigger separation, real helper loading/polling, desktop/VR activation and direct-flow recovery. A rendered editor artifact is produced without computer-use automation. The native parser trace is explicit diagnostic opt-in; normal logging remains off by default.

## Final candidate checks

The static-runtime Release DLL was checked in `artifacts/controls-probe-20260923-200248` with the production `DirectMenus` assets. After profile loading, the live event configuration remained Baja / `baja_iron` / `route_0` / Subaru STI. The native parser accepted the configured actions, all isolated save hashes were unchanged, and the original proxy, LAN backend and flow/state files were restored. No physical wheel or headset acceptance is implied.

The release-source launcher suite passed 492 checks in each Windows theme; the native suite passed all 15 tests. The genuine-GFWL integrity regression also passed. The optional capture helper loaded successfully, but no controller was attached during that local check; axis/button cases used synthetic samples.

Validation of the revised capture: 528 release-source launcher checks passed in artifacts/launcher-tests-20260923-210009 (light theme); the preceding dark run passed 526 checks before adding the two final button-filter tests. Offscreen editor/wizard renders were inspected. The live XInput device sample marshalled correctly, but in-game steering and the physical Fanatec handbrake require user acceptance.

