# PC3 VR crash investigation

The supplied `C:\Temp\delete` logs from 2026-09-23 end abruptly at frame 4440. They identify the supported game hash, SteamVR/OpenXR 2.17.10, PSVR2 at 90 Hz, and 1700×1734 submitted eye textures. The last recorded stereo pair is complete; eye draw and projection counts match. There is no exception code, crash stack, GPU removal result or orderly XR shutdown in these files.

The final address-space sample has 762,703,872 free bytes, including a 235,536,384-byte contiguous free region. Private memory is about 728 MiB. This does not establish out-of-memory as the cause, though the game retains its 2 GiB address-space limit. Do not apply a Large Address Aware patch based on these logs.

## Windows crash report

The supplied `DiRT2VR-crash.json` identifies five `dirt2_game.exe` access violations (`c0000005`) inside Microsoft's `xlive.dll` 3.5.95.0 on 2026-09-23. Latest: 16:03:08 UTC, module offset `0063b741`; previous offsets `006449eb`, `00651e72`, `0064ebb9`, `00651782`. Windows reports the module path as `C:\WINDOWS\SYSTEM32\xlive.dll`; account for WOW64 path redirection when locating the 32-bit file.

PC3 has an RTX 5090, driver 32.0.16.1656, and no game-local `xlive.dll` in the collector's file list. Its game, launcher and VR proxy fingerprints match supported/released 0.10.0 files. The GFWL startup interface is consistent with loading the Microsoft implementation. The local development machine has a different system version (3.1.0099.0), so a successful local test cannot validate PC3's DLL.

This establishes the faulting module, not the caller or underlying cause. It does not yet distinguish GFWL overlay/device handling, another GFWL worker, or corruption caused earlier by the mod. No PC3 crash fix is claimed.

Do not silently substitute the LAN backend for this installation: its `XLiveProtectData` / `XLiveUnprotectData` implementations copy bytes and do not implement Microsoft's protected-save encryption. Compatibility with a genuine GFWL career has not been established. Preserve the user's single career and do not create a new one as a workaround.

## Full dump: 2026-09-23

The user supplied `dirt2_game.exe.25140.dmp` (1,158,325,072 bytes). Read locally with a memory-mapped parser; no dump was uploaded. Extracted facts:

- Process age at capture: **63 seconds** (dump timestamp minus `MINIDUMP_MISC_INFO.ProcessCreateTime`).
- Exception thread 28400; `c0000005`, write to address `0x0000f319`.
- Faulting instruction at `xlive.dll+0x64d9dd`: `mov byte ptr ds:[0xf319], 0x99` (bytes `3e c6 05 19 f3 00 00 99`). This uses a fixed low address, not a bad pointer supplied directly by an eye-rendering call.
- Exception-context `EAX=0x8004930b`; its meaning has not been established. Do not assign an error name based on its numeric form.
- Frame-pointer chain returns through `xlive+0x1458da`, `xlive+0x1468c2`, `xlive+0x146b2c`, then Windows thread entry. The first return is immediately after the worker's indirect callback invocation. This is a background worker, not the game's render-thread stack. Raw stack scans also contain stale pointers; those are **not** established call frames.
- The worker task at `0x093c2fa8` contains `0xea60` (60,000) at `+0x0c`, callback `xlive+0xfa95c` at `+0x30`, and null callback argument at `+0x34`. The value is consistent with a periodic 60-second task, but its field semantics and the callback's exact check remain unproven.
- XLive module base `0x53960000`, image size `0xebe000`, PE timestamp `0x5306b64a`. Its embedded PDB identifier is `E0BD691405B844B893C5D7183F314B04`, age 2. Microsoft's symbol server returned 404 for that PDB.

The timed worker plus fixed-address fault suggests a GFWL compatibility/integrity failure. It does **not** justify disabling a specific check or skipping the faulting instruction: the check and surrounding protected code have not been identified reliably. An overlay-only fix is not supported by this stack.

The user confirmed **Normal Launch → Launch (desktop) stays stable**. An initial response to the desktop Direct practice comparison said it crashed, but the user immediately corrected this: **only VR crashes**. Treat the earlier desktop-crash answer and the resulting inference as withdrawn. There is no established non-VR reproduction. The captured GFWL background-worker fault is still valid evidence, but the triggering VR change (including render/device hooks versus other game hooks) has not been isolated.

The user supplied PC3's original 32-bit DLL. Authenticode validation succeeds (Microsoft LIVE Gaming for Windows); SHA-256 `8AE328CC7E9F22A8ED1B63F7F0C4977E36BC6C7F7582F1B8E41F1FDF960D5796`, timestamp and image size match the dump. A bounded inactive-proxy benchmark with it in the isolated fixture exits before graphics initialization with XLive Event ID 2, `0x80004005`, referring to the local identity runtime (`msidcrl40.dll`). This is a separate local startup limitation, not a reproduction of PC3's crash. Fixture DLLs were restored and verified.

The subsequent PC3 flat-VR comparison initially appeared stable, then the user corrected it: **it also crashed after about two minutes, while driving an event on the flat screen**. Thus the recorded boundary remains desktop stable / VR session crashes; active cockpit stereo replay is not necessary. Do not retain the superseded flat-screen-stable conclusion.

## Candidate: account for known code edits in the image checksum

[widberg's primary GFWL research](https://github.com/widberg/xlive-research) identifies `PEVerifyHash` for the exact supplied file hash. Local disassembly confirms its entry at RVA `0xf36b3`, its call to a word-checksum primitive at `0xf0f3b`, and its failure path constructing the status seen in the dump. This supports the integrity-check hypothesis; it does not by itself prove the failing range.

The candidate `gfwl_compat.cpp` hooks that primitive only for the call returning to `xlive+0xf3707`. It computes the ordinary checksum over a temporary view that restores the original bytes of recorded mod edits **only when the live edit still matches exactly**. Unknown modifications remain visible. It does not unconditionally return verification success, suppress exceptions, modify on-disk DLLs, change catalog/signature checks, or replace GFWL profile protection.

Activation is VR-only and guarded by the DLL's full disk SHA-256 plus loaded-code signatures. Other GFWL/replacement versions are untouched. All recorded MinHook entry edits, including hot-patch-above and the compatibility hook itself, are covered, as is the direct-mode control byte. The original code is never restored into running threads during verification.

Validation: 15 native tests, including overlapping/partial checksum windows and preserving unrelated/changed-detour bytes, pass. A separate x86 test loads the supplied signed DLL (without initializing a game/profile), exercises its actual checksum primitive and a real MinHook detour, and confirms that normalized checksums match original code while other modifications and hash callers retain their original behavior. **The candidate has not yet passed PC3 gameplay testing.** Keep the dump until that comparison completes.

Capture scripts under ignored `artifacts/pc3-dump-*.py` and `artifacts/read-pc3-dump.py` preserve the local analysis; no executable or save modifications were made for dump inspection.

## Next capture on PC3

`tools/Collect-Crash.ps1` collects events and hardware/file fingerprints read-only. For the exception context and thread stacks, use `tools/Configure-CrashDump.ps1`, based on [Microsoft's per-application WER LocalDumps support](https://learn.microsoft.com/en-us/windows/win32/wer/collecting-user-mode-dumps). It configures only `dirt2_game.exe`, refuses unrelated existing application-specific configuration and retains at most one full dump. It leaves the default dump folder unchanged. Existing machine-wide WER policy may override the default location; WER may not capture applications with their own crash reporting.

On PC3, in administrator PowerShell from the script's folder:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Configure-CrashDump.ps1 -Mode Enable
```

Launch the game normally (unelevated), reproduce once, then wait for the process to exit and the dump to finish writing. Look for `dirt2_game.exe.<pid>.dmp` under `%LOCALAPPDATA%\CrashDumps` unless a global DumpFolder is configured. Disable capture afterward:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Configure-CrashDump.ps1 -Mode Disable
```

The dump may contain private process memory; share it directly for diagnosis, not in a public issue. The disable step preserves dump files. The script changes neither game DLLs nor saves. An interrupted enable is recoverable with Disable; changed or foreign settings are preserved and reported. The capture must run on PC3. A local synthetic crash can test collection mechanics but cannot reproduce its GFWL failure.
