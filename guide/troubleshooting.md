# Troubleshooting, recovery and removal

[← Back to README](../README.md)

## Known limitations

- Scenery visibility and car-aligned headlights passed the reported tests; other lighting, mirrors and interiors need testing.
- Road rubble is retained in both eyes in the tested Novigrad scene. Other stages and ground-cover variants still need testing.
- VR reduces particles and motion blur. **Graphics → Main → VR rendering → Crowds (experimental)** enables spectators using the game's saved crowd quality; it starts off. Save and relaunch to apply. Crowds add rendering work and still need broader headset testing; this does not add spectators to custom tracks that omit them. **Shadows (experimental)** enables shadows; keep them off if brightness or performance looks wrong. Desktop crowd settings are unchanged, and temporary VR graphics changes are restored after play. Water stays visible and uses per-eye reflections in VR; the Ensenada Sprint puddle fix has been checked in Quest 3. Other tracks, reflected objects when looking behind, scenery pop-in and HUD placement still need testing.
- Mizu VR, chase-camera free look in a headset, nighttime filters and different filter presets between LAN players still need wider testing. Use **Original** in Filters if a custom preset looks wrong.
- Occasional hitching remains; a steady headset frame rate is not guaranteed.
- Headset reconnection during play is unsupported. Quit and relaunch after reconnecting.
- Installation acceptance in protected folders and interruption scenarios is still in progress. Packages are experimental.

## Troubleshooting

**Administrator warning:** an elevated launch now offers **OK** to continue or **Cancel** (selected by default). Continuing runs DiRT 2 and every mod it loads with administrator access for that session. Normal launches do not show the warning; the separate file worker still asks for elevation when needed. This can happen without choosing **Run as administrator**, for example with the built-in Windows Administrator account or an elevated parent program. No acceptance is remembered. For an elevated command-line launch with `--quiet` or no interactive desktop, add `--allow-elevated` to accept explicitly for that launch; the flag does not elevate the launcher itself.

**Runtime not found:** for SteamVR, use **Browse…** beside SteamVR runtime in Settings and select `steamxr_win32.json`. For system OpenXR, configure your headset software as the active OpenXR runtime; DiRT 2 needs its 32-bit runtime and D3D11 support. Start the headset connection before launching. You can switch back to SteamVR under **Settings → VR runtime**. DiRT2VR selects the runtime for its own processes without changing Windows' global OpenXR setting.

**OpenXR add-on prevents startup:** if a layer such as ReShade or OpenXR OBS Mirror fails to load, DiRT2VR names the layer and offers **Disable and retry** when its installed configuration provides a supported bypass. Review which features will be unavailable before accepting. **Remember for this DiRT2VR installation** is optional and is saved only after the headset preflight succeeds. To undo a remembered choice, close the game and use **Settings → VR compatibility → Manage layer bypasses… → Re-enable selected**. Bypasses affect only DiRT2VR's VR launches; other applications keep their settings. Changed layer registrations need approval again if they still block startup. Unsupported failures show an error without disabling anything. With diagnostic logging enabled, **Open logs** contains both `preflight.txt` and, after a retry, `preflight-retry.txt`. The manual ReShade and OBS Mirror bypasses were confirmed by affected users; the new prompt and remembered-choice flow still need affected-user testing.

**No VR / shortcuts do nothing:** select cockpit view, focus the game window, and use Toggle VR. Check connected-device indicators and saved bindings. Other camera views use the virtual screen unless **Advanced → Graphics → 3D beyond the cockpit** is enabled; that option supports bonnet, bumper and chase views.

**Game closes while loading, with no error (NVIDIA):** once NVIDIA's shader cache for DiRT 2 grows to 512 MB, the game closes during loading. The launcher removes that cache file before each launch, and the driver rebuilds it, so the first minutes of driving can stutter while shaders compile again. If the launcher reports that it could not remove the file, close DiRT 2 and delete the named file in `%LOCALAPPDATA%\NVIDIA\DXCache`, or clear the shader cache with the NVIDIA app. Other games' caches are not touched.

**Unsupported executable or foreign proxy:** use the supported game build and resolve the reported conflict. Do not bypass compatibility checks or download replacement game executables from an untrusted source.

**Launcher won't open: "Object reference not set to an instance of an object":** in 0.18.5 to 0.18.7, a section collapsed in the Graphics tab (for example **Desktop**) can stop the launcher opening on the next start. Update to a newer build, or delete `collapsed-sections.json` from your installation's folder under `%LOCALAPPDATA%\DiRT2VR`; the launcher then opens with every section expanded.

**Custom track/car build failed:** enable diagnostic logging in **Settings**, save, then retry the build. On failure, **Open logs** contains a dated folder with `custom-build-error.json`; its location also appears in the error details. It records the pack, source-folder path and error details, including converter error output when available. Cancelled builds do not create failure reports.

**Move built tracks or cars to another PC:** see [Custom tracks → Moving built tracks or cars to another PC](custom-tracks.md#moving-built-tracks-or-cars-to-another-pc).

**File preparation failed:** the error includes the affected file and failure details when available. Close the game and use **Restore original files**. Include `%LOCALAPPDATA%\DiRT2VR\<installation-id>\worker-error.json` in your report; it contains the latest failed operation, including any administrator retry. This small recovery diagnostic is kept even with logging off. Keep the original backups and do not change permissions across the whole game folder.

Read-only game files are supported during camera, effects, menu and offline-profile preparation. Their original protection is restored after play or interrupted-session recovery. Updates also handle read-only launcher files. If an older version failed with `cameras.xml [ReadOnly, Archive]`, update the launcher, choose **Restore original files**, then retry. If recovery reports a file in use, close the application using it first. Other access-denied errors can still require folder permission changes; the launcher reports the affected file and does not change Windows permissions.

## Report a problem

Enable diagnostic logging in **Settings**, save, and reproduce the issue. Then use **Open logs** and zip the whole relevant session folder, including `trace.log` and `dirt2vr-settings.json`. New sessions include the build, effective launcher settings, driving calibration and initial seat positions; VR traces record track/route loading destinations and player-camera cars. Loading records do not prove a race finished loading, and career series names are not captured, so still provide the series/event name, headset, steps to reproduce, and whether the problem affects cockpit VR or the virtual screen in a [GitHub issue](https://github.com/preseznik/DiRT2VR/issues). Turn logging off afterward. Logs live under `%LOCALAPPDATA%\DiRT2VR\<installation-id>\logs`. Do not upload game files or save profiles.

The launcher checks its temporary files once a day while idle, and every minute until a large backlog is cleared. It removes older downloaded installers (keeping the newest and any less than a day old), abandoned custom-track build folders older than a day, diagnostic sessions as described in [Settings → diagnostic logging](settings-and-updates.md#launcher-settings), and the Direct multiplayer game copy after two weeks without a Direct race. Folders left by other or removed DiRT 2 installations are cleaned the same way. Settings, bindings, careers, filter presets, installed tracks, replays and recovery backups are kept. Pressing Launch pauses cleanup so the game starts straight away. Cleanup waits while a game, update, track build or recovery is active; it never asks for administrator approval. A small `cache-cleanup.json` report is kept in the installation’s AppData folder.

## Restore, upgrade or remove

VR sessions temporarily adjust camera assets, motion-blur asset and selected graphics settings. Direct practice and Race also temporarily adjust the game's menu definitions and create `DiRT2VR/p.xml`. Originals and recovery journals are saved before changes. Normal exits and detected game crashes trigger restoration; Return to menus restores the direct-session files before reopening the game. Unrelated graphics-settings edits are retained. An existing `DiRT2VR/p.xml` is preserved and blocks preparation.

After a power failure or forced session-manager termination, close any remaining DiRT 2 processes and choose **Restore original files**. A new launch also checks for pending recovery. Do not delete `DiRT2VR/backups` or the corresponding AppData folder while recovery is pending.

If recovery reports a conflict, it preserves unexpected asset edits and backups instead of overwriting them. Keep the files and report the error. Recovery must finish before upgrading or uninstalling. Close the launcher and game before replacing package files.

**Installer upgrade/removal:** run the new installer to upgrade, or use Windows Installed apps / the DiRT2VR uninstaller to remove it. Uninstalling also removes that installation's logs, the Direct multiplayer game copy, downloaded updates and custom build leftovers from `%LOCALAPPDATA%\DiRT2VR`. Preferences, profiles, saved replays, lap times and retained backup records are preserved.

**ZIP upgrade:** restore original files, close the launcher, then extract the new package over the existing mod files. Keep the backups and `installation.json` receipt. The next launch updates only a recognized proxy.

**ZIP removal:** with the game closed, run `DiRT2VR.exe --remove-proxy` from the game folder. After it succeeds, remove `DiRT2VR.exe`, `Start-DiRT2VR.cmd` and the `DiRT2VR` subfolder. Do not remove game files. This also removes logs and other caches as the uninstaller does; AppData preferences and replays may be retained or removed separately once recovery is complete.
