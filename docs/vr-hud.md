# Cockpit HUD layer

The HUD uses a transparent OpenXR quad, four metres wide and four metres ahead of the recentered seated pose. Its height follows the game's render-target aspect ratio. The default pose stays in LOCAL space; because game eye transforms are relative to the car, the HUD stays with the car when the player looks or leans. `HudFollowView` instead places the quad ahead of the predicted head centre on each frame. Recenter updates the fixed reference. Neither setting changes normal desktop rendering.

## Capture and presentation

The renderer's main outer scene is RVA `0x33ad10`, called from `0x2889c0`, with prepared inner scene at `0x336be0`. Desktop captures established that HUD batches execute inside the inner scene, not after it returns. A capture started at either scene exit gets zero HUD draws.

The capture identifies five EGO UI vertex-shader hashes: `bef999561247f2d5`, `9d4dd795ddf7c656`, `47b5203eae84ab0f`, `edfda3c33aa2b9fc`, and `ce4f6cb0ec7d5697`. Reflection identifies image/ramp, distance-field text and lit gauge variants. Additional guards require a single original-backbuffer render target, immediate context and no depth-writing geometry. Stencil-only draws remain game-owned. Unsupported targets or shader variants are not redirected.

During the first eye, eligible HUD GPU draws go into a cleared transparent texture. Their normal colour output is omitted from both world-eye images. The second eye does not draw the HUD again. No game UI update or simulation function is repeated for the overlay. The capture preserves colour blending, accumulates coverage alpha, disables capture depth/stencil writes, and restores the original output-merger state after each draw.

After both world images, the same frame's HUD is copied to a third OpenXR swapchain. The copy unpremultiplies captured colour and applies the existing sRGB-to-linear conversion; the quad uses source-alpha blending and the unpremultiplied-alpha flag. Empty captures clear the quad, frame stamps reject stale textures, and virtual-screen frames suppress the overlay. The HUD is composited on top of the cockpit; it has binocular panel depth but does not participate in scene occlusion. The desktop VR mirror contains the world image without this separate compositor HUD.

This adds one game-sized capture texture and one runtime HUD swapchain. Runtime resolution scale also scales the HUD swapchain. Capturing the HUD performs no CPU readback during normal play. Original unsupported MSAA/array backbuffers are rejected; the launcher's VR baseline uses a single-sample backbuffer.

## Evidence and remaining checks

- `hud_capture_gpu` uses D3D11 WARP pixel readback to check transparent untouched pixels, coverage alpha, colour conversion, original backbuffer preservation, graphics-state restoration, second-eye suppression, rejection of offscreen/MRT/depth-writing draws and stale/menu frame rejection.
- `xr_frame_lifecycle` checks projection-plus-HUD ordering, distinct swapchain ownership, alpha flags, menu suppression, image acquisition failure and cleanup after a HUD copy exception. Camera-math tests distinguish fixed and following poses after a head turn and lean.
- Launcher checks cover migration/default-off behavior, persistence, session environment forwarding, Graphics-tab location and resetting defaults.
- Desktop diagnostic `artifacts/hud-probe-20260923-122525` captured 40 HUD draws at frame 3000 on Baja. `frame-923000.ppm` and its alpha PGM contain lap/time, position, route map and speedometer without scenery. This was a desktop benchmark capture, not a headset session. Diagnostic processes exited normally and the original proxy was restored; no camera/effects/graphics files were edited.

For bounded capture diagnostics only, `DIRT2VR_HUD_PROBE=1` together with logging and detailed captures duplicates the identified HUD draws in the main desktop scene at frames 300, 1200 and 3000, saving RGB/alpha images. The diagnostic leaves desktop HUD drawing intact and does not initialize OpenXR. Ordinary launcher sessions do not set this flag, and logging remains off by default.

The user deferred headset acceptance. Still required: readability at four metres, stable car-relative placement while looking/leaning, recenter, following mode, asymmetric eye projections, pause/resume and Toggle VR transitions, crop/resolution changes, other event HUD variants, long-session memory/performance and LAN VR. Desktop capture and synthetic-runtime tests do not establish those results.
