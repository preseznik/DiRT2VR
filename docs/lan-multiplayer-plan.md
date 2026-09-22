# LAN multiplayer feasibility and proposed implementation

Assessment date: 2026-09-22. Research and a proposal only; no multiplayer implementation, network session, DLL replacement or game launch was performed for this assessment.

## Recommendation

Prototype LAN play by retaining DiRT 2's original multiplayer simulation and replacing the obsolete GFWL service layer with XLiveLessNess (XLLN). Build launcher lobbies and discovery around that proven game session, after demonstrating a two-PC race. Do not attempt to turn the current demo-based Race mode into multiplayer by launching identical configurations or sending vehicle transforms between independent simulations.

Feasibility is promising. The hardest unproven part is reliable programmatic entry into the game's host/join/race state machine, followed by integration with our graphics and recovery code. The launcher UI and LAN browser are comparatively routine. No unconditional delivery estimate is justified before the first two gates below.

## Evidence and limits

| Evidence | What it establishes |
| --- | --- |
| Copied `dirt2_game.exe` SHA-256 `49B1E00EA1D4BD02E633CEED63390B5CAE07601333F673C4EFD8D2F0EB54FE48` | This remains the supported executable. |
| PE imports include XSessionCreate, Search/SearchByID/SearchEx, JoinLocal/JoinRemote, Start/End, GetDetails, MigrateHost, XNetStartup and socket APIs | The installed game contains an existing network/session integration; imports do not prove it works with a replacement. |
| Copied `xlive.dll` SHA-256 `5BBE4CD8EE97FBD22EB5AB457963DB73C5D7889BA77952EEA318CF2DA6D934C8`, 209,920 bytes | Existing offline shim is identifiable. Its INI has Online=0 and a fixed local identity. Do not copy that identity to every LAN participant. |
| Read-only x86 disassembly: XSessionJoinRemote RVA 0x8df0 immediately returns zero after a common helper; XSessionSearch RVA 0x8d50 clears an output and returns zero | These entry points are stubs, not remote membership/discovery implementations. Changing Online=0 to 1 is not a substantiated multiplayer fix. |
| Installed Codemasters readme documents TCP/IP networking and TCP/UDP 3074 for joining/creating sessions | Useful baseline only. XLLN's selected port mapping and our launcher discovery need their own measured port inventory. |
| [XLLN compatibility list](https://gitlab.com/GlitchyScripts/xlivelessness/-/work_items/1) lists DiRT 2 as fully functional for v1.6.2.1 | Strong upstream precedent, not our executable-plus-VR acceptance. |
| [XLLN DiRT 2 quick-patch definition](https://gitlab.com/GlitchyScripts/xlln-modules/-/blob/7f701e39fb92e524778e96ff1ab219315f820112/bin-quick-patch/XLiveLessNess/xlln-quick-patch/xlln-dirt-2.json) includes our exact SHA-256 | Upstream explicitly recognizes this build. The documented patch enables multiple instances; it does not supply launcher race-start automation. |
| XLLN source exports XLLNLogin, XLLNLogout and XLLNModifyProperty and supports modules, adapter selection, broadcast addresses and instance port settings | Plausible integration surfaces. Exact behavior, private storage configuration and profile compatibility need testing. |

Inspected XLLN release: v1.6.2.1, published 2026-08-19, commit `0b4ca99727566f835cc5aaca6b0a9dc4aced28b9`. Module source inspected at `7f701e39fb92e524778e96ff1ab219315f820112`. Retrieve from the upstream project, pin and verify the chosen build before running it. Its current [LICENSE.md](https://gitlab.com/GlitchyScripts/xlivelessness/-/blob/0b4ca99727566f835cc5aaca6b0a9dc4aced28b9/LICENSE.md) is LGPL 2.1; older mirrors are not authoritative for the current license. Account for dependency notices, corresponding source and any modifications when packaging. Do not bundle proprietary GFWL binaries or game assets.

## Player experience

Add a **Multiplayer** tab containing a LAN browser and **Host**, **Join**, **Refresh**, and **Direct IP** actions. Keep ordinary single-player modes available.

1. Host creates a named lobby and selects event, track, circuit laps and allowed cars/classes. Initially use native multiplayer rules rather than promising every solo track/car combination is valid.
2. Others find the lobby on the selected LAN adapter or enter the host address. Show lobby name, event/track, players/capacity, latency, compatibility and Waiting/Starting/Racing state. This first browser lists DiRT2VR-managed sessions, not every third-party game session on the network.
3. Each player chooses an allowed car and **Desktop** or **VR**, then readies up. Display names and persistent unique local identities require no Microsoft sign-in.
4. Host clicks **Start race**. Each launcher prepares its local files and starts its game. The native bridge creates/joins the real game session and waits for all expected clients to join and finish loading before the host starts the event. Loading speed differences must not strand players in separate races.
5. After results, return all participants to the same launcher lobby for another event. For the first complete implementation, cleanly exiting and relaunching each game between races is acceptable; the launcher lobby persists. Keeping the engine running between events is a later optimization.

Initially target two human players, one tested Landrush or Rallycross circuit, no AI. Expand toward the original game's supported player limit after measurement. AI filling, unrestricted mixed classes, join-in-progress, spectators, voice, host migration and dedicated servers are separate features. If the host leaves, end the session cleanly and return remaining players to their launchers. Do not advertise seamless migration merely because the executable imports XSessionMigrateHost.

## Architecture

### Reuse native race networking

The existing engine should synchronize vehicles, collisions, start sequence, lap progress, finishes and results through XLLN. Neither the VB launcher nor a new custom transform stream should become the race simulation authority. Preserve the authority/replication behavior implemented by DiRT 2, measuring its limitations during testing.

Add a small guarded x86 multiplayer bridge, hosted in our native DLL or an XLLN module after checking loader compatibility. It accepts a versioned local session descriptor: host/join role, lobby identity, expected participant identities, local car and native event settings. Find game functions that perform normal network menu actions and call them on the correct game thread, with a state machine, timeouts, acknowledgments and executable/signature guards. Hooking XSession calls helps observe progress; those APIs alone do not configure an EGO race. Keyboard/menu automation is useful for diagnosis but is not the intended shipping interface.

Use a dedicated multiplayer launch route, independent of `-demo` and its human-control/lap patches. Current `Session.vb` uses `-demo` for Practice/Race and `DIRT2VR_DESKTOP_PRACTICE` for the desktop bridge. Reusing catalog choices and form controls is feasible; reusing that startup path would bypass actual remote-player creation. Our VR camera/visibility/rubble/lighting fixes must be tested on multiplayer renderer and pause states.

### Launcher lobby and LAN discovery

The host's existing background session manager runs a small lobby coordinator. A reliable TCP channel carries membership, configuration revisions, car choices, readiness and launch/results states. The game/XLLN retains its own gameplay transport. Use user-scoped local IPC for launcher-to-native communication; do not expose native control commands directly on the LAN.

UDP discovery on selected local interfaces advertises a protocol/build identifier, lobby ID, coordinator endpoint and small display metadata. Respond to bounded queries and expire missing hosts after heartbeat timeout. Measure lobby latency separately from gameplay latency. Do not scan every LAN address. Support manual IP when discovery is blocked by VLANs, Wi-Fi isolation or multiple adapters; direct IP cannot overcome absent network reachability.

Host controls event configuration; any change clears ready flags. Before launch, compare protocol version, executable build, multiplayer bridge/XLLN build and required track/car availability. Graphics/VR settings may differ. Verify authoritative session details over the coordinator connection instead of trusting discovery metadata. Bound message sizes/rates, validate enums and roster size, and never accept arbitrary executable arguments or file paths from peers.

First version is trusted-LAN only: no public matchmaking, cloud account, internet relay, UPnP or router forwarding. Keep background update checks independent of LAN functionality; lobby/race operation must work with internet disconnected. Offer narrowly scoped Windows Private-network firewall access for the measured game and launcher ports, not a firewall-disable workaround.

### Deployment and recovery

Existing distribution deliberately preserves `xlive.dll`. Multiplayer needs a new, explicit opt-in compatibility transaction rather than silently changing that promise. Stage the pinned XLLN payload beneath `DiRT2VR`; use the actual loader path required by the game only during the managed multiplayer session. Investigate safe redirection, but do not assume the current D3D11 proxy can redirect xlive imports early enough.

If a temporary root DLL swap is required, journal and durably back up the original DLL/related configuration before changing it. Refuse unknown conflicts, never replace original backups with modified bytes, and restore only recognized deployed hashes after all game descendants exit. Add interrupted-upgrade/uninstall/recovery tests. Single-player must recover its original shim before starting.

Use separate LAN identities and prove isolated profile/save storage before testing against the user's existing career. The fixed identity in the present shim is inappropriate for multiple players; changing identity can also affect save access. Do not silently rename, migrate or overwrite career saves. Retain the previous supported profile and DLL after normal exit and failures. Keep diagnostic logging opt-in as elsewhere in the launcher.

## Delivery gates and indicative effort

Estimates are developer effort, conditional on access to two Windows PCs and successful preceding gates; not commitments.

| Stage | Work and proof | Estimate |
| --- | --- | --- |
| 1. Native LAN baseline | Two isolated, supported installations with pinned XLLN and separate profiles. Use native menus to host/join and complete a desktop race without internet. Both drivers see each other, collisions and results agree, and a second race works. Verify saves/restoration. | 2–5 days initially; reassess if blocked |
| 2. Programmatic race entry | Trace successful native host/join and event selection; identify guarded game entry points. A small test harness launches two clients into the same chosen event without manual game-menu interaction. Test delayed loading, failed join, cancel and retry. | 1–3 weeks, largest uncertainty |
| 3. Launcher integration | Multiplayer tab, LAN discovery/direct IP, roster, per-player cars, readiness, host settings, all-ready launch barrier and return to lobby. Reuse preparation/input/update infrastructure where appropriate. | 1–2 weeks |
| 4. VR and distribution hardening | Desktop/VR and VR/VR sessions, repeated races, effects/performance, disconnect/host loss, multiple adapters, firewall prompts, installation recovery and packaging. | 1–3+ weeks |

Allow roughly 4–8+ weeks for a usable integrated version if the native networking and launch bridge cooperate. A same-machine dual-instance test may accelerate tracing because upstream has a matching mutex patch, but our existing process guards and shared Documents files are intentionally not dual-instance-safe. Separate lab copies/profiles/ports are required; this is not a substitute for two-PC acceptance or a reason to weaken production guards.

**First milestone:** two humans complete the same native LAN race on separate PCs with XLLN and internet disconnected. **Second milestone:** the same race starts from a programmatic host/join request. Only then commit to the full launcher browser/lobby delivery.

If XLLN fails, investigate the exact missing API/behavior and compatibility patches with a bounded spike. If menu automation is the only working bridge, describe that as an interim assisted launch. If the engine's multiplayer path cannot be made reliable, stop and reassess; custom synchronization of independent demo races would be a much larger project with substantial collision, determinism, ownership and correction risks.

## Acceptance checklist

- Two real PCs, unique identities, both drivers controllable, correct remote cars, contacts, countdown, laps, finish order/results and repeated events.
- Host configuration/car changes reset readiness; incompatible builds/missing content fail before modifying files. Delayed clients, crashes and timeouts return a clear state rather than starting mismatched races.
- Browser discovery, expiry, full/in-progress lobbies and direct IP across Ethernet/Wi-Fi and multiple adapters; no external connectivity needed. Join-in-progress remains explicitly unavailable initially.
- Separate LAN profile storage, original career access, exact DLL/config recovery on normal exit, game crash, terminated launcher, failed preparation and uninstall. Unknown edits survive as recovery conflicts.
- Desktop/Desktop first, then Desktop/VR, then VR/VR. Check recenter, pause menus, scenery visibility, headlights, rubble, game timing, GPU/CPU/memory budget and sustained remote vehicle motion. VR remains a local presentation option and must not advance networking or simulation twice per frame.
- Increase player counts only after the two-player baseline; test supported event/car rules before enabling broad choices. Do not infer multiplayer AI support from the existing solo Race grid.

Implementation of this entirely new feature would receive the next minor version (`0.2.0` from the current `0.1.x` line); this planning document alone does not trigger an application build or version bump.
