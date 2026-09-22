# LAN baseline prototype

Implementation status, 2026-09-22: first-stage desktop lab kit `0.2.0`. Launcher lobby/discovery and automatic host/join are not implemented. The two-PC native race is the acceptance gate before that work, as described in [the multiplayer plan](lan-multiplayer-plan.md).

## Implemented

- Pinned XLLN v1.6.2.1 at `0b4ca99727566f835cc5aaca6b0a9dc4aced28b9`, built as x86 with the static MSVC runtime and Opus dependency.
- A small recorded upstream patch requires `DIRT2VR_LAN_CONFIG` and calls our profile integration at XLLN's post-import/game-entry hook. It disables loading other XLLN modules in this lab build. No multiple-instance patch is used.
- The native integration verifies the supported executable hash and changes only the executable's `SHGetFolderPathA` import for CSIDL_PERSONAL. Other folder requests and the system API remain unchanged. The redirect persists for process lifetime. Missing/invalid isolation stops startup; this DLL is not a general-purpose replacement for the existing offline shim.
- A PowerShell 5-compatible desktop runner sets a private XLLN configuration and Documents root, uses a random persisted name per fresh kit, leaves Microsoft online sign-in and voice disabled, and disables XLLN's default 60 Hz render limiter. Native LAN APIs remain enabled. No internet broadcast endpoint or optional relay/port-forwarding module is configured.
- A durable, hash-checked transaction backs up and temporarily replaces only root `xlive.dll`. It restores exact original bytes or original absence; external changes block recovery. The original `xlive.ini`, game binaries and current career are not modified. The runner shares `Global\DiRT2VR.Session` with the main launcher and refuses concurrent games. Interrupted lab sessions require `Start-LanTest.cmd -RecoverOnly` before ordinary play.
- `tools/package.ps1 -LanLab` routes to the separate lab ZIP while using the same version reservation policy. This does not publish or replace the normal launcher package. The first feature build reserved `0.2.0` through `-VersionBump Minor`; subsequent kit builds use patch increments.

## Build

Requires the usual Visual Studio x86 tools, CMake/Ninja and 7-Zip. XLLN's release CI artifacts had expired, so the DLL was built from pinned source.

1. Clone `.deps/xlivelessness` at tag `v1.6.2.1`, confirming the commit above. `Package-LanTest.ps1` can clone it and apply `tools/lan/xlln-integration.patch`; it also accepts that exact patch already applied.
2. Download Microsoft's June 2010 SDK archive from `https://download.microsoft.com/download/A/E/7/AE743F1F-632B-4809-87A9-AA1BB3458E31/DXSDK_Jun10.exe` to `.deps/dxsdk-jun10/DXSDK_Jun10.exe` and extract only `DXSDK/Include` and `DXSDK/Lib` into `.deps/dxsdk-jun10`. Do not run the SDK installer or install anything into Windows system directories.
3. Run `tools/package.ps1 -LanLab` (use the explicit minor flag only when first introducing a new feature). The source declares its Opus, RapidXML and RapidJSON dependencies. The wrapper supplies only legacy D3DX headers so old DXGI/D2D definitions do not shadow the modern Windows SDK. A forced standard C library include supplies upstream's missing malloc/free declarations; the static link excludes the conflicting legacy MSVCRT default library.

`tools/lan/build.cmd` builds and tests without reserving another distribution version during the same development cycle. The native library is emitted in `.deps/xlivelessness/bin`; the isolated test harness is under `build/lan`. The normal VR proxy is not rebuilt or changed by this target.

The kit includes XLLN and dependency notices plus modified upstream source and the DiRT2VR additions. In the initial kit's source snapshot, reconstruct the normal repository layout to rebuild: upstream `source/xlivelessness`, `cmake` and top-level CMake/README/LICENSE belong under `.deps/xlivelessness`; `source/DiRT2VR-lan` contains `tools/lan`, plus `common.cpp/.h` for `src` and `lan_profile_test.cpp` for `tests`. Dependencies remain available at the pinned source references. This source snapshot contains no SDK/game binaries.

## Verification so far

- Native import test passes: redirected Documents, unchanged LocalAppData and system Documents API, rejected invalid paths and duplicate installation. The test deliberately reloads the import across calls; an optimizer initially cached the pre-hook pointer inside the test function. The real hook runs before game code and before such caches could be created.
- Eight transaction checks pass: payload installation, exact restoration, journal-before-write interruption, external-edit conflict, repeated preparation, original absence and corrupt payload rejection.
- DLL imports contain Windows system libraries only, with no separate MSVC runtime or DirectX helper DLL requirement.
- First packaged startup attempt failed before any game-file modification because a PowerShell 7 environment supplied an incompatible PSModulePath to Windows PowerShell. Retrying with its default module environment succeeded. Normal double-click uses Windows PowerShell directly; automation should clear the inherited PSModulePath when crossing PowerShell editions.
- Live local game startup produced the mandatory `profile-ready.txt` receipt and new settings/save files solely under the kit's private Documents tree. All 61 pre-existing career files remained byte-identical during and after the test. After exit, `xlive.dll` matched its original SHA256 and the pending journal was removed.
- The local XLLN game process opened UDP 39000 and 39001. This is an observed startup port inventory, not a completed join/race traffic capture.
- The tester confirmed PC1 hosting, PC2 joining, both loading the same race, and both driving and seeing each other. They did not finish the race. Contact/collision behavior, results, repeat races, profile persistence across relaunch, Windows firewall behavior on the second machine and full failure recovery in live game sessions remain acceptance items.

First kit: `artifacts/lan-packages/0.2.0-20260922-085853/DiRT2VR-0.2.0-LAN-Test.zip`. It contains no test user's profile. Local session uses the extracted kit alongside the existing isolated `artifacts/game` through `-GameRoot`; the second PC should extract the original ZIP into its game directory as directed by the included README.

## Optional introduction bypass

`Lan-Settings.cmd` opens a small Windows checkbox dialog. Settings version 1 lives at `user/lan-settings.json`; `SkipIntroduction` defaults false and is applied on the next lab launch. This is a lab-kit option, not yet part of the main launcher. The tester confirmed the fresh-profile path reaches LAN without the movie or forced introductory race.

Read-only decoding of `system/states.bin` and `system/flow.bin` established two separate paths:

- `StateVideoPostStart` is used exactly once, for the unskippable `first_race_video`. Its registration at RVA `0x284115` loads factory RVA `0x276050`. When opted in, the factory operand instead points to the native `StateWayPoint` factory at RVA `0x0db440`, whose delay defaults to zero. The original video transition's surrounding fades and subsequent `rv_new_profile` route stay in place.
- `StateCareerIntroDecision` selects `intro_not_shown` at `0x225e75` when the career flag is absent. Changing the conditional branch at `0x225e73` from `75 14` to `EB 14` keeps the normal `intro_shown` result and avoids the map speech/London tutorial path. It does not write the saved flag or award race results.

Both process-memory edits happen at the XLLN pre-entry hook, before state-factory registration and state creation. All original bytes/pointers are checked before either write, and original page protections are restored. The caller first verifies the executable fingerprint. The runner also rejects modified flow/states files when bypass is enabled: SHA256 `D261EA3394627188AF47DBF8F36F22FDF3B99E70835921B8590D739220DC601F` and `62606A2C6A09F8E9E7AF172141418812E95337672BB31102849C59FD6676D3AD`, respectively. No game asset or executable is changed on disk. A guard failure exits before game entry so the runner can restore the shim.

Native tests cover relocated addresses, failed-site rejection without partial writes, duplicate rejection and restored page protections. Settings tests cover default off, persisted on/off and malformed/version-mismatched preferences, including Windows PowerShell 5.1. Build `0.3.0` used a new isolated profile with bypass enabled; the native readiness receipt confirmed activation, and the tester confirmed reaching LAN without the movie or forced race. Existing-profile behavior and disabling the option after a skipped introduction remain follow-up checks. The setting cannot undo progress already saved by the game.

After the intro test exited, the original shim hash matched again, the pending journal was absent, and all 61 existing career files still matched the baseline. Final kit `0.3.1` is at `artifacts/lan-packages/0.3.1-20260922-094131/DiRT2VR-0.3.1-LAN-Test.zip`; the DLL matches the tested build, all root manifest hashes pass, and the ZIP includes no user profile/settings. Open `Lan-Settings.cmd` before launching to opt in. To retain an earlier kit's isolated profile on another PC, close the game, recover its session if necessary, and keep that PC's existing `user` folder while replacing the kit files. Never transfer the used profile to another player.
