# Launcher releases and updates

## Post-download responsiveness

The old updater performed its synchronous file flush/hash, recovery worker wait and shell installer startup on the WinForms thread. A user reported the alpha.5 launcher becoming unresponsive at the end of the download, with no other dialog. The exact blocking operation on that PC was not captured. All three potentially blocking phases now run off the UI thread, and the dialog labels verification, restoration and setup startup separately. Download progress is limited to changed percentages and the complete HTTP body transfer has a ten-minute cancellation deadline.

`PrepareInstallerAsync` acquires and releases the thread-affine session mutex within one background delegate. It restores graphics under the user's account, invokes the fixed-purpose file worker without hidden error dialogs, and propagates errors. Cancellation is checked before and after recovery; it does not terminate a worker midway through a journaled restoration. Recovery failure or cancellation prevents installer startup. Existing installed binaries cannot receive this fix until upgraded, so affected users must run the published installer directly once.

Regression tests deliberately block recovery while pumping a WinForms timer, verify the session guard remains held, cancel and verify release, reject competing sessions, propagate recovery errors and retry successfully. A synchronous HTTP fixture also verifies download/verification work leaves the UI thread, alongside the existing checksum, size and cancellation checks.

The Help / About panel reads the assembly informational version. Packaged builds also include their UTC build timestamp and Git revision. `launcher/DiRT2VR.vbproj` is the version source; Inno Setup and `package.json` receive that same version. Use a new SemVer version for every published update; metadata or a replacement asset under the same version is not considered newer.

## Publish

1. Determine the next distribution version with `tools/next-version.ps1` (patch by default; minor for a new feature); `tools/package.ps1` reserves that number when it runs. Before packaging, move the changes being shipped from **Unreleased** into a `## <version> — YYYY-MM-DD` changelog section and add its GitHub Release link. Keep unrelated unpublished work under **Unreleased**. Use the publication date, and label internal experiments as development notes rather than published releases. Update end-user instructions and commit the source so the embedded revision identifies it.
2. Run `tools/package.ps1` using the installed .NET, Visual Studio and Inno Setup tooling. Use `-SkipNativeBuild` only when the existing native distribution matches this source revision's native code.
3. Test the resulting installer/ZIP in an isolated supported game installation. Keep game files and generated assets out of the package.
4. Create a GitHub Release in `preseznik/DiRT2VR`, tagged `v<version>` at the tested source commit. Mark alpha builds as prereleases. Upload the exact `DiRT2VR-<version>-Setup.exe`, `DiRT2VR-<version>.zip` and `SHA256SUMS.txt` from the same package output directory. Publish release notes stating remaining acceptance limitations.
5. Confirm the changelog has a dated heading matching the published tag and no shipped changes still classified as **Unreleased**. Check the public Releases API: the installer asset must be fully uploaded and have a `sha256:` digest. Confirm the launcher detects it from an older version and test the complete setup handoff before advertising automatic updates as release-tested.

Do not commit distributable binaries to the source tree. The updater does not build source or execute files from a branch. It requires Release assets. The package script creates files locally; it does not tag, push or publish a release.

## Update behavior

- Checks are anonymous against `https://api.github.com/repos/preseznik/DiRT2VR/releases?per_page=100`, with a 20-second timeout. The launcher window starts one asynchronous check on `Shown`; quick launch, workers and game sessions do not. Alpha builds include prereleases; stable builds exclude them. No account credentials or local settings are sent. Drafts are ignored. The newest valid SemVer among those 100 entries wins, regardless of API ordering.
- A newer release shows a clickable indicator beside Help / About. That window receives the cached result, avoiding a duplicate request. Closing the launcher cancels its request; late results and offline/rate-limit failures cannot interrupt launching or open error dialogs. Manual retries still report errors, and the manual channel checkbox applies to that About window only.
- The asset must have the exact expected installer name, repository/tag download URL, uploaded state, positive size no larger than 512 MiB and SHA-256 digest. A newer release without these is reported with the Releases link, but cannot be installed by the launcher.
- Downloads go into a per-installation Local AppData `updates` directory with a unique name. Exact byte count and SHA-256 are verified before setup can run. Failed or canceled partial downloads are deleted. Completed installers are retained for troubleshooting. HTTPS and the digest protect delivery integrity; this is not Authenticode publisher signing.
- Before setup, the launcher checks that the game is closed, acquires the shared session mutex and restores graphics under the playing user's account plus game assets through the existing file worker. A running session or recovery conflict blocks the handoff.
- The existing interactive Inno installer starts with `/DIR=<current game folder>` and `/NORESTART`. The launcher then closes. Setup repeats game validation and recovery checks. Its normal prompts and Windows elevation remain visible. Declining elevation or canceling setup leaves the installed version intact. A ZIP installation becomes installer-managed if the user completes setup.
- There is no background polling, forced update, downgrade or silent self-replacement. Installing source archives and automatically updating from raw branch files are deliberately unsupported.

## Validation

Automated launcher tests cover SemVer precedence, channel filtering, draft/malformed releases, exact asset selection, download integrity/size failures, cancellation cleanup, API errors, Unicode/spaced installer arguments and About rendering in light/dark mode. Startup UI checks cover pending requests without blocked launch buttons, the indicator and cached About result, no-update/offline results, one request per window and cancellation on close. The real anonymous GitHub check succeeds. End-to-end interactive setup across public releases remains a separate acceptance check; existing isolated transaction and installer lifecycle tests cover the shared recovery/install paths separately.

See [GitHub's release API](https://docs.github.com/en/rest/releases/releases), [asset metadata](https://docs.github.com/en/rest/releases/assets) and [Inno Setup parameters](https://jrsoftware.org/ishelp/topic_setupcmdline.htm).
