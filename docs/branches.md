# Branch and release workflow

- `main` is the default branch and the source of tested releases. Ordinary fixes start here, using a temporary branch when isolation is useful.
- `preseznik/custom-track-development` holds unfinished custom-track work. Bring tested main changes into this branch as needed; do not independently duplicate release fixes in both histories. Merge its completed features into main only after review and validation.
- Retire temporary branches after their changes are incorporated. Preserve unique local work and diagnostic evidence before removing a checkout.
- A release tag identifies its exact source commit. Publish packages built from the intended main commit, commit the reserved version and matching library source, then tag and upload the verified assets. Branch housekeeping must not move existing tags or replace published downloads.

## Local checkout locations

The repository was reorganized on 2026-09-28 without moving working files:

| Checkout | Purpose |
| --- | --- |
| `C:/Users/iztok/.codex/worktrees/issue-diagnostics-release/DiRT2VR` | Active main/release checkout; the folder keeps its historical name. |
| `F:/Coding/Codex/Mods/DiRT2VR` | Custom-track development; contains unfinished local Aspen changes. Use the main checkout above for ordinary release fixes. |
| `artifacts/controls-package-src` under the development checkout | Detached historical build checkout at `45a0ce3`; retained and locked to protect packages, diagnostics and dependency links. |
| `artifacts/pc3-package-src` under the development checkout | Detached historical build checkout at `3220f9a`; retained and locked for the same reason. |

The former `preseznik/driving-controls` and `preseznik/pc3-gfwl-compatibility` branches had no unique committed work outside main and were removed locally and remotely. Their detached folders are archives, not active development branches. No game files or build evidence were deleted. All pre-existing release tags and release asset identities/checksums were verified unchanged; 0.15.7 remains the latest release.

The former `preseznik/issue-diagnostics-release` branch was renamed to `main`; `preseznik/native-vr-prototype` was renamed to `preseznik/custom-track-development`. GitHub and local origin/HEAD now point to main. Existing development history includes earlier separately copied fixes; reconcile that history carefully when the track work is eventually ready to merge, with local work preserved first.
