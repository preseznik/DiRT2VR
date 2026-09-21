# DiRT2VR

An experimental cockpit VR mod for **DiRT 2**, with stereoscopic rendering, head tracking and a virtual screen for menus.

Tested with **Quest 3 through SteamVR**, primarily in the Subaru Impreza STI. Other cars, stages and headsets need more testing. **PSVR2 compatibility has not been tested.** Occasional hitching and incomplete features remain.

## What you need

- A Windows PC capable of running DiRT 2 and SteamVR.
- Your own working copy of **DiRT 2, version 1.1.0.0**. The launcher checks the exact supported executable and stops if it does not match.
- A headset connected and ready in **SteamVR**.
- A keyboard for the VR shortcuts, plus your usual gamepad or steering wheel and pedals for driving. Motion controllers are not used.
- A prepared build of this mod. **There is no packaged installer yet.** Downloading the repository alone is not enough; follow the [build instructions](docs/development.md#build) once, or have someone prepare the build for you.

## Set up the game copy

1. Run your original game normally once to check that it works and has created its settings.
2. Inside the mod folder, create `artifacts/game` and copy your **entire DiRT 2 installation** into it, keeping all existing files, including `xlive.dll`. The folder should contain `dirt2.exe`, `dirt2_game.exe` and the game's data folders directly.
3. Keep the mod's launcher, tools and prepared build in their existing folders. The launcher sets up the copied game automatically.

The launcher uses this separate copy and leaves your original installation untouched. Both copies use your **normal save profile**, so race progress is saved normally. Temporary graphics and camera changes are restored when you quit through the launcher.

## Play in VR

1. Start SteamVR, connect your headset and put it on.
2. Double-click **`Start-DiRT2VR.cmd`** in the mod folder. Leave its window open while playing.
3. Navigate the game on the virtual screen using your usual controls.
4. For the tested starting point, enter a **Subaru Impreza STI** event and select cockpit view.
5. Click the game window if necessary, then press **F9** to enter cockpit VR. Face forward in your normal seated position and press **F10** to recenter.
6. Before using pause menus, replays or flashbacks, press **F9** to return to the virtual screen.
7. Quit the game normally. Let the launcher finish restoring settings before closing its window.

## Controls

| Control | Action |
|---|---|
| **F9** | Switch between the virtual screen and cockpit VR; also recenters |
| **F10** | Recenter the cockpit or virtual screen |
| Gamepad / wheel / keyboard | Normal game driving and menu controls |

The game window must have focus for F9/F10. VR shortcuts do not yet have wheel or gamepad bindings.

Menus start on a screen that stays fixed in space as you move your head. Recognized trailer and exterior views also use that screen. Automatic switching is incomplete, so use F9 when needed.

## Current limitations

- The prepared camera setup targets the Subaru STI. Other interiors are not yet verified.
- Scenery now stays available when looking sideways and behind in the tested scene. Broader stage coverage is still needed.
- Headlights stay aligned with the car in the tested Subaru night event at Battersea Bridge. Other lighting and mirror behavior still need testing.
- Several effects are reduced or disabled, including crowds, particles, shadows and motion blur. Water rendering is incomplete.
- Occasional hitching remains; a consistent headset frame rate is not guaranteed.
- HUD presentation, seat adjustment and automatic handling of every menu/replay transition are unfinished. World scale has not been physically calibrated.
- Headset reconnection during play is unsupported. Quit and relaunch after reconnecting.

## Troubleshooting

**The launcher reports a missing build or converter:** complete the [one-time build](docs/development.md#build). Keep the full prepared mod folder together.

**The launcher rejects the executable:** only the currently supported DiRT 2 build works. See the [supported executable details](docs/development.md#isolated-game-diagnostic); do not replace it with files from an untrusted source.

**The headset stays on a screen:** select the Subaru cockpit and press F9 with the game window focused. Other camera views may intentionally remain on the screen.

**F9/F10 do nothing:** click the game window on the desktop, then try again.

**The view is facing the wrong direction:** sit comfortably, face forward and press F10.

**The launcher cannot find SteamVR:** if SteamVR is installed elsewhere, open PowerShell in the mod folder and supply its runtime path:

```powershell
.\tools\run-trace.ps1 -Interactive -Runtime 'D:\SteamLibrary\steamapps\common\SteamVR\steamxr_win32.json'
```

Replace the example path with the location on your PC.

**The game or launcher was forcibly closed:** the temporary settings may not have been restored. Close any remaining DiRT 2 processes and follow the [settings recovery instructions](docs/development.md#recovering-settings-after-an-interrupted-run).

To play without the mod, quit the VR copy and launch your original installation normally.

## Report a problem

Open a [GitHub issue](https://github.com/preseznik/DiRT2VR/issues) with the car, event, time of day, headset, steps to reproduce and whether it happens in cockpit VR or on the virtual screen. Include the relevant `trace.log` and `diagnostic-options.json` from the latest `artifacts/trace-*` folder. Do not upload game files or your save profile.

See the [changelog](CHANGELOG.md) for updates. Build instructions and diagnostics are in the [development guide](docs/development.md).
