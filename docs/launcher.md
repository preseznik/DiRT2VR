# Launcher and distribution implementation

Status: experimental `0.1.0-alpha.1`. End-user instructions are in [README](../README.md). The launcher works in the existing game folder; retain `artifacts/game` only for development and failure testing.

## Appearance

Tabs are Launcher, Graphics, Controls and Settings. Each TabPage disables the native visual-style background and uses the form's resolved palette, including unused page space. Practice combo text is owner-drawn with that palette because native combo text areas can keep a light brush. These controls retain normal keyboard navigation; contrast selection uses system colors. Tests render all tabs offscreen in both light and dark modes.

Startup calls `Application.SetColorMode(SystemColorMode.System)` before creating controls. This uses the Windows app color preference with native Windows Forms controls and title-bar theming. The framework reads the setting at startup, requires Windows 11 for dark mode and respects contrast themes; it does not switch a running application when Windows changes theme. See [Microsoft's API documentation](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.application.setcolormode?view=windowsdesktop-10.0). No Windows preference is written by the launcher.

`SetCompatibleTextRenderingDefault(False)` runs first, before DPI/theme initialization, inside the entry point's error handler. During the initial direct-practice launcher test, Windows reported an unhandled `InvalidOperationException` because the prior ordering called it after a hidden window existed. That background process exited before writing session status, making Launch VR appear inactive. The regression test launches the actual application entry point in separate child processes as well as rendering MainForm in the test harness.

The generated artwork and its prompt are in [launcher/assets](../launcher/assets/README.md). `tools/make-launcher-icon.ps1` re-encodes the PNG into a multi-resolution ICO. The project embeds the icon in both PE and managed resources, so the main form and Explorer/shortcuts use the same artwork even with single-file publishing. Inno Setup uses the same ICO for setup and the launcher executable for the uninstall display icon.

## Build and package

Prerequisites: the pinned dependencies from `tools/bootstrap.ps1`, Visual Studio x86 C++ tools, Windows SDK, CMake/Ninja, Python, .NET SDK 10, and Inno Setup. No development dependencies are required by packaged users.

```powershell
.\tools\build-distribution.cmd
# Requires an unmodified supported game fixture at artifacts/game:
dotnet run --project tests/launcher/LauncherTests.vbproj -c Release -- .
.\tools\package.ps1 -SkipNativeBuild
```

`DIRT2VR_VS_ROOT` overrides the Visual Studio path; `DIRT2VR_PYTHON` overrides Python. `package.ps1 -InnoCompiler <path>` overrides the Inno compiler. The default compiler is the locally installed Inno Setup 7. Packaging creates a timestamped directory under `artifacts/packages`; installer and ZIP come from the same SHA256 manifest. Never include proprietary game executables, assets or saves in a distribution.

The native build uses a static MSVC runtime, including the OpenXR loader. The launcher publishes a self-contained x64 single-file WinForms executable. `tools/ego-xml` compiles only the required XML, endian and stream support from the pinned EGO dependency. The package retains EGO, MiscUtil, MinHook, OpenXR, JsonCpp and bundled .NET notices. MiscUtil notice source: https://jonskeet.uk/csharp/miscutil/licence.txt.

## Ownership and activation

The footer groups Launch / Launch VR on the left and Save settings / Restore original files / Open logs on the right. Both buttons save the same menu/practice/race selection, then dispatch to the session manager; `--desktop` selects ordinary monitor play. Both are disabled during a managed game session. Race forwards 1–7 opponents through the same preparation and recovery path; see [direct-start grid details](direct-practice.md).

After successfully spawning a launch session, MainForm minimizes immediately. Status polling never restores or activates it. Worker/session processes do not create console windows; errors still use the existing message/status path. This avoids the launcher competing with the game window while its controls become disabled during preparation. Actual desktop foreground acceptance remains pending.

`VrSettings.LoggingEnabled` defaults to false, including for saved JSON without that field. The launcher passes explicit `DIRT2VR_LOGGING=0/1` to active VR and desktop-practice sessions and omits their output path when disabled. All native diagnostic streams go through `TraceFile`; `Log` is also gated. Screenshot/shader capture and expensive memory/GPU instrumentation are disabled with logging. Rendering, shader identities, head tracking and recovery are independent of that flag. `tools/run-trace.ps1` explicitly enables logs, as does the developer-only `--diagnostic-capture` VR launch option. Disabled logging preserves old files; no automatic deletion occurs. Automated tests cover absent/disabled/enabled native logging, existing-file preservation, preference migration, UI saving and both active launch paths.

Desktop mode recovers pending VR changes first, then skips runtime validation/preflight, VR input polling and graphics/camera preparation. Normal menu launch passes `DIRT2VR_ACTIVE=0` after removing inherited mod flags and does not deploy a proxy. Desktop practice requires `graphics_card/directx@forcedx9=false`, deploys the recognized proxy and passes `DIRT2VR_DESKTOP_PRACTICE=1` with the direct-practice flag. The proxy takes an early path that applies the guarded human-control byte only, before MinHook, render hooks, hotkeys or OpenXR initialization. When logging is enabled, desktop session logs therefore contain only compatibility/control diagnostics.

Version 4 recovery journals own only the desktop practice config, with no game-asset entries or graphics journal. They retain the short-path hash/ownership and existing-file conflict checks. Versions 1–3 continue to recover as before. Desktop and VR sessions share the mutex and process-wait routine, including cleanup on launch failure and game exit.

The package carries its x86 D3D11 proxy under `DiRT2VR/payload`. Setup validates the supported executable SHA256, rejects linked paths and foreign proxies, then records the installed proxy hash in `DiRT2VR/installation.json`. Upgrades accept the current packaged hash or a matching installed ownership receipt.

`DIRT2VR_ACTIVE=1` is required before the proxy runs compatibility checks or installs hooks. An ordinary Steam launch calls system D3D11 directly through the proxy, without XR or diagnostic side effects. The launcher strips inherited `DIRT2VR_*` variables and sets its tested rendering baseline explicitly. `XR_RUNTIME_JSON` is set only on the probe and game processes, never globally.

The settings window starts a separate unelevated session manager. The `--launch --no-ui` path and `Start-DiRT2VR.cmd` use that same manager. The file worker can elevate for fixed setup/prepare/recover/remove operations; it does not start the game or read controllers. The session manager holds `Global\DiRT2VR.Session` through preparation, play and restoration. Graphics settings are shared by game installations, so detection conservatively refuses modification if any DiRT 2 process is running.

Process lifetime currently follows the launched wrapper handle plus polling `dirt2`/`dirt2_game` processes. Full descendant/job tracking is not yet implemented; delayed-launch and unusual wrapper behavior need acceptance before release. GUI windows can coexist; only one session or recovery transaction can run.

## Transactions

Asset preparation backs up the user's own `cars/sti/cameras.xml` (the selected car for direct practice) and `postprocess/effects.xml` in `DiRT2VR/backups`. It flushes originals and a pending JSON journal before replacing either file. The journal identifies the original and modified hashes of each allowlisted asset and the owning user's pending graphics journal. An existing pending transaction cannot be overwritten by preparation. Version 3 asset journals use the verified short practice config path; versions 1 and 2 retain their original recovery paths. See [direct practice](direct-practice.md) for selection, activation and config ownership details.

Recovery restores only original/already-restored or recognized modified bytes. Unexpected asset edits, missing backups or mismatched hashes preserve the pending journal and surface a conflict. Each restored entry is journaled, then the completed journal is archived. Backups are retained. An installer running under a different account refuses to recover assets while the owner's graphics journal still needs recovery.

Documents graphics overrides have a separate per-user journal in Local AppData. If the current XML exactly matches the applied version, recovery restores the original bytes. Otherwise it restores only overridden attributes that still match the applied values, retaining unrelated or deliberately changed values. Missing/malformed settings stop recovery; they are not silently replaced.

There is an unavoidable distinction between durable files and a tested power-cut guarantee. Automated partial-write and conflict checks passed; abrupt machine loss during each actual filesystem operation has not been tested. The reported power outage occurred after the observed completed session had already restored assets.

## Graphics configuration

Graphics fields are integer `RenderScale` (50–150, default 100), `HeadsetScale` (25–100, default 50), `FieldOfView` (70–100, default 100), and `Mirrors` (`game`/`on`/`off`, default `game`). Version 1 loads with these defaults and retains its bindings. Current settings version 3 also stores launch mode and practice selection; version 2 graphics/bindings migrate unchanged. Unknown versions and invalid ranges fail validation before preparation.

`GraphicsTransaction.Prepare(settings)` journals width/height as `round(1600 or 1200 × RenderScale/100 × FieldOfView/100)`. A mirror override adds `mirrors/@enabled`; `game` leaves that attribute untouched. Recovery uses the same recorded applied values and original-byte/merge rules as the previous fixed baseline. Desktop VSync remains off; the original desktop refresh-rate attribute remains untouched. Both GUI and quick launch pass these settings to the same transaction.

The session passes invariant-culture `DIRT2VR_HEADSET_SCALE` and `DIRT2VR_FOV_SCALE` to the proxy. Missing, malformed, nonfinite or out-of-range native environment values fall back to 0.5 and 1.0. The first scales OpenXR swapchain width/height relative to runtime recommendations (this is not SteamVR's total-pixel percentage). It does not change the game's scene-render resolution; the XML override does that separately. Raising only the destination resolution cannot generate more scene detail.

For cockpit frames, `XrFrames` narrows each asymmetric FOV edge with `atan(tan(angle) × FieldOfView/100)` before both the draw callback and projection-layer submission. Eye poses are unchanged. The reduced game target supplies actual pixel-work reduction; the implementation does not render a full-size image then cover its edges. Visibility preparation still retains scenery in every direction, so CPU/draw-call costs are not reduced. Screen-mode FOV and quad placement remain unchanged, though the shared game target affects menu image detail. Cropping does not currently reduce OpenXR swapchain allocation size.

Preflight optionally enables `XR_FB_display_refresh_rate` and queries `xrGetDisplayRefreshRateFB`. It never calls a refresh-rate request API. If unavailable, preflight succeeds with an explicit unavailable value. Graphics reads the single overwritten `headset.json` summary (falling back to old `preflight.txt` logs) and labels any numeric rate as historical. The summary contains only a refresh rate and timestamp; full preflight reports are saved only with logging enabled. It does not infer hardware Hz from application frame timing; OpenXR allows `predictedDisplayPeriod` to differ from the physical refresh cycle. See the [refresh-rate extension](https://registry.khronos.org/OpenXR/specs/1.0/man/html/XR_FB_display_refresh_rate.html) and [frame submission guide](https://github.com/KhronosGroup/OpenXR-Guide/blob/main/chapters/frame_submission.md).

Validation: all eight native CTests pass, including asymmetric crop math, matching draw/submission FOV and screen/cockpit transitions. Launcher checks cover legacy migration, graphics ranges, UI save persistence, retained controller pairs, scaled XML/mirrors, exact restoration and merging unrelated edits. The UI test renders tab images off-screen without desktop input. Nondefault resolution, mirror overrides, refresh reporting on hardware and cropped-view headset acceptance remain pending; the user deferred headset testing for this change. Defaults retain the established rendering baseline.

## Input

The UI groups bindings by action on the Controls tab, with separate keyboard and controller/wheel columns. The underlying binding list and shared-memory action protocol are unchanged. List contents are rebuilt only when their text/connection state changes, preserving the selection. Changing tabs cancels an unfinished capture. All tab editing is disabled while the session manager is active.

Keyboard bindings are configured through `DIRT2VR_KEYS`; defaults are F9/F10. The game window consumes both edges of assigned keys, including F10, preventing the system-menu freeze. Ctrl/Alt/Shift are optional.

The session manager reads XInput and Raw Input/HID without exclusive acquisition or synthetic keystrokes. Raw Input registration stays out of the injected DLL. Controller identity is the XInput slot or HID device path; HID devices become known on receiving a report. Bindings are one button or a two-button chord on one device; subsets/duplicates are rejected. Held buttons and reconnection require a neutral state before arming, and a completed combination fires once until fully released.

Controller actions use a session-specific, 16-byte shared mapping: uint32 magic `0x32565244`, ABI version `1`, Toggle VR counter, Recenter counter. The manager increments counters only with the game focused. The proxy consumes changes through the existing action handling. VR buttons still reach DiRT 2 normally.

## Accepted checks and remaining gates

- Seven native CTests passed, including inactive-proxy WARP device creation without diagnostic output.
- Launcher tests cover installation/owned upgrade/foreign conflicts, Unicode and spaced paths, partial preparation, exact restoration, external-edit conflicts, graphics merging and binding arming/release/reconnect rules. Fixtures use local game files and synthetic graphics XML.
- Real Inno setup, repeat upgrade, foreign-proxy rejection and uninstall passed in a writable isolated fixture. Uninstall preserved original game files.
- The user confirmed GUI launch, Xbox-compatible controller discovery, button capture, Toggle VR and Recenter in a driven race. Bindings were Xbox slot 0, left shoulder for toggle and right shoulder for recenter.
- The follow-up `--launch --no-ui` session confirmed automatic pause/options/confirmation screen mode and return to VR on resume. Both asset originals were restored afterward.
- Still pending: protected-folder/UAC acceptance (including denial), complete descendant tracking, forced session-manager termination/recovery, interruption at every recovery stage, installed Steam game desktop/regression testing, quick-launch CMD invocation, comprehensive duplicate/focus tests on hardware, generic HID wheel hardware and PSVR2.

No public release or full acceptance claim follows from these checks. Continue using the isolated full game copy for failure injection. Validate the installed Steam game only after recovery acceptance is sufficient.

## Pause-menu rendering fix

The original camera filter admits near-plane 0.075 cockpit records, including the paused cockpit behind the Load Preset Controls dialog. That allows per-eye camera changes into a scene intended for a flat menu. The user confirmed the same dialog was correct in virtual-screen mode.

On the fingerprinted 1.1 executable, `RenderPauseWorld` is named at RVA `0xf13d18`, its type getter is `0x259830`, and frontend dispatch at `0x17bbaa` calls the handler at `0x16ec90`. That handler copies message byte +4 to frontend byte +0x96, with a second flag at +5. Pause entry call sites set +4 to one; resume clears it. The detour checks the original instruction bytes, calls the original unchanged, and observes this boolean atomically. It never retains the message pointer.

`HeadsetScene` declines stereo while the flag is set, letting the original completed frame reach `HeadsetScreen` at Present. The requested user mode remains intact, so resume restores cockpit VR. If the hook guard fails, headset output remains on the virtual screen. This does not claim to identify every replay or non-pause overlay.

Local evidence (not distributed): `artifacts/menu-build.log`; session `20260921-163231-056` under the isolated installation's AppData logs. The hook loaded successfully; pause transitions matched screen output; the tester confirmed readable menus and return to VR. The original GUI/controller acceptance log is `20260921-161815-168`. These logs contain diagnostics, not assets or saves.
