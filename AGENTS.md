# Repository guidance

- Keep changes small, readable and scoped to the requested behavior. Preserve unrelated work.
- Keep `README.md` exclusively for end users: requirements, setup, play instructions, controls, troubleshooting and current limitations. Put build steps, reverse-engineering details and diagnostic evidence in `docs/`.
- Update `CHANGELOG.md` with meaningful behavior, setup, compatibility or documentation changes in the same change. Add new entries under `Unreleased`; use dated milestones or versions only when established, and never imply an untested feature is verified.
- Keep end-user instructions consistent with the actual launcher. Link to technical documentation rather than duplicating it in the README.
- Use plain `major.minor.patch` versions, without alpha/beta suffixes. Every new distribution build increments patch through `tools/package.ps1`; use `-VersionBump Minor` when adding a completely new feature (resetting patch), and `-VersionBump Major` only when the user explicitly requests a major bump (resetting minor and patch). Internal compile/test passes within that build retain its version. Commit the version reserved by packaging; failed package attempts do not reuse their number. Publish future releases without GitHub's prerelease flag, while retaining accurate feature limitations.
