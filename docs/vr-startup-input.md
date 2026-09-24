# VR startup and wheel shortcuts

The Fanatec driving-binding fix was confirmed by the user. VR shortcuts previously used a separate Raw Input/HID reader. `ControllerInput` now also uses the manifest-verified `driving_input.dll` already used by driving capture, both in the launcher and background session manager. DirectInput instance GUIDs and one-based button numbers (1–128) identify shortcut assignments. Xbox remains XInput; existing HID assignments remain readable. Inputs stay nonexclusive and reach the game normally.

The reader owns a hidden top-level window (required for DirectInput cooperative level), enumerates on first poll, and refreshes after Windows device notifications. Refresh sends disconnect before new samples to disarm held shortcuts. Button capture ignores switches already held at capture start, waits for the selected buttons to release, and forgets incomplete capture on disconnect. Launcher sampling runs every 20 ms independently of status refresh; session sampling uses the existing wait loop. Driving axes never enter the VR shortcut button set.

`VrStartInfo` sets `DIRT2VR_AUTO_COCKPIT=1` for solo, quick launch and HOST/JOIN VR. Desktop factories clear inherited experimental flags. This starts the existing screen/cockpit mode switch in cockpit mode; existing cockpit-camera, pause and trailer guards still govern presentation. Manual Toggle VR continues to work and retains the chosen mode through pause/resume.

`cockpit_start.cpp` intercepts the verified game's camera-name lookup at RVA `0x724f90`. Only four caller sites request `head-cam`: saved `current_camera` restoration at `0x233ce4`, deferred startup override at `0x3217b0`, event startup override at `0x352772`, and demo camera setup at `0x245360`. Ordinary cycling, replay and look-back lookups retain their arguments. A missing head camera retains the original lookup. Executable identity, function prologue and all four relative call targets are checked before enabling the hook. No profile camera setting is directly edited; existing temporary comfort-camera assets and recovery are unchanged.

## Validation

- Static inspection of the supported executable establishes the named lookup, saved-camera key, call targets and guard bytes.
- Native distribution: 15 CTest checks pass, including inactive-proxy forwarding and VR hotkeys.
- Launcher regressions cover DirectInput capture, an unrelated held switch, button pairs including button 128, startup/reconnect suppression, overlap rejection, helper loading and HOST/JOIN startup flags.
- The isolated desktop game probe on 2026-09-24 exited before D3D11 initialization, before the new hook was installed. All temporary proxy/LAN/flow files were restored and the isolated test save hashes remained unchanged. This is **not runtime proof of cockpit selection**.
- Physical Fanatec shortcut and automatic cockpit/headset startup acceptance are pending. Check normal-menu entry, direct Practice/Race and multiplayer, plus manual Toggle VR and pause/resume. Do not describe them as hardware-verified until tested.
