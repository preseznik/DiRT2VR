# Changelog

Notable changes to DiRT2VR are recorded here, newest first. The project is an unreleased development prototype; dated entries are development milestones, not packaged releases. Headset checks refer to Quest 3 through SteamVR unless stated otherwise.

## Unreleased

### Added

- Visual Basic .NET Windows Forms launcher with a bundled runtime, SteamVR preflight, configurable keyboard shortcuts and Xbox / standard HID button bindings.
- In-place installer and ZIP packaging. Game copying is now a developer failure-testing workflow, not an end-user requirement. Packages contain mod files only.
- A background session manager shared by the GUI and quick-launch script, with original-file backups, durable recovery journals, proxy ownership checks and narrowly scoped file-worker elevation.
- Per-installation settings and logs under Local AppData; restoration preserves unrelated graphics-settings changes and refuses unexpected asset changes.
- Inactive proxy forwarding for normal desktop launches, without VR hooks, XR initialization or diagnostics.

### Validation

- The tester confirmed that the launcher detected the Xbox-compatible controller, captured bindings, launched the game, and supported Toggle VR and Recenter during play.
- Native tests, launcher transaction/input tests and installer install/upgrade/conflict/uninstall checks passed in isolated fixtures. Protected-folder, interruption and broader hardware acceptance remain incomplete.
- Following the reported power outage, inspection found the previous session already restored both original game assets with no pending recovery journal; this does not establish recovery from a power loss during an active write.

### Fixed

- Novigrad road rubble now renders in both eyes by preserving the matching ground-cover instance counts through the second eye. Rubble remains enabled; the tester confirmed the fix in the headset. Other ground-cover shader variants still need coverage.
- Paused cockpit menus and confirmation dialogs now use the virtual screen, preserving the selected VR mode for resume. The tester confirmed the Load Preset Controls dialog is readable and resuming returns to cockpit VR.

### Documentation

- Replaced developer-copy setup instructions with packaged in-place installation, launcher controls, recovery, upgrade and removal instructions.
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
