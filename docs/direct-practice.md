# Direct practice investigation and implementation

## Track-selection fix — 2026-09-21

The first packaged run selected Baja / Ensenada Sprint / Subaru STI but loaded Croatia. Investigation resumed with desktop-only tests. Reading the supported game's parsed configuration at module RVA `0x1051130` showed the fallback `croatia/croatia_rally/route_0`, and the actual child command line ended at `-demo DiRT2VR/backup`, instead of the complete generated subfolder path. Replacing the generated XML contents with the earlier working XML did not change that result.

Passing the short path `DiRT2VR/p.xml` worked with the original generated XML (no declaration required). Separate runs read `baja/baja_iron/route_0/sti` and `london/battersea/route_1/mex` in the game itself. The config contains country, track and route at offsets `+0x8c`, `+0xac`, `+0xcc`, first car at `+0xec` and track count at `+0xef0`. The game falls back to Croatia at RVA `0x40d10e` when no tracks were parsed. This was an argument delivery failure, not a mislabeled catalog entry.

Evidence: `artifacts/practice-parser-20260921-204758-generated`, `204915-original`, `205101-short` and `205241-short` (each has the full `practice-parser-20260921-` prefix). The two short-path runs used different track/route/car combinations. Direct invocation of `dirt2_game.exe` without its wrapper did not reach config initialization; production continues to use `dirt2.exe`.

Version 3 asset journals use the fixed short file and refuse an existing file before modifying assets. Only one session owns it, protected by the existing session mutex and pending journal. Version 2 journals still recover their old GUID-based config location; version 1 remains Subaru-only. Both light and dark launcher runs passed 117 checks, including legacy config recovery and fixed-file collision preservation. Headset testing is still pending.

The self-contained launcher's actual file worker was then used to prepare STI/track 127, followed by a desktop game run reading that generated file. `artifacts/practice-parser-20260921-205723-prepared/parsed.json` records the expected Baja/Ensenada Sprint/route 0/STI selection. Recovery completed with exact original camera/effects hashes, no pending journal and no remaining `p.xml`. Diagnostic runs required termination of their own test process when normal window-close requests were ignored; no unrelated processes were stopped. A post-fix human driving/headset check remains separate from this parsed-selection evidence.

The Launcher tab exposes the built-in `-demo <config.xml>` path as **Direct practice**. It is a solo, repeating driving session, not a replacement for career or the full race setup frontend. Event is a discipline filter for tracks; it does not configure career rules, opponents, difficulty or lap counts. Normal menu launch remains the default.

## Evidence on the supported 1.1 executable

- The original `-demo vr_benchmark.xml` test loaded Baja Ensenada Sprint with the Subaru STI but drove automatically.
- Adding a `Controller` event with `Switch type="human"` to the spectator start sequence did not fix control. That temporary XML edit was restored; it is not part of the implementation.
- Participant creation already identifies the local vehicle as human (`vehicle + 0x5588 == 0`). The controller update at RVA `0x72a580` overrides that selection on every update when the direct-start global at VA `0x14511b0` is set.
- At RVA `0x72a5a1`, the conditional branch decides whether to set `controller + 0x3a` (force AI). Changing its opcode from `74` to `EB` skips the forced-AI assignment and keeps normal human/AI selection in this controller. Frontend skip state and other vehicles' roles are unchanged.
- The diagnostic recorded `role=0 name=human switch=2 force_ai=0`. The tester confirmed player driving, pause/resume and completing the route. Finishing restarted the route, and pause offered only Continue.

Local evidence is in `artifacts/direct-controller-20260921-184312/trace.log`. The diagnostic proxy was restored with an original-hash check; its receipt is `restored.json` in that directory. These artifacts are ignored and not distributed.

## Activation and failure behavior

Only practice launches set `DIRT2VR_DIRECT_PRACTICE=1`. The proxy also requires existing session activation and the supported executable hash. It verifies the original update prologue and branch bytes before changing one byte in process memory, restores page protection, and flushes the instruction cache. A guard/protection failure reports an error and exits the game process so the session manager can recover prepared assets. No executable is modified on disk. Controller introspection hooks from the diagnostic are not shipped.

The ordinary menu launch does not set the practice flag. An inactive proxy continues to forward system D3D11 without any practice or VR work.

## Selection, preparation and recovery

`launcher/assets/race-catalog.json` is an embedded allowlist of 41 installed route IDs and 43 car codes, names and discipline mappings for the supported build. It contains identifying facts only, not the game database, localization tables, assets or saves. Entries were checked against the installed `database/database.bin` and English names in `language/language_eng.lng`, using the pinned EGO schema/format as a read-only reference. The UI further filters by installed route directories and camera files. It allows cross-discipline car choices, as in the accepted Subaru/Baja test; unusual combinations remain experimental.

Settings version 3 adds `LaunchMode` (`menus`/`practice`), `TrackId` and `CarCode`. Versions 1 and 2 migrate with their existing controls and graphics preserved. Selected values must resolve through the embedded allowlist; worker arguments cannot supply arbitrary asset or config paths.

The existing fixed-purpose file worker accepts selected IDs for preparation. Version 3 asset journals record the selected car, originals and applied hashes. Version 1 journals always map to Subaru STI, irrespective of new preferences or fields. Preparation uses the existing generic camera patch and fails before asset modification when the car's required camera fields are absent.

The worker generates `DiRT2VR/p.xml`, the short ASCII relative path verified through the installed wrapper. Do not replace it with a longer GUID-based argument without a real-game regression check. Its ownership/hash is journaled before creation. The file contains one track and one car, is passed through `ProcessStartInfo.ArgumentList`, and is deleted on recovery only if it matches its recorded hash. Unexpected changes preserve the file and pending journal. The same session manager handles GUI and quick launch, graphics overrides, inputs, exit detection and restoration.

## Validation and remaining work

- Eight native CTests pass, including inactive proxy behavior.
- Launcher tests cover light/dark tab backgrounds, menu/practice selection, event filtering, saved selections, version 1/2 migration, unknown IDs, missing routes, selected-car restoration, legacy journals, partial preparation and changed-config conflicts. Offscreen rendering avoids desktop automation.
- Every listed camera file passes read-only camera transformation and all listed route folders exist locally. These checks do not prove visual correctness, engine compatibility for every combination, or headset acceptance.
- Desktop driving, pause/resume and completion are tester-confirmed only for the Subaru STI at Baja. Full packaged GUI/quick-launch VR checks, other cars, wheels and all routes remain acceptance work.
- A future normal race/results frontend with Restart/Quit requires further game-state integration. Do not relabel the current repeating demo session as a full event launcher or imply normal career/progression behavior.
