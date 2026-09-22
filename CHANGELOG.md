# Changelog

Notable changes to DiRT2VR are recorded here, newest first. Versioned releases remain experimental; older dated entries are development milestones. Headset checks refer to Quest 3 through SteamVR unless stated otherwise.

## Unreleased

- Added a separate `0.2.0` desktop LAN test kit using pinned XLiveLessNess, a fresh process-isolated profile, opt-in launch and journaled restoration of `xlive.dll`. Native profile isolation and eight recovery checks pass; local startup created only isolated save/settings files and left existing career files unchanged. Two-PC race acceptance is pending. Launcher multiplayer lobbies, LAN browser and automatic race startup are not yet implemented. See `docs/lan-lab.md`.

- Added a LAN multiplayer feasibility plan covering XLiveLessNess reuse, launcher host/join and discovery, native race-start integration, profile/DLL recovery and staged acceptance. This is a researched proposal; multiplayer has not been implemented or locally validated.

- Adopt plain `major.minor.patch` versions starting from `0.1.0`, without alpha suffixes. Packaging increments patch for each distribution build; entirely new features increment minor and reset patch, and major changes require an explicit user request. Existing alpha release history is preserved.

## 0.1.0-alpha.5 — 2026-09-22

- Numeric launcher settings now use sliders with visible values and one-step arrow-key adjustment, including Graphics, AI opponents and circuit laps. Renamed the Game menus launch mode to **Normal Launch**; existing preferences remain compatible.
- Race adds **Same as driver**, **Mixed** (all installed classes), and **Same class** opponent car choices. Mixed/class grids retain the chosen player car and randomize other installed models, repeating small pools as needed. Solo practice stays solo. The tester confirmed the mixed-grid desktop race works correctly. Same-class grids pass configuration tests; same-class gameplay and crowded-grid VR acceptance remain pending.

- Diagnostic logging toggle in Settings, off by default for new and existing preferences. Disables preflight log files, native trace/CSV/binary output and captures while preserving recovery journals and a single latest headset refresh summary. Existing logs are not deleted. Native logging-on/off and launcher preference tests pass; desktop tests created or changed zero diagnostic files. VR gameplay with logging disabled still needs a headset check.
- Launcher minimizes after starting desktop or VR play and does not restore itself during session-status updates. A focus trace identified DiRT 2 replacing its startup window and leaving another app active. Added a single startup focus handoff to the replacement game window; switching away from the existing game window with new input or a 30-second timeout cancels it. The tester confirmed an acceptable brief focus drop followed by immediate return on desktop. VR focus acceptance remains pending.

## 0.1.0-alpha.3 — 2026-09-21

First public experimental package, including the development changes below.

### Added

- Automatic, nonblocking update check when the launcher window opens, with a clickable **New version available** indicator beside Help / About. Alpha builds include experimental releases; stable builds check stable releases only. Offline checks stay quiet, and downloads/installation remain user-initiated. Package version is `0.1.0-alpha.3`.
- Help / About (`?` or F1) with installed version, build revision/date, Bohloney developer credit and documentation/issues/release links. Launcher package version is now `0.1.0-alpha.2`.
- User-initiated GitHub Release checks with stable/experimental selection, verified installer downloads and an assisted setup handoff for the current game folder. Existing recovery and installer safeguards apply; preferences survive. No silent background updates. ZIP installations can switch to installer-managed updates. Public release-to-release installation remains pending until packaged releases are published.
- Fixed the initial lap override targeting an unused route setup path. A guarded hook now supplies the selected count to the actual demo route descriptor for both Race and Practice. Solo/eight-car desktop diagnostics confirmed three laps in the copied descriptor; the tester confirmed the three-lap HUD and continuation into lap 2 at Baja – Ensenada Sprint. Complete multi-lap finishes and VR acceptance remain pending.
- Configurable 1–20 circuit laps for Direct practice and Race. Point-to-point stages remain one run, identified from the installed game's route metadata. Original executable and database files stay untouched.
- Experimental Race mode beside Direct practice, with 1–7 AI opponents using the selected car, for desktop and VR launch. Solo practice remains unchanged. Retains the repeating direct-start session and Continue-only pause menu; difficulty and normal results flow are not exposed. Desktop player control and seven AI opponents were tester-confirmed at Baja with the Subaru; crowded-grid VR acceptance remains pending.
- Regular desktop Launch beside Launch VR, with both buttons on the left and utility buttons aligned right. Both launch modes support menus and Direct practice; desktop play skips SteamVR and VR graphics/camera overrides. Desktop practice requires DX11 and enables only the human-control change, with a config-only recovery journal. Added `--launch --desktop --no-ui` while keeping the existing quick-launch script in VR mode.
- Fixed direct practice falling back to Croatia: the installed launch path shortened the long generated config argument. The session now uses the verified short `DiRT2VR/p.xml` path, refuses existing-file conflicts and retains recovery of old version 2 journals. Desktop engine reads matched selected Baja and London routes/cars; headset acceptance remains pending.
- Experimental Direct practice on the Launcher tab with event-category, installed-track and car selectors. Uses the game's direct-start path with a guarded, memory-only change restoring human control. Desktop driving, pause/resume and finishing were tester-confirmed at Baja; the session loops afterward and the pause menu only has Continue. Packaged VR and other cars remain unverified.
- Direct-practice preparation journals the selected car camera and a short generated race configuration. Recovery supports existing Subaru journals, preserves external edits and removes only the recorded configuration.
- Graphics tab with scene render resolution, headset texture scale, optional field-of-view cropping, mirror overrides and restoration of defaults. Existing graphics defaults remain unchanged; nondefault values and cropping await headset acceptance.
- Optional headset refresh-rate reporting during preflight; Graphics displays the last reported rate without changing SteamVR/headset settings.
- New DiRT 2 VR rally/headset icon for the launcher executable, window, shortcuts and installer.
- Launcher light/dark appearance follows the Windows app theme at startup on Windows 11. Reopen after a theme change; contrast themes retain the system appearance.
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

### Changed

- Fixed a background-session startup crash that could make Launch VR appear to do nothing: Windows Forms text rendering is now configured before theme initialization can create a hidden window.
- Moved game location, runtime selection and setup instructions into Settings. Launcher now holds launch mode and practice selection; saved selections also apply to quick launch.
- Fixed light areas left by native themed TabPages in dark mode and explicitly themed practice dropdown text. Offscreen light/dark rendering checks cover all four tabs.
- Settings version 3 preserves existing graphics and bindings while adding launch mode, track and car preferences. Normal game-menu launch remains the default.
- Split the launcher into Launch, Graphics and Controls tabs. Controls groups each action with its keyboard shortcut and controller/wheel assignments, retaining multiple-device bindings and disconnected assignments.
- Settings migrate from version 1 to version 2 with existing bindings intact. Graphics changes share the existing temporary-file journal and quick-launch lifecycle.

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
