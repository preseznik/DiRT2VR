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

## Engine fix, 2026-09-27

Disassembly corrects the earlier display-mode-list hypothesis: windowed validation at RVA `0xac3b52` compares the XML dimensions with `GetSystemMetrics(0/1)` (desktop width/height). An oversized request fails and is replaced by 1280 × 720. The D3D11 proxy loads after that decision.

At the verified `D3D11CreateDeviceAndSwapChain` call (return RVA `0xd29b55`), the proxy now sets the renderer's width/height, windowed dimensions and resident swapchain descriptor together, before the engine allocates its colour/depth targets. This is a process-local data change, gated by VR activation, the supported executable hash, call site and renderer layout. It changes neither executable bytes nor Windows display modes. Desktop startup clears the requested VR dimensions. Existing graphics recovery remains in use.

Bounded isolated-copy tests with the actual game:

- `artifacts/scene-resolution-2400x1800-234502`: fallback 1280 × 720 corrected to 2400 × 1800; colour targets, including 2x MSAA HDR, render at that size.
- `artifacts/scene-resolution-3200x2400-234725`: fallback corrected to 3200 × 2400; scene colour targets, depth buffer and draw-time viewport agree. Captured frame 1200 is 3200 × 2400.
- These are rendering/allocation checks, not headset image-quality or sustained performance acceptance. The runtime was not visible/focused, so eye submission is not claimed. The diagnostic restored the original proxy and graphics XML bytes after each run.

## Launcher layout fix

The Launcher regression reproduced 84 sibling control resizes, including temporary dropdown widths around 65,000 pixels. Responsive containers returned the unconstrained measurement width as their preferred size, causing a real resize before layout corrected it. They now return their current managed width; opponent descriptions also keep allocated bounds. Launcher slider and opponent-choice changes cause zero sibling resizes. Both light and dark layout suites pass 577 checks. Human flashing confirmation remains pending.

## Local delivery

The package script reserved 0.15.4. Package hashes, source provenance, ZIP setup/repeat setup and the installer pass. Deployment to artifacts/game exited 0 and preserved 15,551 protected files, including game data, saves and settings. Native payloads match 0.15.3; only launcher/readout behavior and documentation change. Evidence: artifacts/slider-redraw-0.15.4. This patch has not been published as a GitHub release.
