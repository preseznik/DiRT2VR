# Settings, Advanced and updates

[← Back to README](../README.md)

This guide covers the **Settings** and **Advanced** tabs and **? → Help / About**. Graphics options under Advanced are in [Graphics](graphics.md#advanced-graphics-options).

## Launcher settings

The launcher matches the Windows app light/dark setting when opened. Reopen it after changing that setting. Native dark mode requires Windows 11; Windows 10 and Windows contrast themes retain the standard accessible system appearance.

**Launcher** selects how to start. **Settings** contains the game location, **VR runtime** and setup instructions. Choose **Ask every time**, **OpenXR (system default)** or **SteamVR** under VR runtime; changes apply on the next launch. System OpenXR uses the registered 32-bit runtime and can also resolve to SteamVR. VDXR headset rendering and gameplay validation remain pending. **Graphics** and **Controls** hold the settings described in [Graphics](graphics.md) and [Controls](controls.md). Choose **Save settings**; changes apply to the next session, including quick launch. Existing shortcuts and graphics preferences are preserved when upgrading.

The launcher remembers its window size and maximized state when closed. It adapts to your current screen and display scaling when reopened. Closing the window does not save uncommitted game-setting edits; use **Save settings** for those.

**Settings → Appearance → Modern interface** previews the compact modern look immediately; turn it off for Classic. Select **Save settings** to keep your choice. Existing installations start in Classic; fresh installations start in Modern. Both follow Windows light/dark appearance. In Modern Controls, click a binding cell to change it, **+** to add another controller assignment and **×** to remove one. Hover shortened cells for full device details.

**Settings → Enable diagnostic logging** is off by default, including when upgrading older settings. Enable it and save before a troubleshooting run to collect preflight details and game-side logs in **Open logs**. Turn it off afterward to stop generating session logs, frame CSVs and diagnostic captures. Existing logs are kept; you can remove them yourself when no longer needed. Settings, the latest session/refresh-rate/resolution summaries and recovery journals remain available with logging off so the launcher can restore your game files safely.

During a guided headset test, a message card may appear below your forward view. **Enter** opens its reply box; type your answer and press **Enter** again to save it, or **Escape** to cancel. Replies stay in that test's local logs for the person running the test to read. Keep the game focused and stop the car before typing; the race continues. This input is active only in explicitly enabled diagnostic sessions.

**Skip startup logo movies** and **Skip introduction for LAN multiplayer** are described in [LAN multiplayer](multiplayer.md).

## Advanced

**Frame-rate-independent rewind → On (Experimental)** is off by default. It preserves more rewind history at high FPS without limiting gameplay frame rate. Save and relaunch to apply it to single-player Normal Launch, Practice or Race, on desktop or in VR; LAN ignores it. Desktop play requires DX11. Diagnostic logging is not required.

The initial desktop test retained 10 seconds of history instead of about 5 seconds at roughly 120 FPS. Other events and VR still need testing. If rewind or resuming behaves incorrectly, turn it off and relaunch. See **? → Instructions → Advanced**.

### Large Address Aware (Experimental)

**Advanced → Memory → Large Address Aware (LAA)** lets DiRT 2 use up to 4 GB of address space on 64-bit Windows. It may help crashes when loading another race; it does not increase VRAM or fix every crash. It is off by default. Save settings and launch again to apply to desktop or VR.

The launcher backs up the supported executable, enables LAA for the session and restores the original afterward. After an interrupted session, close the game and use **Restore original files**. An exact LAA patch installed separately is left unchanged. Compatibility with every setup is not established; turn the option off if it causes problems.

### Background play (Experimental)

**Advanced → Gameplay → Keep running when unfocused** targets automatic pauses when switching to another app, including replay playback. It is off by default; save settings and relaunch to apply it. Manual pause remains available. Use windowed or desktop borderless mode: exclusive fullscreen can still suspend rendering when minimized. Desktop replay playback and manual pause have been checked. Driving and VR background behavior still need acceptance.

## Help and updates

Click **?** at the top right of the launcher (or press **F1**) for **Help / About**. It shows the installed version and build, credits developer **Bohloney**, and links to instructions, GitHub issues and releases.

The launcher checks GitHub Releases once in the background when its window opens. When a newer eligible version exists, **New version available** appears beside **?**; click it to review the update. Stable releases are always eligible. Experimental releases are included only when **Include experimental releases** is enabled. Offline checks stay quiet and never block launching. Quick launch and background game sessions do not check for updates.

Choose **Check for updates** in Help / About to retry manually; no GitHub account is required. Downloads and installation always require your action.

When a newer release has a verified installer, **Download and install** downloads it, checks its SHA-256 checksum and opens setup for your current game folder. Close the game first. The launcher restores pending changes before handing over to setup, then closes. Complete the normal setup prompts, including Windows administrator approval if requested. Preferences are retained. This is an assisted update, not a silent background installation. ZIP users can use it too; doing so adds the installer and uninstaller to that installation.

The updater shows separate downloading, verification, recovery and setup-opening messages. Closing Help / About before the installer handoff cancels setup. If an older launcher freezes after downloading, close it and run the latest **Setup.exe** directly from [Releases](https://github.com/preseznik/DiRT2VR/releases). Choose the same game folder and keep existing backups; there is no need to uninstall first.

**Include experimental releases** in **? → About** is off by default and saved immediately for this installation. It controls both automatic notices and manual checks. Turn it on to include newer experimental builds; normal releases remain eligible. Experimental offers are labelled clearly. Switching it off does not change the installed version.

Experimental builds show **Return to stable…**. This finds the latest normal release, which may be older, and asks before installing it. The game must be closed. Returning to stable disables experimental updates, keeps settings and bindings, and saves a preference backup under the installation's user-data folder in `updates/rollback-preferences`. Your career is not reset. A newer normal release is also offered through ordinary update checks.

Updates require a published [GitHub Release](https://github.com/preseznik/DiRT2VR/releases) containing the packaged installer. Source commits and GitHub's source-code ZIPs are not installable updates. If checking or downloading fails, your current installation remains available; retry later or use the Releases link.

To upgrade or remove an installation manually, see [Troubleshooting → Restore, upgrade or remove](troubleshooting.md#restore-upgrade-or-remove).
