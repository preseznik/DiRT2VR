# Launcher releases and updates

The Help / About panel reads the assembly informational version. Packaged builds also include their UTC build timestamp and Git revision. `launcher/DiRT2VR.vbproj` is the version source; Inno Setup and `package.json` receive that same version. Use a new SemVer version for every published update; metadata or a replacement asset under the same version is not considered newer.

## Publish

1. Update the project version (and the installer fallback), changelog and end-user instructions. Commit the source so the embedded revision identifies it.
2. Run `tools/package.ps1` using the installed .NET, Visual Studio and Inno Setup tooling. Use `-SkipNativeBuild` only when the existing native distribution matches this source revision's native code.
3. Test the resulting installer/ZIP in an isolated supported game installation. Keep game files and generated assets out of the package.
4. Create a GitHub Release in `preseznik/DiRT2VR`, tagged `v<version>` at the tested source commit. Mark alpha builds as prereleases. Upload the exact `DiRT2VR-<version>-Setup.exe`, `DiRT2VR-<version>.zip` and `SHA256SUMS.txt` from the same package output directory. Publish release notes stating remaining acceptance limitations.
5. Check the public Releases API: the installer asset must be fully uploaded and have a `sha256:` digest. Confirm the launcher detects it from an older version and test the complete setup handoff before advertising automatic updates as release-tested.

Do not commit distributable binaries to the source tree. The updater does not build source or execute files from a branch. It requires Release assets. The package script creates files locally; it does not tag, push or publish a release.

## Update behavior

- Checks are manual and anonymous against `https://api.github.com/repos/preseznik/DiRT2VR/releases?per_page=100`, with a 20-second timeout. No account credentials or local settings are sent. Drafts are ignored. Stable-only mode also excludes prerelease tags. The newest valid SemVer among those 100 entries wins, regardless of API ordering.
- The asset must have the exact expected installer name, repository/tag download URL, uploaded state, positive size no larger than 512 MiB and SHA-256 digest. A newer release without these is reported with the Releases link, but cannot be installed by the launcher.
- Downloads go into a per-installation Local AppData `updates` directory with a unique name. Exact byte count and SHA-256 are verified before setup can run. Failed or canceled partial downloads are deleted. Completed installers are retained for troubleshooting. HTTPS and the digest protect delivery integrity; this is not Authenticode publisher signing.
- Before setup, the launcher checks that the game is closed, acquires the shared session mutex and restores graphics under the playing user's account plus game assets through the existing file worker. A running session or recovery conflict blocks the handoff.
- The existing interactive Inno installer starts with `/DIR=<current game folder>` and `/NORESTART`. The launcher then closes. Setup repeats game validation and recovery checks. Its normal prompts and Windows elevation remain visible. Declining elevation or canceling setup leaves the installed version intact. A ZIP installation becomes installer-managed if the user completes setup.
- There is no background polling, forced update, downgrade or silent self-replacement. Installing source archives and automatically updating from raw branch files are deliberately unsupported.

## Validation

Automated launcher tests cover SemVer precedence, channel filtering, draft/malformed releases, exact asset selection, download integrity/size failures, cancellation cleanup, API errors, Unicode/spaced installer arguments and About rendering in light/dark mode. The real anonymous GitHub check succeeds; at implementation time the repository has no published releases. End-to-end download/setup across two public releases therefore remains unverified. Existing isolated transaction and installer lifecycle tests cover the shared recovery/install paths separately.

See [GitHub's release API](https://docs.github.com/en/rest/releases/releases), [asset metadata](https://docs.github.com/en/rest/releases/assets) and [Inno Setup parameters](https://jrsoftware.org/ishelp/topic_setupcmdline.htm).
