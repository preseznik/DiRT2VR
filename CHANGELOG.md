# Changelog

Notable changes to DiRT2VR are recorded here, newest first. The project is an unreleased development prototype; dated entries are development milestones, not packaged releases. Headset checks refer to Quest 3 through SteamVR unless stated otherwise.

## Unreleased

### Documentation

- Replaced the root README with end-user setup, play instructions, controls, limitations and troubleshooting.
- Moved the previous technical README to `docs/development.md` and added explicit interrupted-run recovery instructions.
- Added this changelog and repository guidance to maintain it alongside future changes.

## 2026-09-21 — Development prototype

### Added

- Geometry stereo cockpit rendering with headset-driven eye poses, projections and turning/leaning support.
- `Start-DiRT2VR.cmd` for interactive play using a separate game copy, with temporary settings restored on normal exit.
- A stationary virtual screen for menus and videos, F9 screen/cockpit switching and F10 recentering.
- A reduced-effects Subaru STI camera setup for the current test baseline.
- Frame timing, stereo integrity and memory diagnostics for investigating performance and compatibility.

### Changed

- Disabled expensive capture diagnostics by default during interactive play to reduce diagnostic stalls.
- Enabled scenery visibility in every direction for eligible VR cockpit views. The tester confirmed scenery remained visible when looking left, right and behind. The original visibility behavior is available with `-WideVisibility:$false` for comparisons.

### Fixed

- Recognized trailer/exterior cameras now stay on the virtual screen when cockpit VR is requested.
- F10 recentering no longer freezes the game until a second press.
- Headlights no longer follow the headset view in the tested Subaru Impreza STI Group N night event at Battersea Bridge.
- Trackside scenery, vegetation and buildings no longer disappear on side/rear head turns in the tested scene.

### Known limitations

- No packaged installer or PSVR2 hardware validation yet.
- Broader car/stage coverage, occasional hitching, HUD/mirrors, complete menu/replay transitions, controller bindings for VR shortcuts and headset reconnection remain unfinished.
- Several visual effects remain reduced or disabled. Current tests do not establish full-race timing integrity or a guaranteed headset frame rate.

## 2026-09-16 — Initial investigation

### Added

- Guarded DX11 instrumentation for the supported 32-bit DiRT 2 executable.
- A standalone OpenXR stereo cube diagnostic, seen in Quest 3 through SteamVR. This validated headset presentation, not game VR.
- Scene replay diagnostics that identified missing work when rendering the entire outer scene twice, motivating the later prepared-scene approach.
