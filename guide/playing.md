# Playing DiRT2VR

[← Back to README](../README.md)

This guide covers the **Launcher**, **Profile** and **Replays** tabs: launch modes, Direct practice, Race, car choices, saved replays, careers and CUSTOM cars. For building tracks, see [Custom tracks](custom-tracks.md).

## Launching

**Launch** starts regular desktop play; **Launch VR** starts headset play. Both use the selected Normal Launch, Direct practice or Race mode. The launch buttons stay on the left; Save settings, Restore original files and Open logs stay on the right.

A small progress window shows the current startup step and elapsed time until the game window appears. **Hide**, Escape or closing this window dismisses it while startup continues. Runtime selection and VR compatibility prompts still need your response. Once the game opens, its own loading screens take over.

The launcher stays visible during preparation. For desktop play, it minimizes when the game window appears. Reopen it from the taskbar when needed; the background session manager continues running.

DiRT 2 may briefly fall behind other windows as it replaces its startup window. The launcher hands focus to the replacement once; the brief drop followed by immediate return has been confirmed on desktop.

Regular Launch needs no SteamVR or headset and uses your desktop graphics preferences. Desktop Direct practice and Race require DX11 and temporarily enable human control, the selected race and the direct-session menus. VR shortcuts and the VR rendering/HUD settings apply only to Launch VR. The step-by-step first VR launch is in the [README](../README.md#play).

To switch from VR to desktop, quit the game and choose **Launch** in DiRT2VR. This uses your saved desktop graphics and filter choices. Launching the game outside DiRT2VR uses the original game settings.

### Quick launch

For quick launch with saved settings, use **Start-DiRT2VR.cmd**. It runs the same session manager without opening the settings window. Its equivalent command is `DiRT2VR.exe --launch --no-ui`.

For desktop quick launch, use `DiRT2VR.exe --launch --desktop --no-ui`. The existing `Start-DiRT2VR.cmd` continues to launch VR.

## Cars, grades and difficulty

**Race difficulty** controls AI opponents in stock and CUSTOM-track Race launches. Choose Easy, Casual, Serious, Savage, Extreme or Hardcore; **Use game setting** leaves the game’s saved choice in effect. Each custom location remembers its own choice. This does not change career difficulty in Normal Launch or multiplayer.

Choose **Rookie**, **Pro** or **All-Star** beside the car picker for stock cars in Direct practice and Race, on desktop or in VR. The selected grade applies to your car and every AI opponent, including mixed grids and staggered rally races. Rookie is the default. CUSTOM cars retain their own specification. Save settings or launch to keep your choice; saved replays retain their recorded grade. Normal Launch, LAN and career upgrades use the game's own selections.

Use **Class** above either car picker to show **All** installed stock cars or just Rally, Rallycross, Trailblazer, Raid T1, Stock Baja, Class 1 Buggies or Trophy Trucks. If the current car is outside the chosen class, the first matching car is selected. The grade stays unchanged. The filter starts at All when reopening the launcher and is disabled for CUSTOM cars; it does not restrict Mixed opponents.

Stock-car pickers and per-car VR seat settings also include the prize cars: MG Metro 6R4, Dallenbach Special, Ford RS200 Evolution, Mitsubishi Pajero Dakar 1993, Toyota Stadium Truck, 1995 Subaru Impreza WRX STI and Dodge Power Wagon. Direct practice and Race can use these installed cars without unlocking them in career. This does not change your garage or career progress.

### Vehicle setup

**Vehicle setup…** opens the six game-style adjustments: gear ratio, downforce, suspension, ride height, differential and brake bias. Enable **Use this setup for direct launches**, choose one of five positions on each bar, then **Apply**. Save settings or launch to keep your choice. **Reset to centre** returns all six bars to the middle; uncheck the option to use the game’s setup. The same setup applies to your car in stock and CUSTOM Direct practice and Race, on desktop or in VR. Normal Launch, opponents and career saves are unchanged.

## Direct practice (experimental)

On **Launcher**, choose **Direct practice**, an **Event** category, a **Track**, and a **Car**, then **Launch VR**. Event filters the track list by discipline; this is solo practice, not a career event. The launcher lists installed routes and cars from the supported game's catalog.

Below the car, the launcher shows your **best lap** for the selected track, or **best time** on point-to-point routes, with its car and grade. Every completed Direct practice lap counts, on stock, CUSTOM and External tracks. Next to it is the game's **world record** for your car's class. Converted CUSTOM tracks have no world records, and External tracks show one only if their pack includes it. Race results stay in the game. The game's in-race HUD does not show the world record during practice.

Direct practice bypasses the trailer menus and loads a player-driven car. Launch VR selects the cockpit and enables VR automatically; regular Launch retains your desktop camera. At the finish, choose **Full Replay** to watch the completed run from the start. Use **F1** to pause/resume playback. **Esc** or controller **B** opens the replay pause menu; press it again or choose **Continue** to resume from the same point. Choose **Full Replay** to start over, or **Exit replay** to return to the finish menu. Playback also returns there when it ends. You can watch again until restarting or leaving the event. Full Replay is unavailable if the game could not keep a complete recording. In VR, the existing **3D chase replays** option applies. The driving pause menu provides Continue, Restart, **Next event**, Return to menus and **Quit**. Quit is also available in the finish menu and while a replay is paused; it exits the game without reopening it, and the launcher restores session files afterward. Use **Up/Down** to select these vertical menu choices. **Return to menus closes the game and automatically reopens Normal Launch**, keeping Desktop or VR mode; expect another loading sequence. Your saved launcher selections stay unchanged. Alt+F4 quits without reopening. Saved practice selections also work with `Start-DiRT2VR.cmd`.

**Next event (experimental)** in the pause and finish menus opens a panel for your next Direct event without leaving the game: choose **Practice** or **Race**, the discipline, track and car, and for a race the number of opponents and laps on circuits. Use **Up/Down** to choose a row and **Left/Right** to change it (hold to scroll), or the D-pad on a controller. **Enter** or **A** on **Start event** closes the game and starts the chosen event straight away, with no trip through the game menus; expect a normal loading sequence. **Esc**, **B** or **Cancel** returns to the menu. On desktop the panel appears over the menu; in VR it floats in front of you. The car and the menu ignore the panel's keys and buttons. The panel lists the stock tracks and cars the launcher offers; use the launcher for the prototype track, External or CUSTOM content and saved replays. Car grade, opponent cars, difficulty and race start stay as set in the launcher; a staggered start becomes Automatic on tracks that can't stagger. The launcher keeps its own selection for the next time you launch.

## Race (experimental)

Choose **Race**, an event, track and car, then set **AI opponents** from 1 to 7. Use **Launch** for desktop or **Launch VR** for headset play. Choose **Opponent cars**:

- **Same as driver:** all opponents use your model.
- **Mixed:** other models from all installed classes, regardless of event.
- **Same class:** other models in your car's game-defined class.

**Race start** defaults to **Automatic**: stock Rally and Trailblazer stages use **Rally (staggered)**, with separate starts and elapsed-time standings. Other tracks use **Grid race**, starting all cars together. Select **Grid race** to keep simultaneous racing on a stock rally stage. An explicit staggered choice blocks incompatible tracks; choose Automatic or Grid race to continue. External custom tracks can offer **Rally (staggered)**, **Grid race**, or both when their pack supplies the matching starting positions and timing. Each format has its own opponent limit. Saved replays remember their original start format. The desktop Spillway Short start, timing, finish menu, Full Replay and Restart passed a player check; other stages and VR still need acceptance.

Mixed/class grids are randomized each launch, repeating models if the available pool is small. If no other eligible model is installed, they use your model. Direct practice remains solo regardless of saved opponent settings. Start with Landrush or Rallycross. Other disciplines and track/car combinations need testing.

AI opponents use distinct stock driver identities; their names do not depend on the opponent-car selection.

Use **Laps (circuits)** to choose 1–20 laps in either **Race** or **Direct practice**. On point-to-point stages this control is disabled and the session is one stage run; your saved circuit lap preference is retained. The three-lap HUD and continuation into lap 2 have been confirmed on desktop at Baja – Ensenada Sprint. Other counts, complete multi-lap finishes and VR still need testing.

Race uses the same finish and pause choices as practice. It stops at the finish menu rather than automatically repeating. Return to menus restarts the game in Normal Launch; this return path has been confirmed on desktop, while headset validation is pending. **Alt+F4 quits.** Custom races do not award career progress or use the normal results flow; use Normal Launch for full career events. Desktop player control and seven AI opponents have been confirmed at Baja. Mixed opponent models also passed a desktop race check. Same-class gameplay and crowded-grid VR performance still need testing.

## Saved replays (Experimental)

In **Launcher → Replays**, switch **Record full replays** to **ON** before launching Direct practice or Race, including CUSTOM tracks that support Full Replay. It starts **OFF**, including after updating. The choice saves immediately and applies on the next launch. OFF skips full-race recording and saving; instant replay, rewind and playback of existing saved replays remain available. Normal Launch retains the game's own replay behavior.

With recording on, **Full Replay** is available in the finish menu and **Save last replay** keeps the latest completed run. Turn on **Auto-save completed runs** to keep every completed attempt; it starts off and is unavailable while recording is off. Unfinished runs cannot be saved.

Choose a recording and select **Play** or **Play in VR**. The launcher checks the recorded cars and track before starting; missing or incompatible content is reported without changing game files. Replay controls are the same as Full Replay, including Esc/Continue and chase orbit/zoom. VR chase views use **Advanced → Graphics → 3D chase replays**; cinematic angles stay on the virtual screen. See [Controls → Replay controls](controls.md#replay-controls) and [Graphics → Chase replays](graphics.md#chase-replays-experimental).

Recordings are compressed automatically and unpacked temporarily for playback. **Open folder** opens this installation's `Replays` folder in your local DiRT2VR user-data directory. Tested recordings of about one minute took 43–58 MB at high frame rates; size varies with frame rate, duration and the race. Auto-save has no storage limit, so use **Delete…** to remove recordings you no longer want. When auto-save is off, each completed run replaces the unsaved last replay; recordings already saved in the library remain.

This first experimental player supports recordings made by the same installation. Replay sharing/import and recordings from Normal Launch are not supported yet. Careers are unchanged.

Some older recordings lack data needed to restore particle effects in a new game process. If playback reports an unsupported native record and closes, record a new run with the updated launcher; the missing information cannot be recovered from that recording.

## Profiles (Experimental)

Open **Profile** to choose **Current game career** or create a separate career with **Create new profile…**. Choose **Fresh career** or **100% completed career**, enter a name, and the new profile becomes active automatically. The active row is green and marked **Active**. To switch to an existing career, select it and choose **Use this profile**. Names must contain 1–24 letters (A–Z) or numbers (0–9), without spaces or symbols. Existing profile names are left unchanged. No game setup session is needed. Close DiRT 2 before creating or switching profiles; selection saves immediately.

The selected career is used for Normal Launch, Practice, Race and LAN, in desktop or VR, and remains selected when returning to menus. Completed careers include stock career wins, cars, liveries, rewards, All-Star upgrades and teammate relationships. They do not include platform achievements, invented personal-best times or external content. Existing careers cannot be unlocked by this feature.

Your original saves stay in their current location. New profiles have separate save folders under `%LOCALAPPDATA%\DiRT2VR\profiles`, shared by your DiRT2VR installations on this Windows account. Graphics settings and launcher bindings remain shared. Launching the game outside DiRT2VR uses your original career.

**Delete profile…** permanently removes a launcher-created career and its saves after confirmation. First select another career for launching. Current game career cannot be deleted here. Profiles are shared between installations on this Windows account.

Use **Refresh** after saving in-game. Unreadable details show **Unavailable**; intermediate completion percentages and garage counts are not yet supported. If the original game has multiple careers, choose between them in its Load Profile menu. A missing or incompatible selected profile blocks launch until you explicitly choose a valid one.

Single-word launcher profile names also appear on screens which normally show a surname. Existing profiles receive this display correction automatically, without renaming or rewriting their saves.

## CUSTOM cars (Experimental)

**CUSTOM cars (Experimental)** beside either Car selector lists supported local conversions. The first profile is **Ferrari F2004** from your installed Assetto Corsa. Choose **Build/import…** to generate its body, cockpit, handling approximation and V10 audio locally. Its source folder is detected automatically; **Manage… → Choose source folder…** lets you select another location. You can stop conversion before installation begins. Game assets are not included in downloads.

After updating, use **Manage… → Rebuild from source** beside the car selector to apply the cockpit and steering-wheel flicker fixes, texture-filtering improvements and revised steering and tyre tuning. Updating the launcher alone does not regenerate installed cars.

Ferrari supports experimental **Direct practice** and **Race** on compatible installed tracks: use **Launch** for desktop or **Launch VR** for the headset. Race supports up to seven opponents, subject to the track's grid limit and available AI paths. AI handling and headset acceptance remain experimental; opponents currently retain donor engine audio. Custom tracks must support the selected mode. Hover over a disabled launch button to see what needs changing. CUSTOM cars works independently of CUSTOM tracks. Turn it off to restore the previous stock-car selection. **Manage…** offers rebuilding, installation rechecking and uninstalling. An installed car works offline without Assetto Corsa. Ferrari best laps are kept separately from stock-car history.

The F2004 uses sequential shifting with an automatic clutch for the session: use your **Gear Up/Down** bindings (A/Z with the default keyboard preset). Other cars keep their saved transmission setting unless you choose one under [Controls → Transmission](controls.md#driving-controls). The steering-wheel LCD shows **RPM, gear and speed in the game's selected units**; the stock HUD RPM dial is not calibrated for the F2004. **Rebuild an existing import** to obtain the updated LCD and engine tones. Desktop engine sound and gear changes passed a player check; audio remains an approximation of the source car.

Cockpit stabilization reduces visual shake without changing physics and follows deliberate right-stick look. Handling, suspension and the fitted native driver's appearance and animation remain experimental. The normal handbrake control can hold the car on the grid (Space with default keyboard controls; A with the Xbox preset). Body paint uses a matte approximation. Mirrors, damage and distant detail levels have limitations. LAN, career and saved full-replay playback are unavailable for this car.

### External cars

**Open external cars folder** opens `DiRT2VR/custom-cars/external`. Place a complete prepared car pack in its own subfolder, then choose **CUSTOM cars → Manage… → Refresh external packs**. External cars are marked in the car selector; missing or incompatible selections cannot launch. Car and track capabilities must both permit the selected mode. External cars use their declared stock donor’s class and audio. Saved replay playback requires the same pack identity, version and contents. See the [external pack guide](../EXTERNAL_PACKS.md).
