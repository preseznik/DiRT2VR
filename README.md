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

The game folder will contain `DiRT2VR.exe`, `Start-DiRT2VR.cmd`, a `DiRT2VR` subfolder, and an installed `d3d11.dll`. Most mod files stay in the subfolder. Your game executables, `xlive.dll` and save profile are preserved.

If another `d3d11.dll` is present, setup refuses to overwrite it. Remove the conflicting graphics mod using its own instructions first; automatic proxy chaining is not supported.

Start the launcher normally, **not with Run as administrator**. In protected game folders, only the file worker requests elevation when needed. Preferences are saved under `%LOCALAPPDATA%\DiRT2VR`, separately for each installation.

## Play

**Launch** starts regular desktop play; **Launch VR** starts headset play. Both use the selected Game menus, Direct practice or Race mode. The launch buttons stay on the left; Save settings, Restore original files and Open logs stay on the right.

Regular Launch needs no SteamVR or headset and keeps your normal camera, effects and graphics settings. Desktop Direct practice and Race require DX11 and use only the human-control fix and a temporary race file. VR shortcuts and Graphics-tab overrides apply only to Launch VR.

1. Start SteamVR and connect your headset.
2. Open **DiRT2VR.exe**, check the SteamVR runtime path in **Settings**, choose **Game menus** on **Launcher**, and select **Launch VR**.
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

### Direct practice (experimental)

On **Launcher**, choose **Direct practice**, an **Event** category, a **Track**, and a **Car**, then **Launch VR**. Event filters the track list by discipline; this is solo practice, not a career event. The launcher lists installed routes and cars from the supported game's catalog. The Subaru STI is the tested cockpit; other car interiors are experimental.

Direct practice bypasses the trailer menus and loads a player-driven car. Select cockpit view and use Toggle VR as usual. **Practice loops after the finish, and its pause menu only offers Continue. Press Alt+F4 to quit.** The session manager restores temporary files after exit. Use **Game menus** for full race options. Saved practice selections also work with `Start-DiRT2VR.cmd`.

Desktop steering/throttle, pause/resume and finishing have been confirmed in the Subaru at Baja. The packaged direct-practice route in VR, other cars and broader stage coverage still need testing.

### Race (experimental)

Choose **Race**, an event, track and car, then set **AI opponents** from 1 to 7. Use **Launch** for desktop or **Launch VR** for headset play. The whole grid uses your selected car; Direct practice remains solo regardless of the saved opponent count. Start with Landrush or Rallycross. Other disciplines and track/car combinations need testing.

Use **Laps (circuits)** to choose 1–20 laps in either **Race** or **Direct practice**. On point-to-point stages this control is disabled and the session is one stage run; your saved circuit lap preference is retained. The three-lap HUD and continuation into lap 2 have been confirmed on desktop at Baja – Ensenada Sprint. Other counts, complete multi-lap finishes and VR still need testing.

Race uses the same direct-start route as practice: it bypasses menus, repeats after finishing, and only offers Continue when paused. **Alt+F4 quits.** Difficulty, career progression and normal results screens are not configurable here; use Game menus for the full event flow. Desktop player control and seven AI opponents have been confirmed at Baja in the Subaru. Crowded-grid VR performance still needs testing.

### Graphics

| Setting | Default | Effect |
|---|---|---|
| Render resolution | 100% | 1600 × 1200 per eye at full field of view. Adjust from 50–150%; lower values reduce scene detail and pixel work. |
| Headset texture | 50% | 25–100% of SteamVR's recommended width and height. Raising this alone cannot add detail missing from the scene render. |
| Field of view | 100% | Full view. Experimental 70–99% settings crop the periphery and reduce the scene resolution proportionally. |
| Car mirrors | Use game setting | Optionally force mirrors on or off during VR sessions. |

The Graphics tab shows the effective scene resolution and pixel count. These percentages scale width and height, not total pixels: 80% render resolution uses approximately 64% of the baseline pixels. **Restore graphics defaults** returns to the tested baseline; save afterward.

Refresh rate is controlled by **SteamVR or your headset connection software**. The launcher shows the rate reported at the last launch when available, clearly marked as a past reading. The desktop game's refresh setting does not set headset Hz. Lower resolution may help GPU performance, but a particular frame rate is not guaranteed.

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

Choose **Check for updates** to check the public GitHub Releases page; no GitHub account is required. **Include experimental releases** is selected by default in alpha builds. Clear it to check only stable releases. Checks happen only when requested.

When a newer release has a verified installer, **Download and install** downloads it, checks its SHA-256 checksum and opens setup for your current game folder. Close the game first. The launcher restores pending changes before handing over to setup, then closes. Complete the normal setup prompts, including Windows administrator approval if requested. Preferences are retained. This is an assisted update, not a silent background installation. ZIP users can use it too; doing so adds the installer and uninstaller to that installation.

Updates require a published [GitHub Release](https://github.com/preseznik/DiRT2VR/releases) containing the packaged installer. Source commits and GitHub's source-code ZIPs are not installable updates. If checking or downloading fails, your current installation remains available; retry later or use the Releases link. Maintainers can find the publishing steps in [the update documentation](docs/updates.md).

### Restore or remove

VR sessions temporarily adjust the Subaru camera (or the selected car's camera for direct practice), motion-blur asset and selected graphics settings. Originals and a recovery journal are saved before changes. Normal exits and detected game crashes trigger restoration. Unrelated graphics-settings edits are retained. Direct practice temporarily creates `DiRT2VR/p.xml` and removes it during recovery. An existing file at that path is preserved and blocks preparation.

After a power failure or forced session-manager termination, close any remaining DiRT 2 processes and choose **Restore original files**. A new launch also checks for pending recovery. Do not delete `DiRT2VR/backups` or the corresponding AppData folder while recovery is pending.

If recovery reports a conflict, it preserves unexpected asset edits and backups instead of overwriting them. Keep the files and report the error. Recovery must finish before upgrading or uninstalling. Close the launcher and game before replacing package files.

**Installer upgrade/removal:** run the new installer to upgrade, or use Windows Installed apps / the DiRT2VR uninstaller to remove it. Preferences, logs and retained backup records are preserved.

**ZIP upgrade:** restore original files, close the launcher, then extract the new package over the existing mod files. Keep the backups and `installation.json` receipt. The next launch updates only a recognized proxy.

**ZIP removal:** with the game closed, run `DiRT2VR.exe --remove-proxy` from the game folder. After it succeeds, remove `DiRT2VR.exe`, `Start-DiRT2VR.cmd` and the `DiRT2VR` subfolder. Do not remove game files. AppData preferences/logs may be retained or removed separately once recovery is complete.

## Limitations and troubleshooting

- Game-menu launches prepare the Subaru STI camera. Direct practice prepares the selected car, but other interiors and broader stage coverage are not yet visually verified.
- Scenery visibility and car-aligned headlights passed the reported tests; other lighting, mirrors and interiors need testing.
- Road rubble is retained in both eyes in the tested Novigrad scene. Other stages and ground-cover variants still need testing.
- Crowds, particles, shadows and motion blur are reduced or disabled. Water, HUD placement, seat adjustment, replay transitions and calibrated world scale remain unfinished.
- Occasional hitching remains; a steady headset frame rate is not guaranteed.
- Headset reconnection during play is unsupported. Quit and relaunch after reconnecting.
- Installation acceptance in protected folders and interruption scenarios is still in progress. Packages are experimental.

**Runtime not found:** use **Browse…** beside SteamVR runtime and select SteamVR's `steamxr_win32.json`. This is a per-session choice; it does not change the global OpenXR runtime.

**No VR / shortcuts do nothing:** select cockpit view, focus the game window, and use Toggle VR. Check connected-device indicators and saved bindings. Recognized non-cockpit cameras stay on the virtual screen.

**Unsupported executable or foreign proxy:** use the supported game build and resolve the reported conflict. Do not bypass compatibility checks or download replacement game executables from an untrusted source.

**Report a problem:** use **Open logs** and include the relevant `trace.log`, car/event, headset, steps to reproduce, and whether the problem affects cockpit VR or the virtual screen in a [GitHub issue](https://github.com/preseznik/DiRT2VR/issues). Logs live under `%LOCALAPPDATA%\DiRT2VR\<installation-id>\logs`. Do not upload game files or save profiles.

See the [changelog](CHANGELOG.md). Build instructions and diagnostic details are in the [development guide](docs/development.md) and [launcher implementation notes](docs/launcher.md).

Third-party license notices are included in `DiRT2VR/licenses` in packaged builds. This product includes software developed by Jon Skeet and Marc Gravell. Contact skeet@pobox.com, or see https://jonskeet.uk).
