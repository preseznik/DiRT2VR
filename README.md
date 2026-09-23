# DiRT2VR

An experimental cockpit VR mod for **DiRT 2**, with stereoscopic rendering, head tracking and a virtual screen for menus.

Tested with **Quest 3 through SteamVR**, primarily in the Subaru Impreza STI. Xbox-compatible controller binding, launching, Toggle VR and Recenter have been tested in game. Other headsets and wheel button bindings need testing; **PSVR2, Logitech and Fanatec hardware are not yet verified**. This remains an experimental build.

## Requirements

- Windows x64 and a PC capable of running DiRT 2 and SteamVR.
- Your own working **DiRT 2 version 1.1.0.0** installation. Setup checks the exact supported executable.
- A headset connected and ready in **SteamVR**.
- Your usual keyboard, gamepad or steering wheel and pedals. Motion controllers are not used.
- A packaged DiRT2VR installer or ZIP. Packages include the .NET runtime; no development tools or separate .NET installation are needed. The source repository alone is not a playable package.

## Install alongside your game

**A second copy of the game is not required.** Run DiRT 2 normally once before installing, then close it.

**Installer:** run the DiRT2VR Setup EXE, confirm the detected game folder (or browse to it), and finish setup. Select the folder containing `dirt2.exe` and `dirt2_game.exe`.

**ZIP:** extract the complete ZIP into that same game folder. Open `DiRT2VR.exe`. The first VR launch checks compatibility and installs the proxy from `DiRT2VR/payload`.

The game folder will contain `DiRT2VR.exe`, `Start-DiRT2VR.cmd`, a `DiRT2VR` subfolder, and an installed `d3d11.dll`. Most mod files stay in the subfolder. Game executables and the existing career profile are preserved. LAN mode temporarily swaps `xlive.dll` and restores its original bytes after play; other modes leave it unchanged.

If another `d3d11.dll` is present, setup refuses to overwrite it. Remove the conflicting graphics mod using its own instructions first; automatic proxy chaining is not supported.

Start the launcher normally, **not with Run as administrator**. In protected game folders, only the file worker requests elevation when needed. Preferences are saved under `%LOCALAPPDATA%\DiRT2VR`, separately for each installation.

## Play

**Launch** starts regular desktop play; **Launch VR** starts headset play. Both use the selected Normal Launch, Direct practice or Race mode. The launch buttons stay on the left; Save settings, Restore original files and Open logs stay on the right.

The launcher minimizes after starting either mode and stays minimized during play. Reopen it from the taskbar when needed; the background session manager continues running.

DiRT 2 may briefly fall behind other windows as it replaces its startup window. The launcher hands focus to the replacement once; the brief drop followed by immediate return has been confirmed on desktop.

Regular Launch needs no SteamVR or headset and keeps your normal camera, effects and graphics settings. Desktop Direct practice and Race require DX11 and temporarily enable human control, the selected race and the direct-session menus. VR shortcuts and Graphics-tab overrides apply only to Launch VR.

1. Start SteamVR and connect your headset.
2. Open **DiRT2VR.exe**, check the SteamVR runtime path in **Settings**, choose **Normal Launch** on **Launcher**, and select **Launch VR**.
3. Navigate the original game on the virtual screen using your usual controls.
4. Enter a **Subaru Impreza STI** event and select cockpit view for the tested setup.
5. With the game window focused, press **F9** (or your Toggle VR binding). Sit facing forward and press **F10** (or Recenter).
6. Pause/options menus automatically use the virtual screen; resuming restores your selected cockpit VR mode. Use Toggle VR if another menu, replay or flashback looks incorrect.
7. Quit normally. The background session manager restores temporary files when the game exits. You may close the settings window while playing.

For quick launch with saved settings, use **Start-DiRT2VR.cmd**. It runs the same session manager without opening the settings window. Its equivalent command is `DiRT2VR.exe --launch --no-ui`.

For desktop quick launch, use `DiRT2VR.exe --launch --desktop --no-ui`. The existing `Start-DiRT2VR.cmd` continues to launch VR.

To play on the desktop, close the VR session and launch the game through Steam normally. Without a VR-launch session, the proxy forwards to system D3D11 without enabling VR hooks or creating diagnostics.

## Launcher settings

The launcher matches the Windows app light/dark setting when opened. Reopen it after changing that setting. Native dark mode requires Windows 11; Windows 10 and Windows contrast themes retain the standard accessible system appearance.

**Launcher** selects how to start. **Settings** contains the game location, SteamVR runtime and setup instructions. **Graphics** and **Controls** hold the settings below. Choose **Save settings**; changes apply to the next session, including quick launch. Existing shortcuts and graphics preferences are preserved when upgrading.

**Settings → Enable diagnostic logging** is off by default, including when upgrading older settings. Enable it and save before a troubleshooting run to collect preflight details and game-side logs in **Open logs**. Turn it off afterward to stop generating session logs, frame CSVs and diagnostic captures. Existing logs are kept; you can remove them yourself when no longer needed. Settings, the latest session/refresh-rate summary and recovery journals remain available with logging off so the launcher can restore your game files safely.

### Direct practice (experimental)

On **Launcher**, choose **Direct practice**, an **Event** category, a **Track**, and a **Car**, then **Launch VR**. Event filters the track list by discipline; this is solo practice, not a career event. The launcher lists installed routes and cars from the supported game's catalog. The Subaru STI is the tested cockpit; other car interiors are experimental.

Direct practice bypasses the trailer menus and loads a player-driven car. Select cockpit view and use Toggle VR as usual. At the finish, choose **Restart** or **Return to menus**. The pause menu also provides Continue, Restart and Return to menus. **Return to menus closes the game and automatically reopens Normal Launch**, keeping Desktop or VR mode; expect another loading sequence. Your saved launcher selections stay unchanged. Alt+F4 quits without reopening. Saved practice selections also work with `Start-DiRT2VR.cmd`.

Desktop steering/throttle, pause/resume and finishing have been confirmed in the Subaru at Baja. The packaged direct-practice route in VR, other cars and broader stage coverage still need testing.

### Race (experimental)

Choose **Race**, an event, track and car, then set **AI opponents** from 1 to 7. Use **Launch** for desktop or **Launch VR** for headset play. Choose **Opponent cars**:

- **Same as driver:** all opponents use your model.
- **Mixed:** other models from all installed classes, regardless of event.
- **Same class:** other models in your car's game-defined class.

Mixed/class grids are randomized each launch, repeating models if the available pool is small. If no other eligible model is installed, they use your model. Direct practice remains solo regardless of saved opponent settings. Start with Landrush or Rallycross. Other disciplines and track/car combinations need testing.

Use **Laps (circuits)** to choose 1–20 laps in either **Race** or **Direct practice**. On point-to-point stages this control is disabled and the session is one stage run; your saved circuit lap preference is retained. The three-lap HUD and continuation into lap 2 have been confirmed on desktop at Baja – Ensenada Sprint. Other counts, complete multi-lap finishes and VR still need testing.

Race uses the same finish and pause choices as practice. It stops at the finish menu rather than automatically repeating. Return to menus restarts the game in Normal Launch; this return path has been confirmed on desktop, while headset validation is pending. **Alt+F4 quits.** Custom races do not award career progress or use the normal results flow; use Normal Launch for full career events. Desktop player control and seven AI opponents have been confirmed at Baja in the Subaru. Mixed opponent models also passed a desktop race check. Same-class gameplay and crowded-grid VR performance still need testing.

### Graphics

| Setting | Default | Effect |
|---|---|---|
| Render resolution | 100% | 1600 × 1200 per eye at full field of view. Adjust from 50–150%; lower values reduce scene detail and pixel work. |
| Headset texture | 50% | 25–100% of SteamVR's recommended width and height. Raising this alone cannot add detail missing from the scene render. |
| Field of view | 100% | Full view. Experimental 70–99% settings crop the periphery and reduce the scene resolution proportionally. |
| Car mirrors | Use game setting | Optionally force mirrors on or off during VR sessions. |
| Tree detail | Game | Choose the game's Ultra low–Ultra vegetation detail presets. Higher values keep detailed vegetation farther away. |
| Object detail | Game | Choose the game's Ultra low–Ultra trackside-object detail presets. Higher values cost performance. |
| HUD follows view | Off | Keep the cockpit HUD fixed relative to the car at the selected distance. Enable to have it follow your head instead. |
| HUD distance | 1 m | Move the cockpit HUD between 1–20 metres in 0.5 m steps. Its apparent size stays constant. Applies to fixed and follow-view modes. |
| Show HUD areas | Gauges off; others on | Show or hide gauges, lap/time, race position, route map and stage progress in the cockpit HUD. |

Numeric settings use sliders with the current value beside them, including AI opponents and circuit laps. Drag a slider or use the arrow keys for one-step adjustments. The Graphics tab shows the effective scene resolution and pixel count. These percentages scale width and height, not total pixels: 80% render resolution uses approximately 64% of the baseline pixels. **Restore graphics defaults** returns to the tested baseline; save afterward.

For vegetation and object pop-in, try **Tree detail → Ultra** and **Object detail → Ultra**, save, then relaunch VR. **Game** keeps your existing game settings. These overrides are restored after play. Some tracks also impose their own draw distances, so Ultra may reduce transitions without eliminating all pop-in. Compare performance on the same section of track before keeping higher settings.

Refresh rate is controlled by **SteamVR or your headset connection software**. The launcher shows the rate reported at the last launch when available, clearly marked as a past reading. The desktop game's refresh setting does not set headset Hz. Lower resolution may help GPU performance, but a particular frame rate is not guaranteed.

The cockpit HUD is a transparent panel showing the game's race information. **HUD distance** changes its depth without shrinking the text. **Recenter** places the fixed HUD ahead of your seated position at that distance. Save and relaunch VR to apply. **HUD follows view** is off by default; Restore graphics defaults also resets distance to 1 m. Pause menus still use their separate virtual screen. Desktop HUD capture and automated rendering checks pass, but headset placement and distance changes still need testing. Technical details are in [HUD implementation notes](docs/vr-hud.md).

Under **HUD follows view**, uncheck the HUD areas you want hidden. These controls mask areas of the standard race HUD, so another overlay in the same area is hidden too. They do not change menus, the virtual screen, desktop play or the centre of the HUD. Save and relaunch to apply. Restore graphics defaults hides the speedometer/gear/revs area and shows the other areas. Existing saved choices are preserved on upgrade. Alternative HUD layouts and headset use still need validation.

Field-of-view cropping keeps a narrower cockpit view rather than stretching the full image. Menus remain on their normal virtual screen, although reducing render resolution also lowers their image detail. Cropping and nondefault graphics values have automated coverage but still await an in-headset check. Keep the defaults for the established setup.

### Controls

| Default | Action |
|---|---|
| **F9** | Toggle virtual screen / cockpit VR; also recenter |
| **F10** | Recenter |
| Normal game controls | Driving and menu navigation |

The Controls tab has **Action**, **Keyboard** and **Controller / wheel** columns. Click an action's keyboard binding to change it, optionally with Ctrl, Alt or Shift. Escape cancels capture.

Controller shortcuts start unassigned. Choose **Bind…** in the desired action's row, press one button or two buttons together on the same device, then release them. Multiple devices may be assigned to each action. Select an assignment and choose **Remove selected** to clear it. The launcher rejects overlapping assignments. Choose **Save settings**; changes apply on the next launch.

**Assigned controller buttons still perform their normal game actions.** Choose buttons or combinations that avoid unwanted driving/menu actions. Shortcuts fire once per press and require release before firing again. Keep the game window focused.

Assignments stay attached to the selected device. A disconnected device is not replaced automatically; Xbox controller slot changes may require rebinding. For standard HID wheels, press and release a button so the launcher can detect the device. Wheel shortcut support is implemented but not yet hardware-verified; normal game driving controls remain managed by DiRT 2.

## Recovery, upgrades and removal

### Help and updates

Click **?** at the top right of the launcher (or press **F1**) for **Help / About**. It shows the installed version and build, credits developer **Bohloney**, and links to instructions, GitHub issues and releases.

The launcher checks GitHub Releases once in the background when its window opens. When a newer version exists, **New version available** appears beside **?**; click it to review the update. New builds use plain major.minor.patch versions and check regular releases. Older alpha builds also include experimental releases. Offline checks stay quiet and never block launching. Quick launch and background game sessions do not check for updates.

Choose **Check for updates** in Help / About to retry manually; no GitHub account is required. **Include experimental releases** is off by default in new builds and remains selected by default in older alpha builds. This choice applies to the current Help / About window. Downloads and installation always require your action.

When a newer release has a verified installer, **Download and install** downloads it, checks its SHA-256 checksum and opens setup for your current game folder. Close the game first. The launcher restores pending changes before handing over to setup, then closes. Complete the normal setup prompts, including Windows administrator approval if requested. Preferences are retained. This is an assisted update, not a silent background installation. ZIP users can use it too; doing so adds the installer and uninstaller to that installation.

The updater shows separate downloading, verification, recovery and setup-opening messages. Closing Help / About before the installer handoff cancels setup. If an older launcher freezes after downloading, close it and run the latest **Setup.exe** directly from [Releases](https://github.com/preseznik/DiRT2VR/releases). Choose the same game folder and keep existing backups; there is no need to uninstall first.

Updates require a published [GitHub Release](https://github.com/preseznik/DiRT2VR/releases) containing the packaged installer. Source commits and GitHub's source-code ZIPs are not installable updates. If checking or downloading fails, your current installation remains available; retry later or use the Releases link. Maintainers can find the publishing steps in [the update documentation](docs/updates.md).

### Restore or remove

VR sessions temporarily adjust the Subaru camera (or the selected car's camera for direct practice), motion-blur asset and selected graphics settings. Direct practice and Race also temporarily adjust the game's menu definitions and create `DiRT2VR/p.xml`. Originals and recovery journals are saved before changes. Normal exits and detected game crashes trigger restoration; Return to menus restores the direct-session files before reopening the game. Unrelated graphics-settings edits are retained. An existing `DiRT2VR/p.xml` is preserved and blocks preparation.

After a power failure or forced session-manager termination, close any remaining DiRT 2 processes and choose **Restore original files**. A new launch also checks for pending recovery. Do not delete `DiRT2VR/backups` or the corresponding AppData folder while recovery is pending.

If recovery reports a conflict, it preserves unexpected asset edits and backups instead of overwriting them. Keep the files and report the error. Recovery must finish before upgrading or uninstalling. Close the launcher and game before replacing package files.

**Installer upgrade/removal:** run the new installer to upgrade, or use Windows Installed apps / the DiRT2VR uninstaller to remove it. Preferences, logs and retained backup records are preserved.

**ZIP upgrade:** restore original files, close the launcher, then extract the new package over the existing mod files. Keep the backups and `installation.json` receipt. The next launch updates only a recognized proxy.

**ZIP removal:** with the game closed, run `DiRT2VR.exe --remove-proxy` from the game folder. After it succeeds, remove `DiRT2VR.exe`, `Start-DiRT2VR.cmd` and the `DiRT2VR` subfolder. Do not remove game files. AppData preferences/logs may be retained or removed separately once recovery is complete.

## LAN multiplayer

LAN support is included in the launcher package; no separate test kit or PowerShell window is needed. Both PCs need the supported game and the current launcher package on the same LAN.

1. LAN uses your normal DiRT 2 career automatically, in `Documents\My Games\DiRT2`. There is one save for normal play and LAN; no import or profile selection is needed. If you have no career yet, create it in the game as usual.
2. Optionally enable **Skip introduction for LAN multiplayer**. It defaults off and skips the opening first-race movie and forced career tutorial while retaining profile creation.
3. Open the launcher's **Multiplayer** tab. On the host PC, click **HOST**, then choose **Desktop** or **VR** (or **Cancel**). Create a session through the game's **Multiplayer / LAN** menus. Select the event, track and cars inside the game.
4. On the other PC, select the host in the LAN server list and click **JOIN**, then choose **Desktop** or **VR**. Discovery refreshes automatically while this tab is open; **Refresh** starts another scan. JOIN opens LAN play and adds the selected PC to the game's network peers. Complete any startup/profile prompts, then finish joining through the game's **Multiplayer / LAN** menu. It does not enter the lobby automatically.
5. Quit normally after playing. The background launcher restores the original `xlive.dll`, even if you closed its settings window.

HOST and JOIN ask for the display mode every time. Desktop requires no headset. For VR, start SteamVR and connect your headset first; the launcher uses your saved Graphics and Controls settings. Menus start on the virtual screen; use Toggle VR for cockpit view and Recenter when seated. The Subaru STI remains the tested single-player cockpit; multiplayer headset acceptance is pending. VR graphics and asset changes are restored alongside the LAN DLL after play.

The game reads and saves the same career and graphics settings in both modes. Use the game's **Save Profile** action to explicitly save changes before quitting; the launcher does not force an autosave. LAN connection settings and this PC's network identity live under `%LOCALAPPDATA%\DiRT2VR\<installation-id>\lan`; do not copy that folder between players. The launcher does not copy, move or rewrite career files.

**Skip startup logo movies (single-player launches)** is a separate Settings checkbox, off by default. It skips the Codemasters, Intel, AMD and EGO logo movies while retaining the legal screen, attract video and other cinematics. It temporarily changes the supported startup definitions and restores them after play. HOST and JOIN retain the logo movies in both Desktop and VR: changing these definitions fails the game's race-loading checks. Intro skipping still controls the LAN first-run movie/tutorial independently. Turning either setting off does not undo saved progress.

If LAN reports altered `system\states.bin`, close the game and use **Restore original files**. DiRT2VR preserves unexpected external changes; restore the original file from your game installation if another mod changed it. On older launcher versions, turn off **Skip startup logo movies** before HOST/JOIN.

The browser lists PCs launched using **HOST** while their game is running. **HOST game running** does not confirm that a lobby is open; its player count is unavailable and shown as a dash. Create the lobby inside the game before the other player joins. Both PCs need version 0.6.3 or later with the same discovery protocol. Older kits and unmodified games are not listed. Entries disappear when the host game exits; stale entries cannot be joined. Discovery uses UDP 39820 on private IPv4 LANs; guest Wi-Fi/client isolation or firewall rules may block it. No public internet scanning or port forwarding is used.

**Multiplayer VR is available but untested.** Launcher-side event setup and direct creation of a game lobby remain unfinished; HOST opens the game menus. Launcher discovery and JOIN have reached a lobby on two PCs. The Battersea race-loading disconnect was traced to modified startup movie definitions; with logo skipping disabled, both players drove, finished the race and reached results. Current LAN launches automatically keep those definitions intact. Install the same current package on both PCs. Broader event coverage and repeat races still need testing. A manually saved profile change persisted from LAN into Normal Launch; see [test status](docs/lan-lab.md).

For a LAN disconnect report, enable **Settings → Enable diagnostic logging** on both PCs, save, and repeat the event. Quit both games, then use **Open logs** to collect `lan-network.log` and, if present, `lan-network.previous.log` from each PC's newest session folder. These contain network addresses and transport metadata, not packet payloads or save data. LAN traces are limited to 8 MiB per session; turn logging off after the test. Older session folders are kept until you remove them.

After a crash or power failure, close any remaining game processes and choose **Restore original files** before ordinary play. The launcher also recovers interrupted sessions from the older LAN kits. Keep `DiRT2VR/lan-backups`; conflicting edits are preserved and reported. If Windows asks about network access, allow DiRT 2 on your Private/home network rather than disabling the firewall.

Older test-kit profiles and imported copies are no longer used. They are left on disk unchanged; the launcher always uses the normal career.

## Limitations and troubleshooting

- Game-menu launches prepare the Subaru STI camera. Direct practice prepares the selected car, but other interiors and broader stage coverage are not yet visually verified.
- Scenery visibility and car-aligned headlights passed the reported tests; other lighting, mirrors and interiors need testing.
- Road rubble is retained in both eyes in the tested Novigrad scene. Other stages and ground-cover variants still need testing.
- Crowds, particles, shadows and motion blur are reduced or disabled. Water stays visible and uses per-eye reflections in VR; the Ensenada Sprint puddle fix has been checked in Quest 3. Other tracks, reflected objects when looking behind, and scenery pop-in still need testing. The HUD layer awaits full headset validation. Seat adjustment, replay transitions and calibrated world scale remain unfinished.
- Occasional hitching remains; a steady headset frame rate is not guaranteed.
- Headset reconnection during play is unsupported. Quit and relaunch after reconnecting.
- Installation acceptance in protected folders and interruption scenarios is still in progress. Packages are experimental.

**Runtime not found:** use **Browse…** beside SteamVR runtime and select SteamVR's `steamxr_win32.json`. This is a per-session choice; it does not change the global OpenXR runtime.

**No VR / shortcuts do nothing:** select cockpit view, focus the game window, and use Toggle VR. Check connected-device indicators and saved bindings. Recognized non-cockpit cameras stay on the virtual screen.

**Unsupported executable or foreign proxy:** use the supported game build and resolve the reported conflict. Do not bypass compatibility checks or download replacement game executables from an untrusted source.

**Report a problem:** enable diagnostic logging in **Settings**, save, and reproduce the issue. Then use **Open logs** and include the relevant `trace.log`, car/event, headset, steps to reproduce, and whether the problem affects cockpit VR or the virtual screen in a [GitHub issue](https://github.com/preseznik/DiRT2VR/issues). Turn logging off afterward. Logs live under `%LOCALAPPDATA%\DiRT2VR\<installation-id>\logs`. Do not upload game files or save profiles.

See the [changelog](CHANGELOG.md). Build instructions and diagnostic details are in the [development guide](docs/development.md) and [launcher implementation notes](docs/launcher.md).

Third-party license notices are included in `DiRT2VR/licenses` in packaged builds. This product includes software developed by Jon Skeet and Marc Gravell. Contact skeet@pobox.com, or see https://jonskeet.uk).
