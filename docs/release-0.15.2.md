# 0.15.2 release validation

Prepared on 2026-09-27 as a diagnostics and controls-visibility patch. Issues
[#2](https://github.com/preseznik/DiRT2VR/issues/2) and
[#3](https://github.com/preseznik/DiRT2VR/issues/3) remain open: neither reported
hardware problem has been reproduced or established as fixed.

## Source and package

- Release source: published `v0.15.1` plus scoped commit
  `aa0ed4cac493c164ef3a4201f40f1877466359fd` on
  `preseznik/issue-diagnostics-release`. The default branch's unpublished track
  experiments are excluded.
- `tools/package.ps1 -SkipNativeBuild` reserved **0.15.2** after building and
  testing the release checkout's native distribution and driving input helper.
  The unchanged LAN binary matches 0.15.1 byte-for-byte; its six tests passed.
- Embedded launcher product version:
  `0.15.2+aa0ed4cac493c164ef3a4201f40f1877466359fd`.
- Package output: `artifacts/packages/0.15.2-20260927-223349` in the release
  worktree. Main-workspace evidence: `artifacts/release-0.15.2`.
- All 21 manifest-listed files were hash-checked, and every ZIP entry matches
  the staging directory. No game assets, developer docs or track experiments
  are included. ZIP first setup and repeat setup both exited successfully and
  preserved the supported game executables.
- The matching LAN archive contains `.deps/xlivelessness`, the integration
  patch and provenance. Its recorded DLL hash matches the payload, and its
  archive hash matches the installed source notice. Preserve the exact archive
  in both release and default branches; upload only installer, player ZIP and
  checksum file as release assets.

| Artifact | SHA-256 |
| --- | --- |
| `DiRT2VR-0.15.2-Setup.exe` | `42c346f1d71296004798209f186c2d913b094da7c458c9570c8968915425ab05` |
| `DiRT2VR-0.15.2.zip` | `5678edfd26e181b6b3b337a4213615f46f6e001695a9af6a14f848f050e7b9e9` |
| `SHA256SUMS.txt` | `82743b4f44e239d3386da0f7a2980234142c39d4a0b469128a8671a71bbbc421` |
| `DiRT2VR-0.15.2-LAN-source.zip` | `12d6a8ffab0304d4d7b11b8d977f4d0a168a755595f88ef8cfeeccac2e2c7d9b` |

## Checks

- Release native distribution: **18/18 CTests**, including reporting bounds,
  real WARP allocation with an OpenXR stub, and the inactive proxy path.
- Release launcher: **749 checks**, with zero compiler warnings/errors.
  Fixture: `artifacts/launcher-tests-20260927-223307` in the main workspace.
  A first run through a fixture junction was correctly rejected by linked-path
  protection; the successful run used the original physical game-fixture path.
- Earlier implementation checks: **772** launcher checks on the development
  branch, **81** cross-process resolution checks, **235** layout checks in each
  theme. Graphics and calibration screenshots were inspected. Synthetic
  dimensions and repeated launch preparation do not establish hardware results.
- Real Inno installer upgraded the user's isolated `artifacts/game` from
  **0.15.1 to 0.15.2**, exit **0**, no reboot. All installed payload hashes and
  the proxy ownership receipt match. The installed launcher initialized its
  hidden main window and was closed without launching the game.
- Before/after hashes cover **15,456** existing files. **15,430 protected files**
  remained identical, including game assets, saves, graphics XML, launcher
  preferences and recovery backups. Only **12** managed package files changed;
  no unexpected files were added or removed.

## Remaining acceptance

No VR or wheel session was run for this release. Compare actual source and eye
dimensions at 100/150/300% scene resolution with fixed FOV, then 50/100% headset
texture scale, using the same cockpit scene. Check clarity, stability and
graphics restoration. See [resolution diagnostics](launcher.md#requested-and-measured-dimensions-issues-2-and-3-follow-up).

For the deadzone report, record wheel model, launch mode and override state;
verify saved 0% steering on both directions across two actual launches with
overrides enabled and disabled. Retain unrelated bindings and Xbox calibration.
See [driving controls](driving-controls.md). No blanket zero-deadzone or
profile-loading change is included. Full interactive updater-to-installer
handoff remains untested for this version.

## Publication

Published [0.15.2](https://github.com/preseznik/DiRT2VR/releases/tag/v0.15.2)
as a normal release, with tag commit `c69d312b8f1fdd8a792e084e0eec4109842fc4bd`.
The anonymous latest-release API and web redirect both select 0.15.2. All three
uploaded asset digests match the local package; an anonymous download of the
tagged LAN source archive matches its recorded hash. The launcher's actual
HTTP client and update selector successfully detect the verified 0.15.2
installer from installed version 0.15.1 (`artifacts/release-0.15.2/update-check.log`).
Issues #2 and #3 were confirmed open after publication.
