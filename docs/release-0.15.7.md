# 0.15.7 release validation

Published https://github.com/preseznik/DiRT2VR/releases/tag/v0.15.7 as the latest normal release. Tag commit: `6f11c87` on the clean distribution branch. Unrelated custom-track/Aspen work remains outside the release.

- User confirmed correct headset rendering at 150% and 200% in 0.15.6 and approved publication. All native payload hashes in 0.15.7 match that tested package.
- Release notes summarize player-visible changes since 0.15.2; development observations stay in technical documentation. The documentation-only repackage reserved patch version 0.15.7.
- Verified manifest, ZIP contents, matching LAN source, repeated ZIP setup, and uploaded SHA-256 digests. Release contains only Setup.exe, player ZIP and SHA256SUMS.txt.
- Local installer upgrade to artifacts/game exited 0. All installed file hashes match the manifest; 80 checked files covering executables, camera/effect assets, career saves, graphics XML and launcher preferences remained unchanged.
- The launcher's actual update client offers 0.15.7 to 0.15.2 and 0.15.6 and correctly treats 0.15.7 as current. GitHub latest-release API selects v0.15.7 with neither draft nor prerelease flags. Public tagged LAN source download matches its recorded checksum.

Evidence: `artifacts/release-0.15.7`; package: `artifacts/packages/0.15.7-20260928-000130` in the clean distribution checkout. Rendering and launcher regression details: [resolution fix](resolution-fallback.md).
