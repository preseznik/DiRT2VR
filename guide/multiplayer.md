# LAN multiplayer

[← Back to README](../README.md)

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

## Finding the host

The browser lists PCs launched using **HOST** while their game is running. **HOST game running** does not confirm that a lobby is open; its player count is unavailable and shown as a dash. Create the lobby inside the game before the other player joins. Both PCs need version 0.6.3 or later with the same discovery protocol. Older kits and unmodified games are not listed. Entries disappear when the host game exits; stale entries cannot be joined. Discovery uses UDP 39820 on private IPv4 LANs; guest Wi-Fi/client isolation or firewall rules may block it. No public internet scanning or port forwarding is used.

## Current status

**Multiplayer VR is available but untested.** Launcher-side event setup and direct creation of a game lobby remain unfinished; HOST opens the game menus. Launcher discovery and JOIN have reached a lobby on two PCs. The Battersea race-loading disconnect was traced to modified startup movie definitions; with logo skipping disabled, both players drove, finished the race and reached results. Current LAN launches automatically keep those definitions intact. Install the same current package on both PCs. Broader event coverage and repeat races still need testing. A manually saved profile change persisted from LAN into Normal Launch.

## Problems

**VR race disconnects at the countdown:** the race start checks game files, so LAN sessions leave them unchanged: VR switches off motion blur and head camera shake in memory instead of editing camera and effect files, and graphics filters and **Bloom** off are not applied in LAN. Keep **Skip startup logo movies** off on both PCs. A reported VR disconnect right after the countdown is the reason for this; it has not yet been confirmed fixed, so if it still happens, please send the logs described below.

For a LAN disconnect report, enable **Settings → Enable diagnostic logging** on both PCs, save, and repeat the event. Quit both games, then use **Open logs** to collect `lan-network.log` and, if present, `lan-network.previous.log` from each PC's newest session folder. These contain network addresses and transport metadata, not packet payloads or save data. LAN traces are limited to 8 MiB per session; turn logging off after the test. Older session folders are kept until you remove them.

After a crash or power failure, close any remaining game processes and choose **Restore original files** before ordinary play. The launcher also recovers interrupted sessions from the older LAN kits. Keep `DiRT2VR/lan-backups`; conflicting edits are preserved and reported. If Windows asks about network access, allow DiRT 2 on your Private/home network rather than disabling the firewall.

Older test-kit profiles and imported copies are not listed in the Profile tab. They are left on disk unchanged. Current game career remains the default until you choose another profile.
