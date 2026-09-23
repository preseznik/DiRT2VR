# Changelog

Notable changes to DiRT2VR are recorded here, newest first. Versioned releases remain experimental; older dated entries are development milestones. Headset checks refer to Quest 3 through SteamVR unless stated otherwise.

## Unreleased

- Added a **HUD distance** slider on Graphics: 1–20 metres in 0.5 m steps, default 4 m. It preserves apparent HUD size, as requested, and supports fixed and follow-view placement. Save and relaunch VR to apply; graphics defaults restore 4 m. Launcher, camera-math and OpenXR submission tests cover the setting. The user's 20 m check did not show an obvious distance change; added a one-time placement receipt when diagnostic logging is enabled. Perceptual headset acceptance remains unresolved.
- The user confirms water no longer appears to pop in with the partial filter removed, but reflections still differ between eyes. Requested input captures reproduce a shared original-camera reflection sampled using different eye projections. A separate per-eye reflection prototype lost vehicle batches in the second view even with identical cameras, so it was removed; no reflection fix is claimed. Normal play produces no capture files. Evidence and next steps are in `docs/water-visibility.md`.

- Added five visibility toggles below **HUD follows view** for the cockpit HUD's gauges, lap/time, race position, route map and stage progress areas. All start enabled and reset with graphics defaults. These mask the standard HUD layout, including any other overlay sharing a hidden area; menus, virtual-screen and desktop HUDs are unchanged. Desktop captures and all 32 GPU-tested combinations pass; headset and alternative-layout checks remain pending.
- Stopped enabling the legacy two-shader water filter on normal VR launches. It hid some water variants while leaving others visible, a candidate cause of distance-dependent appearance. An unfiltered same-camera inner-scene replay now has matching draw lists. Water remains visible as requested; this is not a verified fix for stereo puddle reflections or straight-line scenery pop-in. See `docs/water-visibility.md` for evidence and remaining work.

- Added a transparent cockpit HUD layer about four metres ahead, fixed relative to the car by default. Graphics → **HUD follows view** optionally follows head movement; Recenter repositions the fixed layer. HUD drawing is captured once per frame and composed for both eyes, separately from the scene. Desktop captures verify separation of the Baja lap/time, position, route map and speedometer from scenery; GPU, OpenXR lifecycle and launcher tests cover the implementation. Headset readability, placement, follow mode and broader event coverage remain unverified; the user deferred headset testing.

- Fixed the identified LAN race-loading disconnect caused by startup logo skipping. Live 0.7.0 captures identified modified `system\states.bin` as the failed validation record; disabling logo skipping on both PCs allowed both players to drive, finish Battersea and reach results. LAN launches now preserve that file even when the preference is enabled. HOST/JOIN preflight rejects altered definitions, and preparation guards prevent combining LAN and movie edits. Single-player logo skipping and LAN's separate introduction skip remain available. All 378 launcher checks pass; LAN VR remains unverified.

- HOST and JOIN now ask for Desktop or VR on every launch, with Cancel available. LAN VR uses the existing SteamVR preflight, graphics/controls settings and combined recovery of VR assets, graphics, startup movies and the LAN DLL. The shared career and HOST/JOIN endpoint are preserved. Launcher tests pass; multiplayer headset testing is deferred, and the race-loading disconnect remains unresolved.

- The 0.6.10 host diagnostic captured the game's session teardown path, but the two-PC loading disconnect persists. The next investigation targets the preceding session-state change; further gameplay testing is deferred.

- Two-PC 0.6.9 testing still disconnects during Battersea loading: PC1 first, PC2 later. Extended opt-in host diagnostics with bounded game-code stack candidates and session API errors to investigate the game's teardown decision. All six native tests pass; this diagnostic change does not claim to fix multiplayer behavior.

- Corrected generated LAN peer addresses so different PCs have distinct, nonzero machine identities. The actual-DLL regression fails on 0.6.8 and passes with the correction; all six native checks pass. Both PCs must update for the next test. Whether this resolves the Battersea race-loading disconnect still requires two-PC confirmation.

- Extended opt-in LAN diagnostics with game-facing send/receive sizes and checksums, plus socket-close call sites. Paired Battersea traces show the host closing its sockets while transport acknowledgements are still flowing; the later client timeout is a consequence. The cause of the host's closure remains under investigation.

- Added opt-in LAN network traces through Settings → Enable diagnostic logging. Each session keeps at most two 4 MiB files containing socket, packet-header, acknowledgement and timeout metadata, without packet payloads. Logging remains off by default. Native logging/transport and launcher tests pass. The two-PC follow-up reached race loading but disconnected at Battersea Rallycross before the grid; that failure remains under investigation.

- Fixed a LAN transport keepalive defect that could time out an otherwise reachable idle peer. Receiving a probe now schedules an acknowledgement through the existing protocol. A regression against the actual DLL fails on the released build and passes with the fix, including ordered data, selective acknowledgements, bounded replies and disconnected-peer timeout. Two-PC lobby/race confirmation is pending.

- Client startup investigation: the supplied alternate wrapper and its two companions successfully reached regular menus in an authorized separate local test with the unchanged LAN shim. Both client variants are retained. PC2's ordinal-43 failure remains unresolved; a system GFWL identity-library dependency is now a concrete lead. Expanded the read-only report to capture companion files, compatibility settings and mapped DLLs even before normal loader initialization. No compatibility fix or new release is claimed.

- PC2's ordinal-43 report confirms correct game/shim files and an active recovery journal, with a different startup wrapper from the working PC. A direct-start candidate reached LAN initialization but failed the local game check; it was reverted and package 0.6.5 was not released. The client startup issue remains under investigation.

- Added a read-only LAN startup report tool for ordinal/DLL-loading failures. It records running game/launcher paths, relevant loaded DLLs, file hashes and recovery state without reading saves or changing the installation. The reported client ordinal-43 failure remains under investigation.

- Fixed blocking updater work after download: verification, recovery and installer startup now run off the window thread, with separate status messages. Recovery retains the session guard and checksum checks; cancellation or recovery errors prevent setup. Older installed launchers need a manual installer update if their updater freezes.

- Enlarged the launcher's initial window and fitted Settings content to the display's working area; smaller monitors retain scrolling.
- Moved multiplayer out of the Launcher mode list into a neighboring **Multiplayer** tab with **HOST**, **JOIN**, **Refresh** and a LAN server browser. HOST retains the native-menu launch flow and advertises a **HOST game running** entry, without claiming a lobby or player count. JOIN adds the selected LAN PC to the game's network peers; players finish joining through the game's Multiplayer / LAN menu. Both PCs need the updated package. Automatic lobby entry remains unfinished. Native/launcher tests and loopback UDP checks cover discovery, expiry and peer setup; two-PC browser/JOIN acceptance is pending.

- LAN now uses the same normal career and graphics settings as desktop/VR play, with the game's Documents lookup unchanged. Removed the temporary import/profile-copy workflow and its controls. Old copy selections are ignored on upgrade; existing copies are left untouched. LAN identity and startup status remain in AppData. Shared-career native and launcher tests pass. The tester confirmed a change saved with the game's **Save Profile** action persisted from LAN into Normal Launch; automatic saving was not established. Post-exit checks confirmed original game-file restoration.
- Added **Skip startup logo movies (all launch modes)**, off by default. Temporarily replaces only the four logo-video states with immediate transitions; legal, attract, first-race and other video states remain intact. Uses guarded, journaled recovery through the normal file worker. Structural/restoration tests pass, and the tester confirmed the logos were skipped during LAN startup. Post-exit checks confirmed original game files restored and all 61 original career files unchanged.

- Integrated desktop LAN multiplayer into the normal launcher, installer and ZIP. Select **LAN multiplayer (desktop)** and use **Launch**; the intro-skip checkbox is now under **Settings**. Native host/join/event selection still use the game's LAN menus; launcher lobbies/browser and LAN VR remain unfinished. Persistent LAN profiles live in AppData, and the launcher session manager/file worker perform journaled shim restoration, including recovery of older kit journals. New launcher transaction, settings and offscreen UI tests pass; integrated gameplay acceptance is pending.

- Added an optional **Skip introduction** checkbox, initially in the separate LAN test kit, off by default. It bypasses the first-run movie and career tutorial decision in process memory, with executable/asset guards; it does not change game files or grant progression. Native guard and settings tests pass. The tester confirmed reaching LAN from a fresh profile without the movie or forced race. This option is now integrated into the launcher as described above.

- LAN test update: the tester confirmed hosting on PC1, joining on PC2 and both driving/seeing each other in the same race. Race completion and results remain untested. Post-exit checks confirmed exact `xlive.dll` restoration and unchanged existing career files.

- Added a separate `0.2.0` desktop LAN test kit using pinned XLiveLessNess, a fresh process-isolated profile, opt-in launch and journaled restoration of `xlive.dll`. Native profile isolation and eight recovery checks pass; local startup created only isolated save/settings files and left existing career files unchanged. Two-PC race acceptance is pending. Launcher multiplayer lobbies, LAN browser and automatic race startup are not yet implemented. See `docs/lan-lab.md`.

- Added a LAN multiplayer feasibility plan covering XLiveLessNess reuse, launcher host/join and discovery, native race-start integration, profile/DLL recovery and staged acceptance. The separate native test kit described above implements the first stage; launcher multiplayer integration remains planned.

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
