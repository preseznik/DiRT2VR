# Changelog

Notable changes to DiRT2VR are recorded here, newest first. Release channels identify normal and opt-in experimental builds; individual features may still have documented limitations. Older dated entries are development milestones. Headset checks refer to Quest 3 through SteamVR unless stated otherwise.

Version headings identify distribution builds; published packages include a GitHub release link. Earlier local-build experiments and validation observations are retained as development notes within the release that included them; later entries supersede those observations.

## Unreleased

- Fix Aspen building from read-only game files. Conversion now creates writable staging copies without changing the originals; cancellation can also clean up read-only temporary files.

- Reduce the installer, ZIP and installed application size by sharing the launcher's .NET runtime with Aspen conversion. Building still runs in a separate background process, supports offline use and cancellation, and preserves existing installed tracks.

- Bundle Aspen conversion tools with the normal Setup and ZIP packages. Building tracks needs no additional download; keep the standard three release assets and concise player-facing release notes.

## 0.17.4 — 2026-09-29 — Experimental

[GitHub release](https://github.com/preseznik/DiRT2VR/releases/tag/v0.17.4)

- Added an experimental CUSTOM tracks launcher flow for locally building Aspen's four Rallycross layouts from the player's own DiRT 3 Complete Edition and DiRT 2 installations. Installation is explicit; installed layouts can be verified and launched offline.
- Added source fingerprints, verified bundled conversion tools, staged installation and recovery of shared track files. Aspen remains restricted to desktop solo Subaru STI practice with its layout-specific lighting. Ice has less grip than packed snow in the user's comparison; exact DiRT 3 handling is not claimed.
- Keep Aspen tower glows and lit lamp faces in a persistent terrain layer. As a limited fallback for the remaining flicker, omit only the 12 decorative ski-hillside beams on Snowmass Sprint; preserve its other beams, towers, lamp faces and lighting, and leave the other layouts unchanged. The rebuilt candidate passed the user's hillside, timed-lap, reset and restart check.
- Preserve reconstructed Aspen snow surfaces and opaque depth in the distant terrain layer. The converter now includes Snowmass Sprint's accepted terrain-distance settings, resolving the reported ground colour transition and hillside tower disappearance in the tested view. Other layouts retain their existing distance settings.

- Recommend the seat-adjustment panel in Controls and tuck the six optional movement bindings into a section that starts collapsed.

## 0.17.3 — 2026-09-28 — Experimental

[GitHub release](https://github.com/preseznik/DiRT2VR/releases/tag/v0.17.3)

- Adjust your VR seat height, depth and sideways position separately for each car. Use **Graphics → VR cockpit**, or press **Tab** while stopped in cockpit VR to open the adjustment panel.
- Use the arrow keys or controller D-pad to move, hold **Shift / Xbox X** for sideways movement, **Enter / A** to save or **Escape / B** to cancel. Add optional movement and panel bindings in Controls. Saved positions survive recentering and relaunching.
- Add **Advanced → Frame-rate-independent rewind (Experimental)**, off by default. Keep more rewind history at high FPS without limiting gameplay. Initial Subaru/Velebit desktop checks passed; broader event and VR rewind testing remain pending.
- Seat-panel movement, save/cancel and persistence passed the reported headset check. Physical wheel navigation and broader rendering checks with adjusted seats remain unverified. Extreme positions can reveal missing interior geometry.
- Available through the launcher only with **? → About → Include experimental releases** enabled. Use **Return to stable…** in About to go back if needed.

## 0.16.0 — 2026-09-28

[GitHub release](https://github.com/preseznik/DiRT2VR/releases/tag/v0.16.0)

- Introduce optional experimental builds for smaller changes and early testing. This lets regular releases focus on more substantial improvements, with fewer update prompts. To try experimental builds, enable **Include experimental releases** in **? → About**; it is off by default.

- Save the experimental-update preference for automatic and manual checks; label experimental builds and offer a confirmed return to the latest stable release with a settings backup.

- Trim internal diagnostics, analysis utilities and legacy test kits from the public repository; retain pinned dependency setup and all build/package inputs.

- Keep development notes and tests out of the public source tree; builds no longer require them. Preserve matching LAN source and license notices.

## 0.15.8 — 2026-09-28

Local test build; included in 0.16.0.

- Add short hover explanations to launcher settings, including what Headset texture scale does and how it differs from Render resolution.

- Add an optional Patreon support link to the README; the mod will remain free forever.

- Make `main` the default branch for tested code and releases; keep unfinished custom-track development separate and retire obsolete branches.


## 0.15.7 — 2026-09-28

[GitHub release](https://github.com/preseznik/DiRT2VR/releases/tag/v0.15.7)

- Higher VR render resolution now increases scene detail instead of falling back to 720p. At full field of view, 150% renders at 2400 × 1800 and 200% at 3200 × 2400.
- Stop sliders and dropdowns flashing when changing settings in the Launcher and Graphics tabs.
- Keep the Graphics resolution readout stable while still updating it when measurements change.

## 0.15.6 — 2026-09-27

Local test build; included in 0.15.7.

- Fix VR resolution falling back to 720p at higher render scales.
- Stop Launcher controls flashing when changing sliders or opponent choices.

## 0.15.5 — 2026-09-27

Internal packaging candidate, superseded by 0.15.6 before deployment.

## 0.15.4 — 2026-09-27

Local test build; not yet published to GitHub.

- Keep Graphics slider changes local to their value/readout controls instead of resizing neighbouring sliders and dropdowns. Confirm that unsupported render sizes can fall back to 1280 × 720; check measured resolution before increasing headset texture scale. The game-side resolution fallback is not fixed yet.

## 0.15.3 — 2026-09-27

Local test build; not yet published to GitHub.

- Stop the Graphics resolution readout from resetting every second when its measurements have not changed. Keep live updates and stable missing/unavailable states. Restore the compact borderless caption within the Desktop group.

## 0.15.2 — 2026-09-27

[GitHub release](https://github.com/preseznik/DiRT2VR/releases/tag/v0.15.2)

- Show requested and measured VR resolution in Graphics to help diagnose blurry output. Clarify that borderless fullscreen applies only to desktop play. The reported resolution issue still needs affected-headset testing.
- Show saved dead zone and saturation beside driving bindings, and refresh them after calibration. Clarify that enabled launcher assignments reapply on launch. The reported wheel dead-zone reset remains unverified; controller defaults are unchanged.

## 0.15.1 — 2026-09-27

[GitHub release](https://github.com/preseznik/DiRT2VR/releases/tag/v0.15.1)

- Extend VR render resolution to 300%, with a native notch and “150% recommended” label. Preserve saved values and the 100% default. High-resolution headset and memory acceptance remain pending.

## 0.15.0 — 2026-09-27

[GitHub release](https://github.com/preseznik/DiRT2VR/releases/tag/v0.15.0)

- Keep release assets to the installer, player ZIP and checksums. Move matching LAN source archives into the repository and remove standalone diagnostic downloads from past and future releases; retain installed licenses and source references.

- Add a VR anti-aliasing slider (Off/2×/4×/8×), defaulting to 2× with high-memory warnings at 4× and 8×. Restore the original desktop MSAA after play.
- Size the HUD's OpenXR texture independently of the eyes, using the source image capped at 2048 pixels on its longest edge. HUD distance and apparent size remain unchanged.
- Add bounded, opt-in graphics failure reports with HRESULT/device-removal status and virtual-address-space snapshots around VR allocations. LAA remains unchanged; PC3/PC4 crash and headset-readability validation are still pending.

## 0.14.4 — 2026-09-27

[GitHub release](https://github.com/preseznik/DiRT2VR/releases/tag/v0.14.4)

- Correct VR startup ordering so the automatic cockpit hook is recorded by the GFWL compatibility layer even without launcher driving overrides. This addresses a concrete regression candidate for PC3/PC4 VR crashes; affected-PC gameplay confirmation remains pending.
- Report file-worker failures with the affected path, exception/HRESULT, exit code and administrator-retry stage. Keep one `worker-error.json` with both attempts, preserve recovery backups, and expand the optional crash collector with preparation diagnostics.

## 0.14.3 — 2026-09-25

[GitHub release](https://github.com/preseznik/DiRT2VR/releases/tag/v0.14.3)

- Distribute matching LAN library source as a separate versioned release ZIP instead of installing `lan-source`. Retain LGPL notices and a source-download link with its checksum under `DiRT2VR/licenses`.

## 0.14.2 — 2026-09-25

[GitHub release](https://github.com/preseznik/DiRT2VR/releases/tag/v0.14.2)

- Exclude developer `/docs` from installer and ZIP packages. Keep the end-user README, changelog, licenses and in-app Instructions; packaged technical-document links point to the matching GitHub release source.

## 0.14.1 — 2026-09-25

[GitHub release](https://github.com/preseznik/DiRT2VR/releases/tag/v0.14.1)

- Add a separate desktop VSync toggle, on by default, independent of borderless. Desktop launches temporarily apply the choice and restore original settings after play; VR retains its own timing setup.
- Reorganize Graphics into compact Desktop, VR rendering and VR HUD groups. Move detailed guidance into the offline ? → Instructions tab.
- Adapt Launcher, Multiplayer, Graphics, Controls, Settings and Help to wide, portrait and compact windows. Groups reflow, bindings stack and LAN rows become tiles at narrow widths; action buttons remain outside scrolling content. Resize checks preserve unsaved settings and keyboard capture.

- Add optional desktop borderless fullscreen under Graphics, off by default. Desktop launches use the primary monitor's native desktop size and the separate VSync preference; display overrides recover after play. VR is unchanged. Desktop borderless display, Alt+Tab and Return to menus passed a local 0.13.0 check; frame-rate comparison is recorded in the launcher implementation notes.
- Correct release history: assign shipped changes to their published versions and dates; retain only unpublished work here.

## 0.12.5 — 2026-09-24

[GitHub release](https://github.com/preseznik/DiRT2VR/releases/tag/v0.12.5)

- Use the working DirectInput wheel reader for Toggle VR and Recenter capture and background session input. Preserve existing Xbox/HID assignments, ignore held switches during capture, and require fresh presses after connection or reconnection. Physical Fanatec shortcut acceptance remains pending.
- VR launches request the real cockpit camera when the game restores its starting view, and start with cockpit VR enabled. Menus and pause screens retain their flat-screen handling; desktop launches retain their saved camera. Headset startup acceptance remains pending.

## 0.12.3 — 2026-09-24

[GitHub release](https://github.com/preseznik/DiRT2VR/releases/tag/v0.12.3)

- Fix driving capture for wheel drivers with nonstandard native offsets or read-only axis ranges. Learn each axis independently so pedal noise cannot block steering. Use modest steering rotation, but require a larger pedal press (45% of full endpoint-resting DirectInput travel) and ignore smaller movement. Game driving sensitivity is unchanged; physical Fanatec validation remains pending.

## 0.12.2 — 2026-09-23

[GitHub release](https://github.com/preseznik/DiRT2VR/releases/tag/v0.12.2)

- Simplify the driving-binding wizard with a large bold action title, action pictograms, a separate step counter and short capture prompts. Binding behavior is unchanged.
- Replace driving-input capture with a guided binding wizard: learn a stable resting position, capture deliberate travel or a button press, and require release before advancing. Walk through all actions, skip/back/review without saving partial changes, detect separate devices automatically, and offer axis/button filters. Constant high handbrake axes and held switches no longer appear as newly engaged controls. Physical Fanatec validation remains pending.
- Add a stock Xbox driving preset and reject identical left/right steering directions or inverted Xbox triggers when enabling overrides. Prevent capturing Xbox stick/trigger return motion as the intended direction; use the game's standard 20% Xbox stick dead zone. Regression tests cover the reported configuration and resting/inverted inputs.
- Direct practice and Race now load the existing profile through the game's native loader before starting the selected event, so saved driving controls can be applied. Local isolated-profile loading succeeds; PC3/Fanatec acceptance is deferred. No extra end-user career or encrypted-save editing is introduced.
- Add Controls → Configure driving controls with optional keyboard, Xbox and DirectInput wheel/pedal assignments, separate-device support, clutch/H-pattern actions and calibration sliders. Overrides apply through the native action parser in DX11 launcher sessions, including desktop launches without OpenXR. Actual-game parsing, native input-helper interop and launcher tests pass; physical wheel and combined LAN/VR acceptance remain pending.
- PC3's user reports no crashes so far with 0.10.2. This is encouraging follow-up evidence; longer-session coverage remains pending.

## 0.10.2 — 2026-09-23

[GitHub release](https://github.com/preseznik/DiRT2VR/releases/tag/v0.10.2)

- Add a candidate VR compatibility fix for Microsoft GFWL 3.5.95.0: its in-memory checksum accounts for intact, recorded mod hooks while retaining checks on other bytes and preserving the original profile APIs. No Windows DLL or career files are replaced. Native checksum/hook tests pass; PC3 crash acceptance remains pending.

### Earlier development and validation notes

- Add opt-in PC3 crash evidence tools and document repeated faults in the system GFWL DLL. Full-dump capture is temporary, limited to the game, and separate from normal diagnostic logging. The underlying VR crash and direct-event wheel-binding issue remain under investigation; no fix is claimed.

## 0.10.0 — 2026-09-23

[GitHub release](https://github.com/preseznik/DiRT2VR/releases/tag/v0.10.0)

- Graphics: add Tree detail and Object detail sliders using the game's native quality presets. Default to the existing game settings, apply only during VR sessions, and restore original values after play. Higher detail can reduce scenery pop-in; track-specific distance limits and headset performance still need comparison.
- Direct practice and Race: stop at a finish menu instead of automatically repeating. Add Restart and Return to menus to the pause menu too. Return to menus closes the direct session, restores temporary files and relaunches Normal Launch in the same Desktop/VR mode; Alt+F4 and ordinary quits do not relaunch. The desktop return path has been tested; headset validation is pending. Custom sessions remain separate from career rewards/results.
- Fixed the tested Ensenada Sprint puddle reflection mismatch by rendering the reflected camera separately for each VR eye while retaining the original reflection draw lists. Water remains visible. Identical-camera/asymmetric desktop benchmarks pass, and the user confirms the reflections look good in Quest 3. Enabled for normal VR launches; desktop rendering is unchanged. Broader tracks, reflection visibility when looking behind, and performance still need coverage.
- Changed fresh/default HUD settings to **1 metre** with **Speedometer / gear / revs** off. Restore graphics defaults uses the same settings; other HUD areas remain enabled. Explicitly saved preferences are preserved on upgrade. Apparent text size is unchanged.

### Earlier development and validation notes

- Added a **HUD distance** slider on Graphics: 1–20 metres in 0.5 m steps, default 4 m. It preserves apparent HUD size, as requested, and supports fixed and follow-view placement. Save and relaunch VR to apply; graphics defaults restore 4 m. Launcher, camera-math and OpenXR submission tests cover the setting. The user's 20 m check did not show an obvious distance change; added a one-time placement receipt when diagnostic logging is enabled. Perceptual headset acceptance remains unresolved.
- The user confirms water no longer appears to pop in with the partial filter removed, but reflections still differ between eyes. Requested input captures reproduce a shared original-camera reflection sampled using different eye projections. A separate per-eye reflection prototype lost vehicle batches in the second view even with identical cameras, so it was removed; no reflection fix is claimed. Normal play produces no capture files.
- Added five visibility toggles below **HUD follows view** for the cockpit HUD's gauges, lap/time, race position, route map and stage progress areas. All start enabled and reset with graphics defaults. These mask the standard HUD layout, including any other overlay sharing a hidden area; menus, virtual-screen and desktop HUDs are unchanged. Desktop captures and all 32 GPU-tested combinations pass; headset and alternative-layout checks remain pending.
- Stopped enabling the legacy two-shader water filter on normal VR launches. It hid some water variants while leaving others visible, a candidate cause of distance-dependent appearance. An unfiltered same-camera inner-scene replay now has matching draw lists. Water remains visible as requested; this is not a verified fix for stereo puddle reflections or straight-line scenery pop-in.
- Added a transparent cockpit HUD layer about four metres ahead, fixed relative to the car by default. Graphics → **HUD follows view** optionally follows head movement; Recenter repositions the fixed layer. HUD drawing is captured once per frame and composed for both eyes, separately from the scene. Desktop captures verify separation of the Baja lap/time, position, route map and speedometer from scenery; GPU, OpenXR lifecycle and launcher tests cover the implementation. Headset readability, placement, follow mode and broader event coverage remain unverified; the user deferred headset testing.

## 0.7.2 — 2026-09-23

[GitHub release](https://github.com/preseznik/DiRT2VR/releases/tag/v0.7.2)

- Fixed the identified LAN race-loading disconnect caused by startup logo skipping. Live 0.7.0 captures identified modified `system\states.bin` as the failed validation record; disabling logo skipping on both PCs allowed both players to drive, finish Battersea and reach results. LAN launches now preserve that file even when the preference is enabled. HOST/JOIN preflight rejects altered definitions, and preparation guards prevent combining LAN and movie edits. Single-player logo skipping and LAN's separate introduction skip remain available. All 378 launcher checks pass; LAN VR remains unverified.
- HOST and JOIN now ask for Desktop or VR on every launch, with Cancel available. LAN VR uses the existing SteamVR preflight, graphics/controls settings and combined recovery of VR assets, graphics, startup movies and the LAN DLL. The shared career and HOST/JOIN endpoint are preserved. Launcher tests pass; multiplayer headset testing is deferred. The loading disconnect is addressed by the startup-definition fix above.

### Earlier development and validation notes

- The 0.6.10 host diagnostic captured the game's session teardown path, but the two-PC loading disconnect persists. The next investigation targets the preceding session-state change; further gameplay testing is deferred.
- Two-PC 0.6.9 testing still disconnects during Battersea loading: PC1 first, PC2 later. Extended opt-in host diagnostics with bounded game-code stack candidates and session API errors to investigate the game's teardown decision. All six native tests pass; this diagnostic change does not claim to fix multiplayer behavior.

## 0.6.9 — 2026-09-22

[GitHub release](https://github.com/preseznik/DiRT2VR/releases/tag/v0.6.9)

- Corrected generated LAN peer addresses so different PCs have distinct, nonzero machine identities. The actual-DLL regression fails on 0.6.8 and passes with the correction; all six native checks pass. Both PCs must update for the next test. Whether this resolves the Battersea race-loading disconnect still requires two-PC confirmation.
- Extended opt-in LAN diagnostics with game-facing send/receive sizes and checksums, plus socket-close call sites. Paired Battersea traces show the host closing its sockets while transport acknowledgements are still flowing; the later client timeout is a consequence. The cause of the host's closure remains under investigation.

## 0.6.7 — 2026-09-22

[GitHub release](https://github.com/preseznik/DiRT2VR/releases/tag/v0.6.7)

- Added opt-in LAN network traces through Settings → Enable diagnostic logging. Each session keeps at most two 4 MiB files containing socket, packet-header, acknowledgement and timeout metadata, without packet payloads. Logging remains off by default. Native logging/transport and launcher tests pass. The two-PC follow-up reached race loading but disconnected at Battersea Rallycross before the grid; that failure remains under investigation.

## 0.6.6 — 2026-09-22

[GitHub release](https://github.com/preseznik/DiRT2VR/releases/tag/v0.6.6)

- Fixed a LAN transport keepalive defect that could time out an otherwise reachable idle peer. Receiving a probe now schedules an acknowledgement through the existing protocol. A regression against the actual DLL fails on the released build and passes with the fix, including ordered data, selective acknowledgements, bounded replies and disconnected-peer timeout. Two-PC lobby/race confirmation is pending.
- Added a read-only LAN startup report tool for ordinal/DLL-loading failures. It records running game/launcher paths, relevant loaded DLLs, file hashes and recovery state without reading saves or changing the installation. The reported client ordinal-43 failure remains under investigation.

### Earlier development and validation notes

- Client startup investigation: the supplied alternate wrapper and its two companions successfully reached regular menus in an authorized separate local test with the unchanged LAN shim. Both client variants are retained. PC2's ordinal-43 failure remains unresolved; a system GFWL identity-library dependency is now a concrete lead. Expanded the read-only report to capture companion files, compatibility settings and mapped DLLs even before normal loader initialization. No compatibility fix or new release is claimed.
- PC2's ordinal-43 report confirms correct game/shim files and an active recovery journal, with a different startup wrapper from the working PC. A direct-start candidate reached LAN initialization but failed the local game check; it was reverted and package 0.6.5 was not released. The client startup issue remains under investigation.

## 0.6.4 — 2026-09-22

[GitHub release](https://github.com/preseznik/DiRT2VR/releases/tag/v0.6.4)

- Fixed blocking updater work after download: verification, recovery and installer startup now run off the window thread, with separate status messages. Recovery retains the session guard and checksum checks; cancellation or recovery errors prevent setup. Older installed launchers need a manual installer update if their updater freezes.

## 0.6.3 — 2026-09-22

[GitHub release](https://github.com/preseznik/DiRT2VR/releases/tag/v0.6.3)

- Enlarged the launcher's initial window and fitted Settings content to the display's working area; smaller monitors retain scrolling.
- Moved multiplayer out of the Launcher mode list into a neighboring **Multiplayer** tab with **HOST**, **JOIN**, **Refresh** and a LAN server browser. HOST retains the native-menu launch flow and advertises a **HOST game running** entry, without claiming a lobby or player count. JOIN adds the selected LAN PC to the game's network peers; players finish joining through the game's Multiplayer / LAN menu. Both PCs need the updated package. Automatic lobby entry remains unfinished. Native/launcher tests and loopback UDP checks cover discovery, expiry and peer setup; two-PC browser/JOIN acceptance is pending.
- LAN now uses the same normal career and graphics settings as desktop/VR play, with the game's Documents lookup unchanged. Removed the temporary import/profile-copy workflow and its controls. Old copy selections are ignored on upgrade; existing copies are left untouched. LAN identity and startup status remain in AppData. Shared-career native and launcher tests pass. The tester confirmed a change saved with the game's **Save Profile** action persisted from LAN into Normal Launch; automatic saving was not established. Post-exit checks confirmed original game-file restoration.
- Added **Skip startup logo movies (all launch modes)**, off by default. Temporarily replaces only the four logo-video states with immediate transitions; legal, attract, first-race and other video states remain intact. Uses guarded, journaled recovery through the normal file worker. Structural/restoration tests pass, and the tester confirmed the logos were skipped during LAN startup. Post-exit checks confirmed original game files restored and all 61 original career files unchanged.
- Added an optional **Skip introduction** checkbox, initially in the separate LAN test kit, off by default. It bypasses the first-run movie and career tutorial decision in process memory, with executable/asset guards; it does not change game files or grant progression. Native guard and settings tests pass. The tester confirmed reaching LAN from a fresh profile without the movie or forced race. This option is now integrated into the launcher as described above.
- Adopt plain `major.minor.patch` versions starting from `0.1.0`, without alpha suffixes. Packaging increments patch for each distribution build; entirely new features increment minor and reset patch, and major changes require an explicit user request. Existing alpha release history is preserved.

### Earlier development and validation notes

- Integrated desktop LAN multiplayer into the normal launcher, installer and ZIP. Select **LAN multiplayer (desktop)** and use **Launch**; the intro-skip checkbox is now under **Settings**. Native host/join/event selection still use the game's LAN menus; launcher lobbies/browser and LAN VR remain unfinished. Persistent LAN profiles live in AppData, and the launcher session manager/file worker perform journaled shim restoration, including recovery of older kit journals. New launcher transaction, settings and offscreen UI tests pass; integrated gameplay acceptance is pending.
- LAN test update: the tester confirmed hosting on PC1, joining on PC2 and both driving/seeing each other in the same race. Race completion and results remain untested. Post-exit checks confirmed exact `xlive.dll` restoration and unchanged existing career files.
- Added a separate `0.2.0` desktop LAN test kit using pinned XLiveLessNess, a fresh process-isolated profile, opt-in launch and journaled restoration of `xlive.dll`. Native profile isolation and eight recovery checks pass; local startup created only isolated save/settings files and left existing career files unchanged. Two-PC race acceptance is pending. Launcher multiplayer lobbies, LAN browser and automatic race startup are not yet implemented.
- Added a LAN multiplayer feasibility plan covering XLiveLessNess reuse, launcher host/join and discovery, native race-start integration, profile/DLL recovery and staged acceptance. The separate native test kit described above implements the first stage; launcher multiplayer integration remains planned.

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
