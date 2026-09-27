# Graphics refresh correction (0.15.3)

The 0.15.2 resolution poll assigned a placeholder to the auto-sized label before assigning the saved report on every one-second refresh. This produced two text changes and layout passes even when the report was unchanged. Compute the description first and assign it only when different. New reports, invalid reports and missing reports still update.

The repeated-poll regression failed against 0.15.2. The corrected distribution source passes 243 responsive checks in each Windows Forms theme, including actual timer ticks with stable label text/bounds, report replacement and malformed/missing recovery, plus wide/portrait/compact layouts. Checks run off-screen; no game or headset was launched.

Restore the shorter borderless caption under the Desktop heading to avoid redundant wrapping and extra vertical scrolling. Correct the published 0.15.2 title to include its feature summary and simplify the release notes and changelog. No resolution enforcement or wheel input behavior is changed by this patch; those hardware reports remain open.

The standard package script reserved 0.15.3. ZIP setup/repeat setup, manifests, source provenance and asset checksums pass. All native payloads match 0.15.2. Inno installed into artifacts/game with exit 0; 15,479 protected game/save/settings files remained unchanged. Evidence is under artifacts/graphics-refresh-0.15.3. This local patch is not yet published.
