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

1. Start SteamVR and connect your headset.
2. Open **DiRT2VR.exe**, check the SteamVR runtime path, and select **Launch VR**.
3. Navigate the original game on the virtual screen using your usual controls.
4. Enter a **Subaru Impreza STI** event and select cockpit view for the tested setup.
5. With the game window focused, press **F9** (or your Toggle VR binding). Sit facing forward and press **F10** (or Recenter).
6. Pause/options menus automatically use the virtual screen; resuming restores your selected cockpit VR mode. Use Toggle VR if another menu, replay or flashback looks incorrect.
7. Quit normally. The background session manager restores temporary files when the game exits. You may close the settings window while playing.

For quick launch with saved settings, use **Start-DiRT2VR.cmd**. It runs the same session manager without opening the settings window. Its equivalent command is `DiRT2VR.exe --launch --no-ui`.

To play on the desktop, close the VR session and launch the game through Steam normally. Without a VR-launch session, the proxy forwards to system D3D11 without enabling VR hooks or creating diagnostics.

## Bindings

The launcher matches the Windows app light/dark setting when opened. Reopen it after changing that setting. Native dark mode requires Windows 11; Windows 10 and Windows contrast themes retain the standard accessible system appearance.

| Default | Action |
|---|---|
| **F9** | Toggle virtual screen / cockpit VR; also recenter |
| **F10** | Recenter |
| Normal game controls | Driving and menu navigation |

Click a keyboard binding in the launcher to change it, optionally with Ctrl, Alt or Shift. Escape cancels capture.

Controller shortcuts start unassigned. Choose **Bind Toggle VR…** or **Bind Recenter…**, press one button or two buttons together on the same device, then release them. The launcher rejects overlapping assignments. Choose **Save settings**; changes apply on the next launch.

**Assigned controller buttons still perform their normal game actions.** Choose buttons or combinations that avoid unwanted driving/menu actions. Shortcuts fire once per press and require release before firing again. Keep the game window focused.

Assignments stay attached to the selected device. A disconnected device is not replaced automatically; Xbox controller slot changes may require rebinding. For standard HID wheels, press and release a button so the launcher can detect the device. Wheel shortcut support is implemented but not yet hardware-verified; normal game driving controls remain managed by DiRT 2.

## Recovery, upgrades and removal

VR sessions temporarily adjust the Subaru camera, motion-blur asset and selected graphics settings. Originals and a recovery journal are saved before changes. Normal exits and detected game crashes trigger restoration. Unrelated graphics-settings edits are retained.

After a power failure or forced session-manager termination, close any remaining DiRT 2 processes and choose **Restore original files**. A new launch also checks for pending recovery. Do not delete `DiRT2VR/backups` or the corresponding AppData folder while recovery is pending.

If recovery reports a conflict, it preserves unexpected asset edits and backups instead of overwriting them. Keep the files and report the error. Recovery must finish before upgrading or uninstalling. Close the launcher and game before replacing package files.

**Installer upgrade/removal:** run the new installer to upgrade, or use Windows Installed apps / the DiRT2VR uninstaller to remove it. Preferences, logs and retained backup records are preserved.

**ZIP upgrade:** restore original files, close the launcher, then extract the new package over the existing mod files. Keep the backups and `installation.json` receipt. The next launch updates only a recognized proxy.

**ZIP removal:** with the game closed, run `DiRT2VR.exe --remove-proxy` from the game folder. After it succeeds, remove `DiRT2VR.exe`, `Start-DiRT2VR.cmd` and the `DiRT2VR` subfolder. Do not remove game files. AppData preferences/logs may be retained or removed separately once recovery is complete.

## Limitations and troubleshooting

- The modified camera targets the Subaru STI. Broader car/stage coverage is unfinished.
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
