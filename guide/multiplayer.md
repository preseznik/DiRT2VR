# LAN multiplayer

[← Back to README](../README.md)

LAN support is included in the launcher package; no separate test kit or PowerShell window is needed. Both PCs need the supported game and the current launcher package on the same LAN.

## Direct lobby (Super Experimental)

Direct is available in Experimental builds for two desktop players on the same LAN. Two-PC testing is still open for Spa driving and automatic return after the finish.

1. On both PCs, select an existing career on **Profile**. Open **Multiplayer**, choose **Direct (Super Experimental)**, then **Open Direct lobby**.
2. Enter a player name on each PC. If your original game files are in another installation, select **Stock game folder…** before connecting. On one PC choose **Host lobby**. On the other, enter the host's displayed private LAN IPv4 address and choose **Join lobby**.
3. Chat and choose a different Subaru each. The host chooses Battersea Bridge, Battersea Air or **Spa-Francorchamps (custom)**. The launcher requests three Rallycross laps. Changing the track or a car clears both ready marks.
4. Choose **Ready** on both PCs, then **Start race** on the host. With two players and different cars, the host can also **Force start** before everyone marks ready. Keep both launchers open until both players have quit the game.
5. Quit DiRT 2 on your PC to bring your existing lobby back into view. Your partner can keep driving. The lobby shows each driver's result, lap count and available times. Both launchers ask their games to close once both PCs confirm the finish results. A driver disqualified for standing still can return automatically once the other driver finishes. If the cars keep circling, you can still quit locally or both choose **Return to lobby**. Once both games close, mark ready to race again with the same drivers, cars, track and chat.

The first start needs about 12 GB free on each PC for a private copy of the stock game. Both PCs verify their files before DiRT 2 opens directly into the event. Each race uses a snapshot of the selected career. Progress from Direct stays in that copy. If the PCs cannot connect, allow the launcher and the private game on the Windows Private network. The lobby uses TCP 39822 and DiRT 2 uses UDP 39001 and 39002.

For Spa, install the same race-capable pack on both PCs using **Manage → GRID 2 → Spa-Francorchamps**. Ready and Force start become available once both copies match. If you update a pack while the lobby is open, switch to Battersea and back to Spa to repeat the check.

Two PCs have raced together on Battersea Air and started another race in the same room after quitting. With the guest parked, the host finished all three laps and AI took over after the finish. If the remaining driver also stops for long enough, DiRT 2 can disqualify both cars and end the race.

If AI takes over early, quit DiRT 2 and choose **Save race logs…** on each PC before starting another race. Send both ZIPs and say which car you drove and where control changed. The logs include each car's lap report and the game's finish list. The optional **Record lap telemetry** setting adds motion data and redirects the game's motion output away from any external dashboard or rig during that race. Leave it off to keep using your normal motion feed. Direct currently supports desktop starts with both players present.

## XLLN (In-game LAN)

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

**XLLN multiplayer VR is available but untested.** XLLN HOST opens the game menus. Launcher discovery and JOIN have reached a lobby on two PCs. The Battersea race-loading disconnect was traced to modified startup movie definitions. With logo skipping disabled, both players drove, finished the race and reached results. XLLN automatically keeps those definitions intact. Install the same current package on both PCs. Broader event coverage and repeat races still need testing. A manually saved profile change persisted from XLLN into Normal Launch.

## Problems

**Disconnect right at the race start ("You lost your connection to the session"):** at the start of every LAN race, DiRT 2 checks 56 of its own files (car, track and core data) and also that its program code is unchanged. DiRT2VR's features change that code in memory, always in VR and on desktop with features such as cockpit steering corrections, chase free look, extended grass, anisotropic filtering or launcher driving controls, so such a player left every race at the start. The LAN support now judges only the files: a player whose files differ is still refused. In VR, motion blur and head camera shake are switched off in memory. Graphics filters, **Bloom** off, the desktop cockpit seat and field of view, and the experimental Large Address Aware patch apply in LAN too: the race start doesn't check the files they change. Keep **Skip startup logo movies** off on both PCs. A desktop race with features on has been confirmed on two PCs; VR LAN still needs a test, so if it still drops, please send the logs described below.

For a LAN disconnect report, enable **Settings → Enable diagnostic logging** on both PCs, save, and repeat the event. Quit both games, then use **Open logs** to collect `lan-network.log` and, if present, `lan-network.previous.log` from each PC's newest session folder. These contain network addresses and transport metadata, not packet payloads or save data. LAN traces are limited to 8 MiB per session; turn logging off after the test. Older session folders are kept until you remove them.

After a crash or power failure, close any remaining game processes and choose **Restore original files** before ordinary play. The launcher also recovers interrupted sessions from the older LAN kits. Keep `DiRT2VR/lan-backups`; conflicting edits are preserved and reported. If Windows asks about network access, allow DiRT 2 on your Private/home network rather than disabling the firewall.

Older test-kit profiles and imported copies are not listed in the Profile tab. They are left on disk unchanged. Current game career remains the default until you choose another profile.
