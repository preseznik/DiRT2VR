# Slider redraw and resolution fallback investigation

## Graphics controls

Changing the AutoSize requested-resolution label caused unrelated native sliders to resize twice per value change; the regression measured 60 sibling resizes across ten Render resolution changes. The MSAA warning had the same problem. Both labels now retain the bounds assigned by SettingRow: text changes repaint only their own readouts, while window resizing still measures their preferred size.

The regression exercises every Graphics slider across wide, portrait and compact layouts, verifies no unrelated trackbar or dropdown resize/invalidation, checks readout text fits, and retains timer stability coverage. The pre-fix checks failed on RenderScale, then on VrMsaa after isolating the first cause. All 558 checks pass in both themes, including the clean distribution launcher build. Screenshots and test logs are under artifacts/slider-redraw-* and the timestamped launcher-test folders. Human confirmation of the visible flashing remains pending.

## User runs, 2026-09-27

Evidence is in `%LOCALAPPDATA%/DiRT2VR/AB7025AA8C9222C378C5D88E/logs`. All rows below have full FOV. `trace.log` records `VR resolution requested=... backbuffer=... left=... right=... source_match=...`; zero eye dimensions on the first line are the pre-OpenXR startup observation, not the final sizes.

| Local session | Requested scene | Actual game backbuffer | Per-eye OpenXR texture |
| --- | --- | --- | --- |
| 224949-219 | 4800 × 3600 (300%) | 1280 × 720 | 3072 × 3264 |
| 225910-775 | 2400 × 1800 (150%) | 1280 × 720 | 1536 × 1632 |
| 230150-583 | 1600 × 1200 (100%) | 1600 × 1200 | 3072 × 3264 |
| 230327-590 | 2400 × 1800 (150%) | 1280 × 720 | 3072 × 3264 |
| 230437-966 | 2400 × 1800 (150%) | 1280 × 720 | 3072 × 3264 |
| 230531-157 | 3200 × 2400 (200%) | 1280 × 720 | 3072 × 3264 |
| 230627-985 | 800 × 600 (50%) | 800 × 600 | 3072 × 3264 |

The source mismatch exists on the first observed Present and persists: increasing headset texture scale successfully increases the output texture, but still scales a 720p game image. In `src/trace.cpp`, each eye renders through `realInner` and captures the game backbuffer before `CopyEye` presents it, so the backbuffer is part of the actual stereo image path, not just an unrelated desktop mirror.

A read-only Windows display-mode enumeration on this PC includes 800 × 600 and 1600 × 1200, but excludes the failed 2400 × 1800, 3200 × 2400 and 4800 × 3600 sizes. The list also includes 1920 × 1440 and 2048 × 1536. This strongly suggests game-side validation against display modes. It does not yet establish the exact validation instruction or whether an additional size restriction applies. `hardware_settings_restrictions.xml` only supplies minimum size and aspect constraints, not this upper-size fallback.

Next: trace requested-size selection before swapchain creation, compare a supported larger 4:3 mode with the failed arbitrary sizes, and apply a guarded VR-only change at the engine's resolution-selection point. Verify scene/depth target sizes and viewports as well as the backbuffer. Enlarging only the final OpenXR texture or forcing only the swapchain dimensions would not establish higher-resolution scene rendering. Preserve desktop behavior and existing graphics recovery. No resolution-enforcement patch is included here.

## Local delivery

The package script reserved 0.15.4. Package hashes, source provenance, ZIP setup/repeat setup and the installer pass. Deployment to artifacts/game exited 0 and preserved 15,551 protected files, including game data, saves and settings. Native payloads match 0.15.3; only launcher/readout behavior and documentation change. Evidence: artifacts/slider-redraw-0.15.4. This patch has not been published as a GitHub release.
