# DiRT2VR

[**Stable download**](https://github.com/preseznik/DiRT2VR/releases/latest) · [**All releases, including Experimental**](https://github.com/preseznik/DiRT2VR/releases)

Open **Assets** on the release page and choose the installer or ZIP. Choose a release marked **Pre-release** for the latest Experimental features. In an existing launcher, enable **? → About → Include experimental releases** to receive these updates. **Return to stable…** lets you go back.

A desktop and VR launcher for **DiRT 2**, with custom tracks, visual presets and experimental stereoscopic VR with head tracking. Game menus use a virtual screen in VR.

Tested with **Quest 3 through SteamVR**. Xbox-compatible controller binding, launching, Toggle VR and Recenter have been tested in game. Other headsets and wheel button bindings need testing; **PSVR2, Logitech and Fanatec hardware are not yet verified**. 0.18.0 is the current Stable release; optional features and custom content retain their documented experimental limitations.

This mod will remain free forever. If you'd like to support the work, you can [support me on Patreon](https://www.patreon.com/cw/bohloney).

## Requirements

- Windows x64 and a PC capable of running DiRT 2. VR requires SteamVR or a compatible 32-bit OpenXR runtime with D3D11 support. VDXR is experimental.
- Your own working **DiRT 2 version 1.1.0.0** installation. Setup checks the exact supported executable.
- For VR, a headset connected and ready in your selected runtime: **SteamVR** or **system OpenXR** (including VDXR). Desktop play needs no headset.
- Your usual keyboard, gamepad or steering wheel and pedals. Motion controllers are not used.
- A packaged DiRT2VR installer or ZIP. Packages include the .NET runtime; no development tools or separate .NET installation are needed. The source repository alone is not a playable package.

## Install alongside your game

**A second copy of the game is not required.** Run DiRT 2 normally once before installing, then close it.

**Installer:** run the DiRT2VR Setup EXE, confirm the detected game folder (or browse to it), and finish setup. Select the folder containing `dirt2.exe` and `dirt2_game.exe`.

**ZIP:** extract the complete ZIP into that same game folder. Open `DiRT2VR.exe`. The first VR launch checks compatibility and installs the proxy from `DiRT2VR/payload`.

The game folder will contain `DiRT2VR.exe`, `Start-DiRT2VR.cmd`, a `DiRT2VR` subfolder, and an installed `d3d11.dll`. Most mod files stay in the subfolder. Game executables and the existing career profile are preserved. LAN and launcher-created profiles temporarily use the bundled offline provider through `xlive.dll`; the original bytes are restored after play. Other launches keep the original provider.

If another `d3d11.dll` is present, setup refuses to overwrite it. Remove the conflicting graphics mod using its own instructions first; automatic proxy chaining is not supported.

Start the launcher normally, **not with Run as administrator**. In protected game folders, only the file worker requests elevation when needed. Preferences are saved under `%LOCALAPPDATA%\DiRT2VR`, separately for each installation.

## Play

**Launch** starts regular desktop play; **Launch VR** starts headset play. Both use the selected Normal Launch, Direct practice or Race mode. Regular Launch needs no SteamVR or headset and uses your desktop graphics preferences.

1. Start your headset software and connect your headset. For VDXR, select VDXR as the OpenXR runtime in Virtual Desktop Streamer.
2. Open **DiRT2VR.exe**, choose **Normal Launch** on **Launcher**, and select **Launch VR**. On your first VR launch, choose **OpenXR — system default** or **SteamVR**. The prompt shows the detected system runtime. Check **Remember my choice** to skip the prompt next time.
3. The launcher stays visible while VR loads, then brings the game window forward. Leave it in front during startup. Navigate the original game on the virtual screen using your usual controls.
4. Enter an event with your chosen car. VR launches select cockpit view automatically.
5. Cockpit VR starts enabled. Sit facing forward while loading: the view centers automatically once tracking is ready and again when cockpit view first begins. Press **F10** (or Recenter) to adjust it afterward. Recenter resets your seated position and horizontal direction without baking head tilt into the view; saved seat adjustments stay unchanged. **F9** (Toggle VR) switches between cockpit VR and the flat screen; keep the game window focused.
6. Pause/options menus automatically use the virtual screen; resuming restores your selected cockpit VR mode. Use Toggle VR if another menu, replay or flashback looks incorrect.
7. Quit normally. The background session manager restores temporary files when the game exits. You may close the settings window while playing.

## Guides

Detailed instructions are in the `guide` folder, installed with the launcher in `DiRT2VR/guide`:

- [Playing](guide/playing.md): launch options, Direct practice, Race, car grades and setup, saved replays, career profiles and CUSTOM cars.
- [Custom tracks](guide/custom-tracks.md): building and playing Mizu Mountain, Nordschleife, Spa-Francorchamps, Misty Loch, Aspen, Smelter and Pikes Peak, plus external track packs. The [custom track catalogue](CUSTOM_TRACKS.md) lists every layout, and the [external pack guide](EXTERNAL_PACKS.md) covers prepared packs.
- [Graphics](guide/graphics.md): filters, bloom, borderless fullscreen, VR resolution and HUD, shadows, texture filtering and the 3D view options.
- [Controls](guide/controls.md): driving bindings, wheels, VR shortcuts, seat position, chase camera, Photo Mode and replay controls.
- [LAN multiplayer](guide/multiplayer.md): hosting and joining LAN games on desktop or in VR.
- [Settings, Advanced and updates](guide/settings-and-updates.md): VR runtime, appearance, diagnostic logging, rewind, Large Address Aware, background play and updating.
- [Troubleshooting](guide/troubleshooting.md): known limitations, common problems, reporting a problem, recovering files, upgrading and removing.

## Help

If something goes wrong, close the game and choose **Restore original files** before playing again. To report a problem, enable **Settings → Enable diagnostic logging**, reproduce the issue and attach the session folder from **Open logs** to a [GitHub issue](https://github.com/preseznik/DiRT2VR/issues). See [Troubleshooting](guide/troubleshooting.md#report-a-problem) for what to include.

See the [changelog](CHANGELOG.md).

Third-party license notices are included in `DiRT2VR/licenses` in packaged builds. This product includes software developed by Jon Skeet and Marc Gravell. Contact skeet@pobox.com, or see https://jonskeet.uk).

Matching LAN library source is preserved in the [repository](https://github.com/preseznik/DiRT2VR/tree/main/source-archives/lan). It is not needed to play. Installed license notices include its download link and checksum.
