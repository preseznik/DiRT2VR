# DiRT2VR

[**Stable download**](https://github.com/preseznik/DiRT2VR/releases/latest) · [**All releases, including Experimental**](https://github.com/preseznik/DiRT2VR/releases)

Open **Assets** on the release page and choose the installer or ZIP. Choose a release marked **Pre-release** for the latest Experimental features. In an existing launcher, enable **? → About → Include experimental releases** to receive these updates. **Return to stable…** lets you go back.

A desktop and VR launcher for **DiRT 2**, with custom tracks, visual presets and experimental stereoscopic VR with head tracking. Game menus use a virtual screen in VR.

Tested with **Quest 3 through SteamVR**. Xbox-compatible controller binding, launching, Toggle VR and Recenter have been tested in game. Other headsets and wheel button bindings need testing; **PSVR2, Logitech and Fanatec hardware are not yet verified**. This remains an experimental build.

This mod will remain free forever. If you'd like to support the work, you can [support me on Patreon](https://www.patreon.com/cw/bohloney).

## Requirements

- Windows x64 and a PC capable of running DiRT 2. VR requires SteamVR or a compatible 32-bit OpenXR runtime with D3D11 support. VDXR is experimental.
- Your own working **DiRT 2 version 1.1.0.0** installation. Setup checks the exact supported executable.
- For VR, a headset connected and ready in your selected runtime: **SteamVR** or **system OpenXR** (including VDXR). Desktop play needs no headset.
- Your usual keyboard, gamepad or steering wheel and pedals. Motion controllers are not used.
- A packaged DiRT2VR installer or ZIP. Packages include the .NET runtime; no development tools or separate .NET installation are needed. The source repository alone is not a playable package.

## Profiles (Experimental)

Open **Profile** to choose **Current game career** or create a separate career with **Create new profile…**. Choose **Fresh career** or **100% completed career**, enter a name, and the new profile becomes active automatically. The active row is green and marked **Active**. To switch to an existing career, select it and choose **Use this profile**. Names must contain 1–24 letters (A–Z) or numbers (0–9), without spaces or symbols. Existing profile names are left unchanged. No game setup session is needed. Close DiRT 2 before creating or switching profiles; selection saves immediately.

The selected career is used for Normal Launch, Practice, Race and LAN, in desktop or VR, and remains selected when returning to menus. Completed careers include stock career wins, cars, liveries, rewards, All-Star upgrades and teammate relationships. They do not include platform achievements, invented personal-best times or external content. Existing careers cannot be unlocked by this feature.

Your original saves stay in their current location. New profiles have separate save folders under `%LOCALAPPDATA%\DiRT2VR\profiles`, shared by your DiRT2VR installations on this Windows account. Graphics settings and launcher bindings remain shared. Launching the game outside DiRT2VR uses your original career.

**Delete profile…** permanently removes a launcher-created career and its saves after confirmation. First select another career for launching. Current game career cannot be deleted here. Profiles are shared between installations on this Windows account.

Use **Refresh** after saving in-game. Unreadable details show **Unavailable**; intermediate completion percentages and garage counts are not yet supported. If the original game has multiple careers, choose between them in its Load Profile menu. A missing or incompatible selected profile blocks launch until you explicitly choose a valid one.

Single-word launcher profile names also appear on screens which normally show a surname. Existing profiles receive this display correction automatically, without renaming or rewriting their saves.

## Install alongside your game

**A second copy of the game is not required.** Run DiRT 2 normally once before installing, then close it.

**Installer:** run the DiRT2VR Setup EXE, confirm the detected game folder (or browse to it), and finish setup. Select the folder containing `dirt2.exe` and `dirt2_game.exe`.

**ZIP:** extract the complete ZIP into that same game folder. Open `DiRT2VR.exe`. The first VR launch checks compatibility and installs the proxy from `DiRT2VR/payload`.

The game folder will contain `DiRT2VR.exe`, `Start-DiRT2VR.cmd`, a `DiRT2VR` subfolder, and an installed `d3d11.dll`. Most mod files stay in the subfolder. Game executables and the existing career profile are preserved. LAN and launcher-created profiles temporarily use the bundled offline provider through `xlive.dll`; the original bytes are restored after play. Other launches keep the original provider.

If another `d3d11.dll` is present, setup refuses to overwrite it. Remove the conflicting graphics mod using its own instructions first; automatic proxy chaining is not supported.

Start the launcher normally, **not with Run as administrator**. In protected game folders, only the file worker requests elevation when needed. Preferences are saved under `%LOCALAPPDATA%\DiRT2VR`, separately for each installation.

## Play

**Launch** starts regular desktop play; **Launch VR** starts headset play. Both use the selected Normal Launch, Direct practice or Race mode. The launch buttons stay on the left; Save settings, Restore original files and Open logs stay on the right.

The launcher minimizes after starting either mode and stays minimized during play. Reopen it from the taskbar when needed; the background session manager continues running.

DiRT 2 may briefly fall behind other windows as it replaces its startup window. The launcher hands focus to the replacement once; the brief drop followed by immediate return has been confirmed on desktop.

**Race difficulty** controls AI opponents in stock and CUSTOM-track Race launches. Choose Easy, Casual, Serious, Savage, Extreme or Hardcore; **Use game setting** leaves the game’s saved choice in effect. Each custom location remembers its own choice. This does not change career difficulty in Normal Launch or multiplayer.

Choose **Rookie**, **Pro** or **All-Star** beside the car picker for stock cars in Direct practice and Race, on desktop or in VR. The selected grade applies to your car and every AI opponent, including mixed grids and staggered rally races. Rookie is the default. CUSTOM cars retain their own specification. Save settings or launch to keep your choice; saved replays retain their recorded grade. Normal Launch, LAN and career upgrades use the game's own selections.

Use **Class** above either car picker to show **All** installed stock cars or just Rally, Rallycross, Trailblazer, Raid T1, Stock Baja, Class 1 Buggies or Trophy Trucks. If the current car is outside the chosen class, the first matching car is selected. The grade stays unchanged. The filter starts at All when reopening the launcher and is disabled for CUSTOM cars; it does not restrict Mixed opponents.

Stock-car pickers and per-car VR seat settings also include the prize cars: MG Metro 6R4, Dallenbach Special, Ford RS200 Evolution, Mitsubishi Pajero Dakar 1993, Toyota Stadium Truck, 1995 Subaru Impreza WRX STI and Dodge Power Wagon. Direct practice and Race can use these installed cars without unlocking them in career. This does not change your garage or career progress.

**Vehicle setup…** opens the six game-style adjustments: gear ratio, downforce, suspension, ride height, differential and brake bias. Enable **Use this setup for direct launches**, choose one of five positions on each bar, then **Apply**. Save settings or launch to keep your choice. **Reset to centre** returns all six bars to the middle; uncheck the option to use the game’s setup. The same setup applies to your car in stock and CUSTOM Direct practice and Race, on desktop or in VR. Normal Launch, opponents and career saves are unchanged.

**CUSTOM cars (Experimental)** beside either Car selector lists supported local conversions. The first profile is **Ferrari F2004** from your installed Assetto Corsa. Choose **Build/import…** to generate its body, cockpit, handling approximation and V10 audio locally. Its source folder is detected automatically; **Manage… → Choose source folder…** lets you select another location. You can stop conversion before installation begins. Game assets are not included in downloads.

After updating, use **Manage… → Rebuild from source** beside the car selector to apply the cockpit and steering-wheel flicker fixes, texture-filtering improvements and revised steering and tyre tuning. Updating the launcher alone does not regenerate installed cars.

Ferrari supports experimental **Direct practice** and **Race** on compatible installed tracks: use **Launch** for desktop or **Launch VR** for the headset. Race supports up to seven opponents, subject to the track's grid limit and available AI paths. AI handling and headset acceptance remain experimental; opponents currently retain donor engine audio. Custom tracks must support the selected mode. Hover over a disabled launch button to see what needs changing. CUSTOM cars works independently of CUSTOM tracks. Turn it off to restore the previous stock-car selection. **Manage…** offers rebuilding, installation rechecking and uninstalling. An installed car works offline without Assetto Corsa. Ferrari best laps are kept separately from stock-car history.

The F2004 uses sequential shifting with an automatic clutch for the session: use your **Gear Up/Down** bindings (A/Z with the default keyboard preset). Other cars keep their saved transmission setting. The steering-wheel LCD shows **RPM, gear and speed in the game's selected units**; the stock HUD RPM dial is not calibrated for the F2004. **Rebuild an existing import** to obtain the updated LCD and engine tones. Desktop engine sound and gear changes passed a player check; audio remains an approximation of the source car.

Cockpit stabilization reduces visual shake without changing physics and follows deliberate right-stick look. Handling, suspension and the fitted native driver's appearance and animation remain experimental. The normal handbrake control can hold the car on the grid (Space with default keyboard controls; A with the Xbox preset). Body paint uses a matte approximation. Mirrors, damage and distant detail levels have limitations. LAN, career and saved full-replay playback are unavailable for this car.

Regular Launch needs no SteamVR or headset and uses your desktop graphics preferences. Desktop Direct practice and Race require DX11 and temporarily enable human control, the selected race and the direct-session menus. VR shortcuts and the VR rendering/HUD settings apply only to Launch VR.

1. Start your headset software and connect your headset. For VDXR, select VDXR as the OpenXR runtime in Virtual Desktop Streamer.
2. Open **DiRT2VR.exe**, choose **Normal Launch** on **Launcher**, and select **Launch VR**. On your first VR launch, choose **OpenXR — system default** or **SteamVR**. The prompt shows the detected system runtime. Check **Remember my choice** to skip the prompt next time.
3. The launcher stays visible while VR loads, then brings the game window forward. Leave it in front during startup. Navigate the original game on the virtual screen using your usual controls.
4. Enter an event with your chosen car. VR launches select cockpit view automatically.
5. Cockpit VR starts enabled. Sit facing forward while loading: the view centers automatically once tracking is ready and again when cockpit view first begins. Press **F10** (or Recenter) to adjust it afterward. Recenter resets your seated position and horizontal direction without baking head tilt into the view; saved seat adjustments stay unchanged. **F9** (Toggle VR) switches between cockpit VR and the flat screen; keep the game window focused.
6. Pause/options menus automatically use the virtual screen; resuming restores your selected cockpit VR mode. Use Toggle VR if another menu, replay or flashback looks incorrect.
7. Quit normally. The background session manager restores temporary files when the game exits. You may close the settings window while playing.

For quick launch with saved settings, use **Start-DiRT2VR.cmd**. It runs the same session manager without opening the settings window. Its equivalent command is `DiRT2VR.exe --launch --no-ui`.

For desktop quick launch, use `DiRT2VR.exe --launch --desktop --no-ui`. The existing `Start-DiRT2VR.cmd` continues to launch VR.

## Custom tracks (experimental)

See the [custom track catalogue](CUSTOM_TRACKS.md) for source games, course descriptions, every available layout and special attributes.

Build tracks from your own installed source games. Conversion tools are included; game assets are not distributed. No SDK or separate .NET installation is required. Once installed, custom tracks work offline without their source game; keep it available for future rebuilds.

Use the **Source** dropdown above CUSTOM tracks to check one or more source games, or choose **All source games**. **Installed tracks only** hides uninstalled layouts and source games with no installed packs. It checks converted DiRT2VR tracks, not whether the original source game is still installed. Tracks needing repair remain visible. Filters keep your saved track choice; if it is hidden, select a visible track or choose **Show all tracks** before launching. **Save settings** remembers your filters for this installation.

Prepared external track packs go in `DiRT2VR/custom-tracks/external/<pack-id>` inside the game installation. Choose **External folders… → Open external tracks folder**, copy the complete pack, then **Refresh**. They appear under **Source → External**. Incomplete or incompatible packs stay visible with an explanation. These must be prepared DiRT2VR packs; copying raw tracks from another game does not convert them. See the [external pack guide](EXTERNAL_PACKS.md).

External tracks support only the Direct practice, Race, VR and replay modes declared by their pack. They do not integrate with career or LAN. The launcher checks the payload before temporarily activating it, then restores the original files after the session. Keep the pack while playing. Close the game before replacing or removing it; launcher cache cleanup leaves external packs alone. Complete supported conversions keep their usual locations, categories and verification rules.

**Open external cars folder** opens `DiRT2VR/custom-cars/external`. Place a complete prepared car pack in its own subfolder, then choose **CUSTOM cars → Manage… → Refresh external packs**. External cars are marked in the car selector; missing or incompatible selections cannot launch. Car and track capabilities must both permit the selected mode. External cars use their declared stock donor’s class and audio. Saved replay playback requires the same pack identity, version and contents.

| Track pack | Required source game | Courses |
| --- | --- | --- |
| Mizu Mountain | GRID 2 | One 10.4 km daylight route |
| Nordschleife | Assetto Corsa, standard Nordschleife circuit | Full circuit in Daylight, Overcast and Evening |
| Spa-Francorchamps | GRID 2 with Spa-Francorchamps DLC | Full 7 km GP circuit, Direct practice and Race on desktop or in VR |
| Misty Loch | Assetto Corsa with Misty Loch 1.4 (`rt_misty_loch`) | Full 6.45 km circuit, Direct practice and Race on desktop or in VR |
| Aspen | DiRT 3 Complete Edition | Ten layouts |
| Smelter | DiRT 3 Complete Edition | Ten layouts |

All six packs offer **Direct practice** and experimental **Race** on desktop or in VR with stock cars. Custom cars follow their own mode limits. Custom-track LAN is unavailable.

### Misty Loch

**Updating an existing Misty Loch build:** choose **Manage… → Rebuild from source…** to apply smooth dry asphalt physics and reduce excessive road shaking. Daylight, Evening and Morning Fog are built together. Existing tracks remain usable until you rebuild. Older saved replays need their original track build because the road physics changed. The fix for flickering black road bands is preserved.

Choose **CUSTOM tracks → Misty Loch → Build and install…** and select the Assetto Corsa folder containing `content\tracks\rt_misty_loch`. Only your installed DiRT 2 files and Misty Loch 1.4 folder are used; no other track, extra grass atlas or CSP installation is required. Original game assets are not included in DiRT2VR downloads.

The optional **Build with high quality assets** checkbox improves material fidelity and adds denser, longer-distance grass for all three presets. It is off by default and requires rebuilding. It can use more GPU and memory; it preserves source texture resolution and your graphics settings. The launcher shows the installed quality separately. Uncheck it and rebuild to restore Standard. See [Misty Loch asset quality](CUSTOM_TRACKS.md#misty-loch) for current validation status and limitations.

Choose **Daylight**, **Evening** or **Morning Fog**, an installed car and use **Launch** or **Launch VR**. **Direct practice** runs one complete lap; **Race** offers 1–7 AI opponents and 1–20 laps. If Misty Loch was built with an older launcher, use **Manage… → Rebuild from source…** to add the eight-car grid before racing. Existing builds still support Direct practice. VR and AI racing remain experimental. Morning Fog's corrected cockpit appearance has passed a headset check; the final HQ build still needs headset and repeated in-game restart checks. Longer sessions and wider car combinations also need testing. Custom-track LAN is unavailable. The port includes layered road/terrain textures, roadside grass and flowers, moving waterfall water, lake waves and gentle boat motion. Assetto Corsa lighting and some effects remain approximate; the boats do not follow their original sailing routes.

### Mizu Mountain

Choose **CUSTOM tracks → Mizu Mountain → Build and install…**, select your GRID 2 game folder, then choose an installed car and **Direct practice** or **Race**. Use **Launch** for desktop or **Launch VR** with your selected runtime and headset ready. The full route is 10.4 km, with three timing checkpoints and a single run from start to finish. Race supports **1–7 AI opponents**, opponent-car choices and race difficulty.

Older Mizu packs need **Manage… → Rebuild from source…** to add the separated starting grid and the **MIZU** pre-race label. Current race packs need no rebuild to enable VR. Desktop practice and Race have passed player checks; headset rendering still needs testing. Some materials and lighting are approximations, movable props and soft vegetation do not have matching collision, and crowds and some animated effects are omitted.

### Aspen and Smelter

Aspen and Smelter each add ten layouts built from your own **DiRT 3 Complete Edition** installation. Conversion tools are included with DiRT2VR; no additional download is needed and no game assets are distributed. You also need the supported original DiRT 2 files. No SDK or separate .NET installation is required.

1. Close DiRT 2. In **Launcher**, check **CUSTOM tracks (Experimental)**.
2. Choose a **Track pack**, then **Build and install…**. Select your detected DiRT 3 folder, paste its path, or use **Browse**. Choose the DiRT 3 Complete Edition game folder, wherever it is installed.
3. Check the **layouts to build**, using **Select all** or **Select none** as needed. Your choices are remembered for each location. Choose **Build and install** and leave the launcher open while it checks source files and converts only those layouts. Unchecked layouts already installed are kept. **Stop build**, beside the green progress message, stops checking or conversion safely and keeps installed tracks. Once final installation begins, Stop is disabled until it finishes.
4. Select a layout, **Direct practice** or **Race**, and any installed car. Choose **Launch** for desktop play, or **Launch VR** with your selected runtime and headset ready. Building does not start the game. Installation is performed once, before play; nothing is converted during game loading.

| Layout | Default lighting |
| --- | --- |
| Lakeside | Night |
| Lake View | Morning sun |
| Snowmass Sprint | Evening sun |
| Snowmass Loop | Overcast |

All twenty Aspen and Smelter layouts offer experimental **desktop and VR** play with any installed car. **Direct practice** is solo; **Race** adds AI opponents, using the same car, mixed cars or cars in your class. Rallycross and Landrush courses allow **1–7 AI opponents**. The four Head-to-head courses have two-car starting grids, so their Race mode allows **one AI opponent**. Race timing on these separate-lane courses still needs testing; DiRT 3's knockout Head-to-head rules are not included. Choose **Race difficulty** from Easy, Casual, Serious, Savage, Extreme or Hardcore, as in DiRT 2. **Use game setting** keeps your saved difficulty. Use the sliders to choose opponents and **1–20 laps**. Your normal opponent count is remembered when moving to and from a two-car course.

**No rebuild is needed to unlock these options on current installed layouts.** Very old Aspen installs may request an AI-path rebuild before racing; Direct practice remains available. Expanded AI racing, car combinations and headset rendering need gameplay validation. LAN is unavailable. Trackside effects and snow spray are included; ski-lift animation, full snowfall and deformable snow remain unsupported. Snow handling is an adaptation for DiRT 2, not an exact recreation of DiRT 3 physics.

Snowmass Sprint omits the decorative beams on the ski hillside where they flickered with viewing angle or distance. The towers, bright lamp faces and ground lighting remain, along with the other beams around the course. The other three layouts are unchanged by this workaround.

Each pack keeps its own layout, car, race and lap selections, separately from stock tracks. Turn **CUSTOM tracks** off to restore your original track, car and event selections. Installed custom layouts work **offline**, without their source game. Keep the source installation available if you want to rebuild or update a pack. Successful file checks are remembered while the launcher stays open, so switching locations does not repeat the scan. Installed-file changes trigger a new check. Use **Manage… → Verify installed files** to check again at any time. The **Manage…** menu also offers rebuilding and removal. Rebuild uses the same layout checkboxes: select just the courses you want to update or add. Choose Select all to update the whole location. The list shows which layouts are already installed, and the launcher reports the installed count. Conversion-tool updates arrive with DiRT2VR updates.

### Spa-Francorchamps

Choose **CUSTOM tracks → Source: GRID 2 → Spa-Francorchamps → Build and install…**, then select your GRID 2 installation with the Spa-Francorchamps DLC installed. The full GP circuit supports **Direct practice and Race** on desktop or in VR, with 1–20 laps, three timing sectors and replay cameras. Race supports 1–7 opponents, car choices and difficulty settings. Existing Spa packs need **Manage → Rebuild from source** once to add the separated starting grid for Race; VR practice needs no rebuild. VR and AI races are experimental and awaiting driving/headset acceptance. Track assets are built locally from your games and are not included in the installer.

### Nordschleife

After updating, use **Manage… → Rebuild from source…** to correct overly shiny asphalt in all three lighting presets. Updating the launcher alone does not regenerate installed track assets.

The **Nordschleife** pack uses your installed **Assetto Corsa** standard circuit. Select **CUSTOM tracks → Nordschleife → Build and install…**, then choose the Assetto Corsa folder containing `content\tracks\ks_nordschleife`. All three lighting presets are built together. Choose **Daylight**, **Overcast** or **Evening**, an installed car, and **Direct practice** or **Race**. Race supports **1–20 laps** and **1–7 AI opponents** on desktop or in VR, with car choices and difficulty settings. Direct practice uses one lap. Older solo packs need **Manage… → Rebuild from source…** to add the race grid; previous best laps are kept.

These are fixed dry lighting presets. Eight timing checkpoints divide the circuit into roughly 2.3 km sectors. **Best lap** shows your fastest completed Direct practice time and its car across all lighting presets; Race results appear in game. The stage progress marker follows your position around the circuit. One-lap sessions show checkpoint divisions; multi-lap races show lap divisions. Existing packs need no rebuild for the progress correction.

The full trackside scenery and distant landscape are included. A complete evening desktop practice lap and a short native VR session have been player-tested. Full-course headset performance and AI Race finishes still need testing; scenery detail and lighting remain in development. For VR, start your selected headset software, connect your headset and choose **Launch VR**. Existing packs need no rebuild to enable VR.

If source checking fails, choose the correct installation or verify its original files in Steam. Modified or unsupported source files are reported by name. If an installed file fails verification, choose **Manage… → Rebuild from source…**. The installer preserves external edits instead of overwriting them; move your modified files aside before retrying. After an interrupted game session, close DiRT 2 and choose **Restore original files** before playing again.

### Aspen Landrush and head-to-head layouts

Aspen includes these six courses alongside the four Rallycross layouts listed above. To add missing layouts or update existing ones, choose **Aspen → Manage… → Rebuild from source…** and select the courses you want. Other installed layouts remain available.

| Layouts | Conditions |
| --- | --- |
| Eagle Hill Rise, Eagle Hill Loop | Night |
| Brush Creek Sprint, Brush Creek Dash | Evening sun |
| Buttermilk Descent, Buttermilk Climb | Overcast snow lighting |

These courses offer the same modes and car selection as Rallycross, with the two-car limit on Buttermilk. Time of day follows source defaults, with reduced exposure on Buttermilk to retain snow detail; full snowfall remains unsupported. Broader racing and VR checks are still needed, including snow handling and night headlights.

### Smelter — ten layouts

Select **Smelter** in CUSTOM tracks and choose **Build and install…** using your DiRT 3 Complete Edition folder. Select a layout, mode and car, then **Launch** or **Launch VR**. Dredger Duel and Furnace Duel allow one AI opponent; the other layouts allow up to seven.

| Layouts | Conditions |
| --- | --- |
| County Loop, Portage Canal | Morning sun |
| Houghton Sprint, Waterfront Park | Wet lighting, without active rain |
| Copper Run, Maple Woods | Evening sun |
| Atlantic Mill, Cole's Creek | Morning sun |
| Dredger Duel, Furnace Duel | Evening sun |

Use **Smelter → Manage… → Rebuild from source…** after updating to apply scenery updates and add missing layouts. Updating the launcher alone does not rebuild tracks. Existing tracks remain usable until the rebuild completes. General Maple Woods scenery flicker has improved in testing, but some log piles remain missing. Other layouts and performance still need wider testing.

**Buttermilk Climb and Descent use lower exposure by default** to retain detail in bright snow. Climb's improved snow detail has been confirmed in desktop testing; Descent still needs a visual check. The abrupt brightness border remains unresolved. **No track rebuild is needed** for installed layouts. Under **Snow lighting**, choose **Original exposure (reference)** or **Bloom off (diagnostic)** for comparisons; exit the game between profiles. Comparison choices reset to **Lower exposure (default)** when changing layout or reopening the launcher. Original post-processing files are restored after exit; interrupted sessions use the usual recovery, preserving external edits. Other tracks keep their existing exposure.

Wet-weather handling, expanded AI races and VR still need gameplay validation. The four Head-to-head courses particularly need start, timing and finish checks with an opponent. The concrete-barrier compatibility test remains visually unverified.

To switch from VR to desktop, quit the game and choose **Launch** in DiRT2VR. This uses your saved desktop graphics and filter choices. Launching the game outside DiRT2VR uses the original game settings.

## Launcher settings

The launcher matches the Windows app light/dark setting when opened. Reopen it after changing that setting. Native dark mode requires Windows 11; Windows 10 and Windows contrast themes retain the standard accessible system appearance.

**Launcher** selects how to start. **Settings** contains the game location, **VR runtime** and setup instructions. Choose **Ask every time**, **OpenXR (system default)** or **SteamVR** under VR runtime; changes apply on the next launch. System OpenXR uses the registered 32-bit runtime and can also resolve to SteamVR. VDXR headset rendering and gameplay validation remain pending. **Graphics** and **Controls** hold the settings below. Choose **Save settings**; changes apply to the next session, including quick launch. Existing shortcuts and graphics preferences are preserved when upgrading.

The launcher remembers its window size and maximized state when closed. It adapts to your current screen and display scaling when reopened. Closing the window does not save uncommitted game-setting edits; use **Save settings** for those.

**Settings → Appearance → Modern interface** previews the compact modern look immediately; turn it off for Classic. Select **Save settings** to keep your choice. Existing installations start in Classic; fresh installations start in Modern. Both follow Windows light/dark appearance. In Modern Controls, click a binding cell to change it, **+** to add another controller assignment and **×** to remove one. Hover shortened cells for full device details.

**Settings → Enable diagnostic logging** is off by default, including when upgrading older settings. Enable it and save before a troubleshooting run to collect preflight details and game-side logs in **Open logs**. Turn it off afterward to stop generating session logs, frame CSVs and diagnostic captures. Existing logs are kept; you can remove them yourself when no longer needed. Settings, the latest session/refresh-rate/resolution summaries and recovery journals remain available with logging off so the launcher can restore your game files safely.

During a guided headset test, a message card may appear below your forward view. **Enter** opens its reply box; type your answer and press **Enter** again to save it, or **Escape** to cancel. Replies stay in that test's local logs for the person running the test to read. Keep the game focused and stop the car before typing; the race continues. This input is active only in explicitly enabled diagnostic sessions.

### Direct practice (experimental)

On **Launcher**, choose **Direct practice**, an **Event** category, a **Track**, and a **Car**, then **Launch VR**. Event filters the track list by discipline; this is solo practice, not a career event. The launcher lists installed routes and cars from the supported game's catalog.

Direct practice bypasses the trailer menus and loads a player-driven car. Launch VR selects the cockpit and enables VR automatically; regular Launch retains your desktop camera. At the finish, choose **Full Replay** to watch the completed run from the start. Use **F1** to pause/resume playback. **Esc** or controller **B** opens the replay pause menu; press it again or choose **Continue** to resume from the same point. Choose **Full Replay** to start over, or **Exit replay** to return to the finish menu. Playback also returns there when it ends. You can watch again until restarting or leaving the event. Full Replay is unavailable if the game could not keep a complete recording. In VR, the existing **3D chase replays** option applies. The driving pause menu provides Continue, Restart, Return to menus and **Quit**. Quit is also available in the finish menu and while a replay is paused; it exits the game without reopening it, and the launcher restores session files afterward. Use **Up/Down** to select these vertical menu choices. **Return to menus closes the game and automatically reopens Normal Launch**, keeping Desktop or VR mode; expect another loading sequence. Your saved launcher selections stay unchanged. Alt+F4 quits without reopening. Saved practice selections also work with `Start-DiRT2VR.cmd`.

### Saved replays (Experimental)

In **Launcher → Replays**, switch **Record full replays** to **ON** before launching Direct practice or Race, including CUSTOM tracks. It starts **OFF**, including after updating. The choice saves immediately and applies on the next launch. OFF skips full-race recording and saving; instant replay, rewind and playback of existing saved replays remain available. Normal Launch retains the game's own replay behavior.

With recording on, **Full Replay** is available in the finish menu and **Save last replay** keeps the latest completed run. Turn on **Auto-save completed runs** to keep every completed attempt; it starts off and is unavailable while recording is off. Unfinished runs cannot be saved.

Choose a recording and select **Play** or **Play in VR**. The launcher checks the recorded cars and track before starting; missing or incompatible content is reported without changing game files. Replay controls are the same as Full Replay, including Esc/Continue and chase orbit/zoom. VR chase views use **Advanced → Graphics → 3D chase replays**; cinematic angles stay on the virtual screen.

Recordings are compressed automatically and unpacked temporarily for playback. **Open folder** opens this installation's `Replays` folder in your local DiRT2VR user-data directory. Tested recordings of about one minute took 43–58 MB at high frame rates; size varies with frame rate, duration and the race. Auto-save has no storage limit, so use **Delete…** to remove recordings you no longer want. When auto-save is off, each completed run replaces the unsaved last replay; recordings already saved in the library remain.

This first experimental player supports recordings made by the same installation. Replay sharing/import and recordings from Normal Launch are not supported yet. Careers are unchanged.

Some older recordings lack data needed to restore particle effects in a new game process. If playback reports an unsupported native record and closes, record a new run with the updated launcher; the missing information cannot be recovered from that recording.

### Race (experimental)

Choose **Race**, an event, track and car, then set **AI opponents** from 1 to 7. Use **Launch** for desktop or **Launch VR** for headset play. Choose **Opponent cars**:

- **Same as driver:** all opponents use your model.
- **Mixed:** other models from all installed classes, regardless of event.
- **Same class:** other models in your car's game-defined class.

**Race start** defaults to **Automatic**: stock Rally and Trailblazer stages use **Rally (staggered)**, with separate starts and elapsed-time standings. Other tracks use **Grid race**, starting all cars together. Select **Grid race** to keep simultaneous racing on a rally stage. An explicit staggered choice blocks incompatible tracks; choose Automatic or Grid race to continue. CUSTOM tracks currently retain grid starts. Saved replays remember their original start format. The desktop Spillway Short start, timing, finish menu, Full Replay and Restart passed a player check; other stages and VR still need acceptance.

Mixed/class grids are randomized each launch, repeating models if the available pool is small. If no other eligible model is installed, they use your model. Direct practice remains solo regardless of saved opponent settings. Start with Landrush or Rallycross. Other disciplines and track/car combinations need testing.

AI opponents use distinct stock driver identities; their names do not depend on the opponent-car selection.

Use **Laps (circuits)** to choose 1–20 laps in either **Race** or **Direct practice**. On point-to-point stages this control is disabled and the session is one stage run; your saved circuit lap preference is retained. The three-lap HUD and continuation into lap 2 have been confirmed on desktop at Baja – Ensenada Sprint. Other counts, complete multi-lap finishes and VR still need testing.

Race uses the same finish and pause choices as practice. It stops at the finish menu rather than automatically repeating. Return to menus restarts the game in Normal Launch; this return path has been confirmed on desktop, while headset validation is pending. **Alt+F4 quits.** Custom races do not award career progress or use the normal results flow; use Normal Launch for full career events. Desktop player control and seven AI opponents have been confirmed at Baja. Mixed opponent models also passed a desktop race check. Same-class gameplay and crowded-grid VR performance still need testing.

### Advanced

**Frame-rate-independent rewind → On (Experimental)** is off by default. It preserves more rewind history at high FPS without limiting gameplay frame rate. Save and relaunch to apply it to single-player Normal Launch, Practice or Race, on desktop or in VR; LAN ignores it. Desktop play requires DX11. Diagnostic logging is not required.

The initial desktop test retained 10 seconds of history instead of about 5 seconds at roughly 120 FPS. Other events and VR still need testing. If rewind or resuming behaves incorrectly, turn it off and relaunch. See **? → Instructions → Advanced**.

### Graphics

Graphics opens on **Main**, with the existing Desktop, VR rendering, VR cockpit and VR HUD settings. **Filters** edits post-processing presets. Hover over a setting for a short explanation. Open **? → Instructions** for more detail and troubleshooting. Tabs adapt to wide and portrait windows; smaller windows scroll vertically while the action buttons remain available.

**Filters (experimental):** choose **New** or **Save as**, then edit the preset's **Day** and **Night** adjustments. Choosing a preset in the editor, creating one or importing one selects it for **both Desktop and VR**. You can still choose different presets with the two launch selectors afterward. Click **Save settings**, then relaunch to apply. **Original** preserves the track's look. The game chooses Day or Night using its effect files, not your computer's clock.

Adjust colour, bloom and exposure relative to each track: **100%** keeps its value, brightness offset **0** keeps brightness, and tint **Off** keeps its tint. **Reset adjustments** resets the displayed Day or Night variant. Main's **Bloom Off** overrides preset glow settings; VR motion blur stays disabled. Exposure controls scale the game's native values; their visual response still needs comparison testing. Headset appearance and differing presets in LAN remain unverified—select Original if you encounter problems.

Start with **Natural** for gentler colour and bloom with the image tint removed, **Vivid** for stronger colour, contrast and bloom, or **Muted** for reduced saturation, contrast and bloom. Each includes Day and Night adjustments; gamma and exposure remain unchanged. These are starting points relative to each track's look, not a guarantee of colour accuracy. **Original** remains the default and existing selections are kept. Use **Save as** to customize a built-in preset.

Presets are shared between installations on this Windows account. **Save settings** keeps adjustments and launch selections. Changing the edited preset offers Save, Discard or Cancel. Use **Rename**, **Delete**, **Import** and **Export** to manage your library; choose and save another preset before deleting one used for launching. Built-in presets are read-only; importing an exported built-in creates an editable copy. Import/export uses versioned **DiRT2VR preset JSON**, not game XML or Assetto Corsa ppfilters. A missing or invalid selected preset blocks launch with an explanation. Original effect files are restored after play; **Restore original files** recovers an interrupted session without discarding external edits.

**Bloom** adds a soft glow around bright areas. Desktop and VR rendering have separate Bloom toggles, both **On** by default. On keeps the game's existing effects; Off removes bloom without disabling colour grading, exposure adjustment or shadows. Save and relaunch to apply. This covers Normal Launch, Practice, Race, custom tracks and multiplayer HOST/JOIN. VR's choice also applies to its flat virtual screen. Original effect files are restored after play. Bloom does not override a custom track's separate lighting choice; for example, Buttermilk's lower exposure stays in effect.

**Borderless fullscreen** is off by default. Enable it to fill the primary monitor at its current desktop resolution without window borders. It applies to desktop Normal Launch, Practice, Race and multiplayer HOST/JOIN, including desktop quick launch. The separate **VSync** toggle defaults to **On** for desktop play, with or without borderless. Turn it off for uncapped rendering. Both settings are temporary and the original display settings are restored after play. Windows resolution/refresh rate and VR launches stay unchanged. Save and relaunch to apply; Restore defaults turns it off.

Borderless mode uses the game's windowed rendering path as a workaround for its exclusive-fullscreen frame-rate limitation. Driver limits, desktop composition and performance can still affect FPS; a frame rate above 60 is not guaranteed. Native desktop resolution may cost more GPU time than your previous game resolution. If Windows rejects the window change, the launcher reports a warning and leaves ordinary windowed play available. Alt+Tab and minimization remain available.

The following settings apply to VR:

| Setting | Default | Effect |
|---|---|---|
| Render resolution | 100% | Requests 1600 × 1200 per eye at full field of view. Adjust from 50–300%, with a green notch marking the recommended 150%. The default remains 100%; 300% requests 4800 × 3600 per eye (nine times the baseline pixels). Graphics also shows the measured size from the last VR launch. |
| Headset texture scale | 100% | Size of the finished image sent to your headset, not car/road texture quality. Lower values save memory but can blur the view. 100% uses your VR runtime's recommended size; 50% halves width and height (one quarter as many pixels). It cannot add detail the game did not render. |
| Anti-aliasing (MSAA) | 2× | VR-only: Off, 2×, 4× or 8×. High values cost memory and may cause crashes; the launcher warns at 4× and 8×. Desktop MSAA is restored after play. |
| Field of view | 100% | Full view. Experimental 70–99% settings crop the periphery and reduce the scene resolution proportionally. |
| Car mirrors | Game setting | Optionally force mirrors on or off during VR sessions. |
| Tree detail | Game | Choose the game's Ultra low–Ultra vegetation detail presets. Higher values keep detailed vegetation farther away. |
| Object detail | Game | Choose the game's Ultra low–Ultra trackside-object detail presets. Higher values cost performance. |
| Follow view | Off | Keep the cockpit HUD fixed relative to the car at the selected distance. Enable to have it follow your head instead. |
| Distance (VR HUD) | 1 m | Move the cockpit HUD between 1–20 metres in 0.5 m steps. Its apparent size stays constant. Applies to fixed and follow-view modes. |
| Show HUD areas | Gauges off; others on | Show or hide gauges, lap/time, race position, route map and stage progress in the cockpit HUD. |

**Road sheen (Experimental)** under **Graphics → Main** has separate Desktop and VR controls. Leave **Use track default** checked to preserve the installed track look. For Nordschleife and Misty Loch, uncheck it and choose 0–100%: 0% is matte, 10–25% gives a milder highlight, and 100% uses the full-strength Nordschleife reflection response. Misty Loch retains its mountain sun occlusion. Older Misty Loch builds need a one-time rebuild; later slider changes only require saving settings and relaunching. The selected track or replay must support this control. Other tracks, cars, glass and water are unchanged. Original track files are restored after play; Graphics → Restore defaults also returns both controls to Use track default.

**Shadow distance** under **Graphics → Main → Desktop** offers **Game** (default) and **160–640 m** in **80 m** steps. Longer ranges extend distant shadows while preserving the near shadow cascade. Overrides require DX11 and shadows enabled in the game. Start with **160 m**; higher distances can reduce distant sharpness and performance. Hover over the setting for a short explanation. Check **Also apply in VR** to use the same outer range in VR; it starts unchecked and requires **VR rendering → Shadows (experimental)** to be on. **Game** keeps the native outer range in both modes. VR **Detailed distance** still controls nearby detail separately. Save and relaunch to apply.

**Anisotropic filtering** under **Graphics → Main** has independent Desktop and VR choices: **Game**, **Off**, **2×**, **4×**, **8×** and **16×**. Game is the default and preserves native filtering. Higher levels aim to keep repeating road and scenery textures clearer at shallow angles; Off uses ordinary trilinear filtering for these surfaces. Point sampling, shadow-comparison sampling and clamped screen-space effects remain unchanged. Save and relaunch to apply. Overrides require DX11 and apply to menus, driving and replays in that launch mode, including VR's flat screen. No track rebuild or driver-profile change is needed. Appearance and performance still need in-game acceptance.

Numeric settings use sliders with the current value beside them, including AI opponents and circuit laps. Drag a slider or use the arrow keys for one-step adjustments. These percentages scale width and height, not total pixels: 80% render resolution uses approximately 64% of the baseline pixels. **Restore defaults** restores the VR baseline, turns both Bloom toggles and desktop VSync on and borderless off; save afterward.

VR render resolution now sets the game scene dimensions even when they exceed the desktop size. At full field of view, 100% is 1600 × 1200, 150% is 2400 × 1800 and 200% is 3200 × 2400 per eye. Check the measured game resolution in Graphics after a VR launch. Headset texture scale is a separate output limit: a low value can still reduce visible detail. Higher scene resolutions also increase GPU and memory use.

For vegetation and object pop-in, try **Tree detail → Ultra** and **Object detail → Ultra**, save, then relaunch VR. **Game** keeps your existing game settings. These overrides are restored after play. VR extends short terrain and trackside-object draw distances and keeps nearby opponents detailed in every direction. Distant cars still use normal detail reduction. VR also extends grass range, allows more grass to be drawn in dense areas, and keeps trees and bushes detailed farther away. The corrected grass coverage passed a player headset check at Novigrad's off-road crest, including chase-camera zoom. VR also keeps trackside props such as tire stacks, flags and scaffolding detailed farther away, with room to draw more of them. User testing in 0.17.23 confirmed that the reported Hammada Circuit pop-in appears resolved. Performance impact has not been measured. Authored visibility and individual model transitions can still cause pop-in. Compare performance on the same section of track before keeping higher settings.

Refresh rate is controlled by **your headset software**. The launcher shows the rate reported at the last launch when available, clearly marked as a past reading. The desktop game's refresh setting does not set headset Hz. Lower resolution may help GPU performance, but a particular frame rate is not guaranteed.

**Graphics → Main → VR rendering → Shadows (experimental)** starts off. On enables VR shadows, including the fix for missing trackside shadows in cockpit view. Choose **Game**, **Low**, **Medium** or **High** shadow quality; Game retains your saved quality, while High is sharper but costs more performance. Off removes shadows, like the game's Ultra low option. **Detailed distance (experimental)** defaults to the recommended **14 metres**. You can increase it up to 60 metres to extend detailed road shadows, but longer distances can cause rapid shadow flickering on walls and scenery. Return to 14 metres if this happens. To extend the outer shadow range too, choose a **Desktop → Shadow distance** and check **Also apply in VR**. Save and relaunch VR to apply. Desktop play is unchanged, and original graphics settings are restored after play.

The cockpit HUD is a transparent panel showing the game's race information. **VR HUD → Distance** changes its depth without shrinking the text. **Recenter** places the fixed HUD ahead of your seated position at that distance. Save and relaunch VR to apply. **Follow view** is off by default; Restore defaults also resets distance to 1 m. Pause menus still use their separate virtual screen. Desktop HUD capture and automated rendering checks pass, but headset placement and distance changes still need testing.

Under **Follow view**, uncheck the HUD areas you want hidden. These controls mask areas of the standard race HUD, so another overlay in the same area is hidden too. They do not change menus, the virtual screen, desktop play or the centre of the HUD. Save and relaunch to apply. Restore defaults hides the speedometer/gear/revs area and shows the other areas. Existing saved choices are preserved on upgrade. Alternative HUD layouts and headset use still need validation.

Field-of-view cropping keeps a narrower cockpit view rather than stretching the full image. Menus use the virtual screen by default, although reducing render resolution also lowers their image detail. Cropping and nondefault graphics values have automated coverage but still await an in-headset check. Keep the defaults for the established setup.

### Controls

**Controls → General → Cockpit animation → Remove artificial steering corrections (Experimental)** is off by default. It removes the reported extra cockpit wheel judder on desktop and in VR while retaining the game's steering filter and original animation range. Handling and force feedback are unchanged. Desktop requires DX11; select cockpit view yourself. Save settings and relaunch to apply. The correction has passed desktop and headset checks, but compatibility with every wheel and setup is not established. It does not provide full 540°/900° wheel matching; the earlier missing-hands and static-wheel report remains unresolved.

For a comparison, use the same car and event for three short runs: main option **off** (original behavior); main option **on** with **Observe only — no correction** checked (hooks active, original animation values retained); then Observe only **unchecked** (correction applied). Enable **Settings → Enable diagnostic logging** for the last two runs. Drive in cockpit view, hold the wheel centred, turn slowly both ways, then drive over rough ground. The log records a bounded set of steering values, wheel-animation positions and hand-blend weights. Observe only does not enable logging by itself.

| Default | Action |
|---|---|
| **F9** | Toggle virtual screen / cockpit VR; also recenter |
| **F10** | Recenter |
| Normal game controls | Driving and menu navigation |

Controls has **General**, **VR**, **Driving** and **Seat** subtabs. General contains cockpit animation and chase-camera settings. VR contains Toggle VR and Recenter; Seat contains the seat panel shortcut and optional individual bindings. The binding rows have **Action**, **Keyboard** and **Controller / wheel** columns. Click an action's keyboard binding to change it, optionally with Ctrl, Alt or Shift. Escape or switching tabs cancels capture without discarding other edits.

**Chase-camera free look (experimental):** enable **Controls → General → Chase camera → Free look**, save settings and relaunch, then select a chase view using the game's camera control. Free look starts **off**. Desktop play requires DX11. For VR driving, enable **Advanced → Graphics → 3D beyond the cockpit**; for replay 3D, use the separate **3D chase replays** switch. Desktop controls and VR chase-replay orbit/zoom have passed player checks.

- Move the **right stick** to orbit around your car, or use the mouse. Choose **Hold left**, **Hold right** (default) or **Always on** with the mouse-icon buttons.
- Hold the **right stick pressed in (R3)** to look behind. Release it to return to the normal chase angle, even while stopped.
- Scroll the **mouse wheel forward** to move closer or **backward** to move farther away. No mouse button is needed. **Controls → General → Chase camera → Maximum zoom out** sets the farthest distance from 180% to 1000% of normal, in 10% steps. The default is the original 180%; try 500% or 750% for a wider view. The closest distance stays at 60%. The same limit applies to replay stick/key zoom. Save and relaunch to apply. Zoom resets when changing chase views or starting a new session; field of view stays unchanged and obstructions can keep the camera closer.
- Adjust mouse/stick sensitivity and **Invert vertical** to suit your controls.

Your angle stays while stopped. When moving, the camera smoothly returns behind the car after you stop looking around. To keep your chosen angle relative to the car while driving or in chase replays, check **Keep camera angle** beside **Free look**, then save settings and relaunch. This option starts off; R3 look back still returns to the normal chase angle when released. Pause, menus and switching apps stop mouse look. The Windows pointer is hidden over the game picture while free look is active and returns for menus, other camera views and window borders.

**VR shortcuts:**

Controller shortcuts start unassigned. Choose **Bind…** in the desired action's row, press one button or two buttons together on the same device, then release them. Multiple devices may be assigned to each action. Select an assignment and choose **Remove selected** to clear it. The launcher rejects overlapping assignments. Choose **Save settings**; changes apply on the next launch.

**Assigned controller buttons still perform their normal game actions.** Choose buttons or combinations that avoid unwanted driving/menu actions. Shortcuts fire once per press and require release before firing again. Keep the game window focused.

Assignments stay attached to the selected device. A disconnected device is not replaced automatically; Xbox controller slot changes may require rebinding. Wheel shortcuts use the same non-exclusive DirectInput reader as driving bindings. Connect the wheel, choose Bind, then press and release a button or pair. Already-held switches are ignored. Existing HID and Xbox assignments remain supported. This updated VR shortcut path still needs physical Fanatec validation.

**Driving controls:** Direct practice and Race now load the existing saved profile before the event, including its control settings. Save your profile after configuring controls in the game. No additional career save is created. Direct launches check sign-in before loading: accounts already signed in continue straight to the event; otherwise, use the game's GFWL sign-in prompt. Canceling sign-in or a reported career-load error ends the launch without creating a replacement save. If no saved career exists, the event uses the game's defaults. Fanatec pedal validation is pending.

For optional launcher assignments, open **Controls → Driving → Configure driving controls…**. For Xbox controllers, **Xbox preset** applies left-stick steering, RT accelerator, LT brake, A handbrake and B/X shifting, plus Start to pause/start, A to confirm, B to go back and D-pad menu navigation. Missing keyboard assignments are filled with the game's default keys; your custom keys are kept. If an earlier preset left keyboard controls blank, apply **Xbox preset** again. Choose **Save driving controls**, then launch again.

For a custom controller or wheel, bind **Pause** and **Menu Start Button** to your Start button. **Menu Select** confirms, **Menu Back** cancels, and **Menu Up/Down/Left/Right** navigate. You can also bind keyboard Escape; during keyboard capture, use the **Cancel** button to leave without assigning it.

**Binding wizard…** walks through the actions in order, automatically listening to all connected controllers. Center the wheel/stick, release pedals and handbrake, and wait for **Ready**. Move the requested control, hold an axis briefly, then return to rest; the wizard advances automatically. A nonzero resting handbrake axis or held switch does not count as a new input. Choose a specific device or **Axis only / Button only** if necessary. Steering, pedals and shifters may use separate devices. For keyboard assignments, select **Keyboard** in the wizard. **Skip** keeps the current assignment; **Back** revisits a step; **Cancel** discards the wizard's changes. Review, **Apply bindings**, then **Save driving controls**. Applying bindings enables launcher overrides.

During capture, turn the wheel clearly left/right and press pedals fully. Small pedal movements are ignored; these capture thresholds do not change driving sensitivity.

To change just one action, select it and choose **Bind keyboard…** or **Bind device…**. **Calibration…** adjusts direction, dead zone and saturation; saved values are shown beside each controller assignment. Enabled launcher assignments and calibration reapply on every launch, replacing in-game changes for those actions. To retain zero steering deadzone, set 0% for both **Steer Left** and **Steer Right**, then **Save driving controls**. Alternatively, disable launcher driving bindings and use the game's saved controls. The editor warns about identical left/right steering directions and inverted Xbox triggers; repair old assignments with the wizard or Xbox preset. Physical Fanatec handbrake validation remains pending.

**Live device input** shows the selected controller, wheel or pedals while you move them, including axes, buttons and POV hats. Choose **Refresh** after connecting a device. These are raw readings before calibration; a resting pedal at one end of its range is normal. Viewing inputs does not change bindings.

The editor includes steering, accelerator, brake, clutch, handbrake, sequential shifts and H-pattern gears. Select the appropriate transmission/assist settings in the game as well. **Use sequential and H-pattern shifts together** is an experimental option for manual transmission: the most recently used shift button or moved shifter controls the gear, so a parked H-shifter does not undo a sequential shift. Stock cars still require manual transmission selected in-game. Native H-pattern bindings cover gears 1–6 and reverse; use sequential shifts for seventh. Hardware acceptance is pending. It applies to Normal Launch, Practice, Race and HOST/JOIN, in desktop and VR modes, using the DX11 renderer. Unassigned actions retain game settings; assigning an action replaces that action's saved inputs, so assign both keyboard and controller inputs if wanted. **Use game binding** removes that action's launcher override. Disabling overrides does not undo bindings subsequently saved by the game.

Driving capture uses XInput for Xbox controllers and non-exclusive DirectInput for wheels/pedals. Wheel models and the combined LAN/VR binding path still need hardware acceptance. The game identifies DirectInput devices by product name; two identical models cannot be distinguished reliably. POV/hat capture and force-feedback tuning are not included.

## Recovery, upgrades and removal

### Help and updates

Click **?** at the top right of the launcher (or press **F1**) for **Help / About**. It shows the installed version and build, credits developer **Bohloney**, and links to instructions, GitHub issues and releases.

The launcher checks GitHub Releases once in the background when its window opens. When a newer eligible version exists, **New version available** appears beside **?**; click it to review the update. Stable releases are always eligible. Experimental releases are included only when **Include experimental releases** is enabled. Offline checks stay quiet and never block launching. Quick launch and background game sessions do not check for updates.

Choose **Check for updates** in Help / About to retry manually; no GitHub account is required. Downloads and installation always require your action.

When a newer release has a verified installer, **Download and install** downloads it, checks its SHA-256 checksum and opens setup for your current game folder. Close the game first. The launcher restores pending changes before handing over to setup, then closes. Complete the normal setup prompts, including Windows administrator approval if requested. Preferences are retained. This is an assisted update, not a silent background installation. ZIP users can use it too; doing so adds the installer and uninstaller to that installation.

The updater shows separate downloading, verification, recovery and setup-opening messages. Closing Help / About before the installer handoff cancels setup. If an older launcher freezes after downloading, close it and run the latest **Setup.exe** directly from [Releases](https://github.com/preseznik/DiRT2VR/releases). Choose the same game folder and keep existing backups; there is no need to uninstall first.

**Include experimental releases** in **? → About** is off by default and saved immediately for this installation. It controls both automatic notices and manual checks. Turn it on to include newer experimental builds; normal releases remain eligible. Experimental offers are labelled clearly. Switching it off does not change the installed version.

Experimental builds show **Return to stable…**. This finds the latest normal release, which may be older, and asks before installing it. The game must be closed. Returning to stable disables experimental updates, keeps settings and bindings, and saves a preference backup under the installation's user-data folder in `updates/rollback-preferences`. Your career is not reset. A newer normal release is also offered through ordinary update checks.

Updates require a published [GitHub Release](https://github.com/preseznik/DiRT2VR/releases) containing the packaged installer. Source commits and GitHub's source-code ZIPs are not installable updates. If checking or downloading fails, your current installation remains available; retry later or use the Releases link.

### Large Address Aware (Experimental)

**Advanced → Memory → Large Address Aware (LAA)** lets DiRT 2 use up to 4 GB of address space on 64-bit Windows. It may help crashes when loading another race; it does not increase VRAM or fix every crash. It is off by default. Save settings and launch again to apply to desktop or VR.

The launcher backs up the supported executable, enables LAA for the session and restores the original afterward. After an interrupted session, close the game and use **Restore original files**. An exact LAA patch installed separately is left unchanged. Compatibility with every setup is not established; turn the option off if it causes problems.

### Restore or remove

VR sessions temporarily adjust camera assets, motion-blur asset and selected graphics settings. Direct practice and Race also temporarily adjust the game's menu definitions and create `DiRT2VR/p.xml`. Originals and recovery journals are saved before changes. Normal exits and detected game crashes trigger restoration; Return to menus restores the direct-session files before reopening the game. Unrelated graphics-settings edits are retained. An existing `DiRT2VR/p.xml` is preserved and blocks preparation.

After a power failure or forced session-manager termination, close any remaining DiRT 2 processes and choose **Restore original files**. A new launch also checks for pending recovery. Do not delete `DiRT2VR/backups` or the corresponding AppData folder while recovery is pending.

If recovery reports a conflict, it preserves unexpected asset edits and backups instead of overwriting them. Keep the files and report the error. Recovery must finish before upgrading or uninstalling. Close the launcher and game before replacing package files.

**Installer upgrade/removal:** run the new installer to upgrade, or use Windows Installed apps / the DiRT2VR uninstaller to remove it. Preferences, logs and retained backup records are preserved.

**ZIP upgrade:** restore original files, close the launcher, then extract the new package over the existing mod files. Keep the backups and `installation.json` receipt. The next launch updates only a recognized proxy.

**ZIP removal:** with the game closed, run `DiRT2VR.exe --remove-proxy` from the game folder. After it succeeds, remove `DiRT2VR.exe`, `Start-DiRT2VR.cmd` and the `DiRT2VR` subfolder. Do not remove game files. AppData preferences/logs may be retained or removed separately once recovery is complete.

## LAN multiplayer

LAN support is included in the launcher package; no separate test kit or PowerShell window is needed. Both PCs need the supported game and the current launcher package on the same LAN.

1. LAN uses the career selected on **Profile**. **Current game career** uses your usual save in `Documents\My Games\DiRT2`, without importing or copying it. You can also choose a separate launcher-created career.
2. Optionally enable **Skip introduction for LAN multiplayer**. It defaults off and skips the opening first-race movie and forced career tutorial while retaining profile creation.
3. Open the launcher's **Multiplayer** tab. On the host PC, click **HOST**, then choose **Desktop** or **VR** (or **Cancel**). Create a session through the game's **Multiplayer / LAN** menus. Select the event, track and cars inside the game.
4. On the other PC, select the host in the LAN server list and click **JOIN**, then choose **Desktop** or **VR**. Discovery refreshes automatically while this tab is open; **Refresh** starts another scan. JOIN opens LAN play and adds the selected PC to the game's network peers. Complete any startup/profile prompts, then finish joining through the game's **Multiplayer / LAN** menu. It does not enter the lobby automatically.
5. Quit normally after playing. The background launcher restores the original `xlive.dll`, even if you closed its settings window.

HOST and JOIN ask for the display mode every time. Desktop requires no headset. For VR, start your selected headset software and connect your headset first; the launcher uses your saved Graphics and Controls settings. Menus use the virtual screen unless 3D menus is enabled; races start in cockpit VR automatically. Use Recenter when seated. Multiplayer headset acceptance is pending. VR graphics and asset changes are restored alongside the LAN DLL after play.

The game reads and saves the same career and graphics settings in both modes. Use the game's **Save Profile** action to explicitly save changes before quitting; the launcher does not force an autosave. LAN connection settings and this PC's network identity live under `%LOCALAPPDATA%\DiRT2VR\<installation-id>\lan`; do not copy that folder between players. The launcher does not copy, move or rewrite career files.

**Skip startup logo movies (single-player launches)** is a separate Settings checkbox, off by default. It skips the Codemasters, Intel, AMD and EGO logo movies while retaining the legal screen, attract video and other cinematics. It temporarily changes the supported startup definitions and restores them after play. HOST and JOIN retain the logo movies in both Desktop and VR: changing these definitions fails the game's race-loading checks. Intro skipping still controls the LAN first-run movie/tutorial independently. Turning either setting off does not undo saved progress.

If LAN reports altered `system\states.bin`, close the game and use **Restore original files**. DiRT2VR preserves unexpected external changes; restore the original file from your game installation if another mod changed it. On older launcher versions, turn off **Skip startup logo movies** before HOST/JOIN.

The browser lists PCs launched using **HOST** while their game is running. **HOST game running** does not confirm that a lobby is open; its player count is unavailable and shown as a dash. Create the lobby inside the game before the other player joins. Both PCs need version 0.6.3 or later with the same discovery protocol. Older kits and unmodified games are not listed. Entries disappear when the host game exits; stale entries cannot be joined. Discovery uses UDP 39820 on private IPv4 LANs; guest Wi-Fi/client isolation or firewall rules may block it. No public internet scanning or port forwarding is used.

**Multiplayer VR is available but untested.** Launcher-side event setup and direct creation of a game lobby remain unfinished; HOST opens the game menus. Launcher discovery and JOIN have reached a lobby on two PCs. The Battersea race-loading disconnect was traced to modified startup movie definitions; with logo skipping disabled, both players drove, finished the race and reached results. Current LAN launches automatically keep those definitions intact. Install the same current package on both PCs. Broader event coverage and repeat races still need testing. A manually saved profile change persisted from LAN into Normal Launch.

For a LAN disconnect report, enable **Settings → Enable diagnostic logging** on both PCs, save, and repeat the event. Quit both games, then use **Open logs** to collect `lan-network.log` and, if present, `lan-network.previous.log` from each PC's newest session folder. These contain network addresses and transport metadata, not packet payloads or save data. LAN traces are limited to 8 MiB per session; turn logging off after the test. Older session folders are kept until you remove them.

After a crash or power failure, close any remaining game processes and choose **Restore original files** before ordinary play. The launcher also recovers interrupted sessions from the older LAN kits. Keep `DiRT2VR/lan-backups`; conflicting edits are preserved and reported. If Windows asks about network access, allow DiRT 2 on your Private/home network rather than disabling the firewall.

Older test-kit profiles and imported copies are not listed in the Profile tab. They are left on disk unchanged. Current game career remains the default until you choose another profile.

## Limitations and troubleshooting

- Scenery visibility and car-aligned headlights passed the reported tests; other lighting, mirrors and interiors need testing.
- Road rubble is retained in both eyes in the tested Novigrad scene. Other stages and ground-cover variants still need testing.
- VR reduces particles and motion blur. **Graphics → Main → VR rendering → Crowds (experimental)** enables spectators using the game's saved crowd quality; it starts off. Save and relaunch to apply. Crowds add rendering work and still need broader headset testing; this does not add spectators to custom tracks that omit them. **Shadows (experimental)** enables shadows; keep them off if brightness or performance looks wrong. Desktop crowd settings are unchanged, and temporary VR graphics changes are restored after play. Water stays visible and uses per-eye reflections in VR; the Ensenada Sprint puddle fix has been checked in Quest 3. Other tracks, reflected objects when looking behind, scenery pop-in and HUD placement still need testing.
- Mizu VR, chase-camera free look in a headset, nighttime filters and different filter presets between LAN players still need wider testing. Use **Original** in Filters if a custom preset looks wrong.
- Occasional hitching remains; a steady headset frame rate is not guaranteed.
- Headset reconnection during play is unsupported. Quit and relaunch after reconnecting.
- Installation acceptance in protected folders and interruption scenarios is still in progress. Packages are experimental.

**Runtime not found:** for SteamVR, use **Browse…** beside SteamVR runtime in Settings and select `steamxr_win32.json`. For system OpenXR, configure your headset software as the active OpenXR runtime; DiRT 2 needs its 32-bit runtime and D3D11 support. Start the headset connection before launching. You can switch back to SteamVR under **Settings → VR runtime**. DiRT2VR selects the runtime for its own processes without changing Windows' global OpenXR setting.

**No VR / shortcuts do nothing:** select cockpit view, focus the game window, and use Toggle VR. Check connected-device indicators and saved bindings. Other camera views use the virtual screen unless **Advanced → Graphics → 3D beyond the cockpit** is enabled; that option supports bonnet, bumper and chase views.

**Unsupported executable or foreign proxy:** use the supported game build and resolve the reported conflict. Do not bypass compatibility checks or download replacement game executables from an untrusted source.

**Custom track/car build failed:** enable diagnostic logging in **Settings**, save, then retry the build. On failure, **Open logs** contains a dated folder with `custom-build-error.json`; its location also appears in the error details. It records the pack, source-folder path and error details, including converter error output when available. Cancelled builds do not create failure reports.

**Move built tracks or cars to another PC:** select the custom pack and choose **Manage → Export pack ZIP…** with the game closed. Track exports include all installed layouts. On the other PC, close DiRT 2 and the launcher, then extract the ZIP into the game folder containing `dirt2.exe`. Open an up-to-date launcher and Refresh. No rebuilding is needed; the destination must still meet the pack's compatibility requirements. Settings and careers are not included. See [pack portability](EXTERNAL_PACKS.md#installing-and-sharing).

**File preparation failed:** the error includes the affected file and failure details when available. Close the game and use **Restore original files**. Include `%LOCALAPPDATA%\DiRT2VR\<installation-id>\worker-error.json` in your report; it contains the latest failed operation, including any administrator retry. This small recovery diagnostic is kept even with logging off. Keep the original backups and do not change permissions across the whole game folder.

Read-only game files are supported during camera, effects, menu and offline-profile preparation. Their original protection is restored after play or interrupted-session recovery. Updates also handle read-only launcher files. If an older version failed with `cameras.xml [ReadOnly, Archive]`, update the launcher, choose **Restore original files**, then retry. If recovery reports a file in use, close the application using it first. Other access-denied errors can still require folder permission changes; the launcher reports the affected file and does not change Windows permissions.

**Report a problem:** enable diagnostic logging in **Settings**, save, and reproduce the issue. Then use **Open logs** and zip the whole relevant session folder, including `trace.log` and `dirt2vr-settings.json`. New sessions include the build, effective launcher settings, driving calibration and initial seat positions; VR traces record track/route loading destinations and player-camera cars. Loading records do not prove a race finished loading, and career series names are not captured, so still provide the series/event name, headset, steps to reproduce, and whether the problem affects cockpit VR or the virtual screen in a [GitHub issue](https://github.com/preseznik/DiRT2VR/issues). Turn logging off afterward. Logs live under `%LOCALAPPDATA%\DiRT2VR\<installation-id>\logs`. Do not upload game files or save profiles.

The launcher checks its temporary files at most once a day while idle. It removes older downloaded installers (keeping the newest and any less than a day old), abandoned custom-track build folders older than a day, and diagnostic sessions older than 30 days (always keeping the latest 10). Settings, bindings, careers, filter presets, installed tracks and recovery backups are kept. Cleanup waits while a game, update, track build or recovery is active; it never asks for administrator approval. A small `cache-cleanup.json` report is kept in the installation’s AppData folder.

See the [changelog](CHANGELOG.md).

Third-party license notices are included in `DiRT2VR/licenses` in packaged builds. This product includes software developed by Jon Skeet and Marc Gravell. Contact skeet@pobox.com, or see https://jonskeet.uk).

Matching LAN library source is preserved in the [repository](https://github.com/preseznik/DiRT2VR/tree/main/source-archives/lan). It is not needed to play. Installed license notices include its download link and checksum.

### VR seat position (Experimental)

In cockpit VR, stop the car and press **Tab**. Use **Up/Down** to raise/lower your seat and **Left/Right** to move back/forward. Hold **Shift** for sideways movement. **Enter** saves, **Escape** cancels, and **Tab** saves and closes. The race continues while the panel is open.

Xbox controllers use the D-pad, **X** as the sideways modifier, **A** to save and **B** to cancel. Assign a panel-opening button in **Controls**. Wheels can use their POV hat; assign the panel modifier/save/cancel buttons in Controls.

**Graphics → VR cockpit** also has position sliders and a per-car reset. Positions are saved separately for each car and survive recentering. Enable **Use universal seat position** for one shared position across all cars. The car picker is disabled in this mode; sliders, reset and in-game adjustments edit the shared position. Turn it off to restore your saved per-car positions. Save settings and relaunch to change modes. The seat panel is the recommended way to adjust your view. All nine optional shortcuts—six movements, Panel sideways modifier, Panel save and Panel cancel—are under **Controls → Seat → Individual seat bindings (optional)**, collapsed by default in both styles. Expand it to assign them. Fixed panel keyboard shortcuts are read-only. Extreme offsets can reveal missing interior geometry. Panel movement, save/cancel, recenter retention, relaunch persistence and universal seat mode have passed headset checks. Physical wheel navigation and broader rendering checks with adjusted seats remain unverified.

### Background play (Experimental)

**Advanced → Gameplay → Keep running when unfocused** targets automatic pauses when switching to another app, including replay playback. It is off by default; save settings and relaunch to apply it. Manual pause remains available. Use windowed or desktop borderless mode: exclusive fullscreen can still suspend rendering when minimized. Desktop replay playback and manual pause have been checked. Driving and VR background behavior still need acceptance.

### 3D beyond the cockpit (Experimental)

**Advanced → Graphics → 3D beyond the cockpit** is off by default. Save and relaunch VR, then use the game's Change camera control to try supported bonnet, bumper and chase views with stereo depth and head tracking. Toggle VR still returns to the virtual screen. Desktop play is unchanged.

Trailer menus and replay 3D have separate switches below. Loading screens, dialogs and movies stay on the virtual screen. Seat adjustments apply only in the cockpit and are retained when switching cameras. Chase views retain the game's camera movement and may be less comfortable. Leaning can expose geometry omitted from the original fixed-camera view. The external views passed the user headset check. Some camera switches briefly return to the virtual screen while the game blends between views; this remains a known limitation.

### 3D menus (Experimental)

Enable **Advanced → Graphics → 3D menus**, save and relaunch VR for stereo depth and head tracking in the interactive trailer interior and exterior. It starts **off**, independently of the driving and replay 3D options. Navigate with the usual game controls; **Toggle VR** (F9 by default) returns to the flat virtual screen. Loading screens, movies, pause screens and unsupported views remain flat. Seat adjustments and racing HUD masks do not affect the trailer.

The game's menu camera animations still move the view; disable this option if uncomfortable. Interior and exterior menus, text readability and window reflections have been checked in VDXR. Returning from a race still needs acceptance.

### Chase replays (Experimental)

Enable **Advanced → Graphics → 3D chase replays**, save and relaunch VR for stereo depth and head tracking in replay chase views. It starts off and works independently of **3D beyond the cockpit**. Cinematic replay angles stay on the flat screen; Toggle VR also returns to the flat screen.

For orbit and zoom on desktop or in VR, enable **Controls → General → Chase camera → Free look**. The same mouse activation, sensitivity and invert settings apply. Orbit and zoom work during playback and while the replay is paused. Pausing playback holds the angle rather than returning behind the car; opening the game menu or switching away suspends input.

Use the game's **replay camera next/previous** controls to choose a chase view or return to cinematic angles (default **F8/F7**, or Xbox **RB/LB**). These are the replay controls, not the driving Change camera action. Choose **Full Replay** after finishing Direct Practice or Race, or play a saved recording from **Launcher → Replays**. Only chase replay cameras support 3D; cinematic angles remain on the flat screen.

### Replay controls

Open **Controls → Replay → Configure replay controls** to bind playback, camera changes, flashback resume and chase zoom. In chase replays, the Xbox left stick moves up to zoom in and down to zoom out; Page Up/Page Down and the mouse wheel also work. Right-stick orbit is unchanged. Enable **Controls → General → Chase camera → Free look** for orbit and zoom. Changes apply on the next launch. Custom zoom supports keyboard and Xbox inputs; native playback controls also support wheels. These zoom bindings do not affect driving.
