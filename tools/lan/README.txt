DiRT2VR LAN desktop test kit

This is the first networking prototype, not the launcher lobby/browser feature.
Both PCs need their own supported DiRT 2 1.1 installation on the same LAN.

1. Extract the DiRT2VR-LAN-Test folder directly inside the game folder, beside
   dirt2.exe. Do this on both PCs from the original ZIP; do not copy a used user
   folder between PCs because it contains a unique LAN identity.
2. Close DiRT 2 and double-click Start-LanTest.cmd on each PC. Use a writable
   game folder for this prototype. Do not run the test as administrator.
   Optional: first open Lan-Settings.cmd and enable Skip introduction, then Save.
   This option skips the opening movie and forced career tutorial, confirmed in
   a fresh-profile desktop test. It defaults off. Profile creation remains
   available. Existing game flow/states files must be unmodified.
3. Complete any first-run profile prompts. This test starts a fresh isolated
   profile. It does not use your existing career. Home opens the XLLN guide.
4. Use the game's Multiplayer / LAN menu: host on one PC and join on the other.
   Begin with one Landrush or Rallycross circuit, two humans and no AI.
5. Check that both humans can drive, see/contact each other and finish the same
   race. Try a second race. Then quit DiRT 2 normally on both computers.

Keep the script window open while playing. The script temporarily swaps
xlive.dll and restores its original bytes after the game exits. Do not use the
normal DiRT2VR launcher during this test. This test is desktop-only; no VR or
demo control patches are activated.

If Windows asks about network access, allow DiRT 2 on your Private/home network.
Do not disable the firewall. XLLN uses its local discovery base port 39000 and
per-instance gameplay port mapping; network diagnostics will establish the
exact required rules. No internet relay or port-forwarding module is included.

If interrupted or after a power failure: close the game, then run
Start-LanTest.cmd -RecoverOnly
before ordinary play. Backups remain in DiRT2VR/lan-backups. Unexpected external
changes block recovery and preserve both versions. Do not delete the backups.

The isolated profile is DiRT2VR-LAN-Test/user/Documents/My Games/DiRT2.
The native DLL refuses to load outside the test runner, and stops the supported
game before its entry point if profile redirection fails. Existing xlive.ini
and the global Windows Documents location are unchanged. Logging is off by
default; one profile-ready.txt receipt is overwritten on each run.

LAN settings are saved in user/lan-settings.json. Turning Skip introduction off
restores normal onboarding checks on the next launch; it does not undo progress
already saved by the game. The bypass changes game code only in process memory.
It does not delete videos, patch game files or grant race wins, XP or unlocks.
This checkbox belongs to the LAN test kit; the normal launcher does not yet have
this option. Multiplayer launcher integration follows the native testing stage.

Limitations: no launcher lobby/browser, no automated event launch, no VR
acceptance, no protected-folder support, no automatic host migration. Full
two-PC race acceptance is required before proceeding to those features.
Two-PC hosting, joining and both players driving/seeing each other are confirmed;
race completion/results and repeated races still need testing.

Upstream: XLiveLessNess v1.6.2.1
https://gitlab.com/GlitchyScripts/xlivelessness
Pinned commit: 0b4ca99727566f835cc5aaca6b0a9dc4aced28b9
DiRT2VR changes: mandatory private configuration and Documents isolation before
game entry; additional third-party module loading disabled in this lab build.
Licenses and modified source are included in the kit.
