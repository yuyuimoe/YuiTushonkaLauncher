# Release checklist

Run through this for every tagged release. It exists because the last step of a release is the one everybody skips.

## Before the tag

1. **`develop` is green and merged into `main`.** `dotnet build -c Release` with zero warnings, `dotnet test`
   green on the merged commit, `dotnet csharpier check .` clean.
2. **Coverage gate passes.** `dotnet test ... --collect:"XPlat Code Coverage"` then
   `dotnet run --project tools/YuiTushonkaLauncher.Tools.CoverageGate -c Release -- --report TestResults --min 90`.
3. **`CHANGELOG.md` is finalised.** The version heading exists, entries are grouped by type, and the date is the
   release date, not the day the entry was written.
4. **Migration notes.** Anything that changes the database schema, the instance layout or the `.sptpack` format is
   called out with what an existing user must do (usually: nothing — migrations run on start — but say so).
5. **`docs/plan/03-verified-facts.md` and `91-before-1.0.md`** say what is still unverified for this release. A release
   whose known gaps are written down is a release a user can trust.
6. **Version bump.** `VersionPrefix` in `Directory.Build.props` matches the tag, so a local build and the release agree.

## Building

7. **Tag.** `git tag -a vX.Y.Z -m "vX.Y.Z"` on the release commit, then `git push origin vX.Y.Z`. The tag triggers
   `.github/workflows/release.yml`.
8. **Artifacts produced by the workflow:**
   - `YuiTushonkaLauncher-X.Y.Z-win-x64-setup.exe` (Inno Setup, per-user, no admin, `.sptpack` association, shortcut)
   - `YuiTushonkaLauncher-X.Y.Z-win-x64.zip` (framework-dependent) and `-selfcontained.zip`
   - `YuiTushonkaLauncher-X.Y.Z-x86_64.AppImage`, `-linux-x64.tar.gz`, `-linux-x64-selfcontained.tar.gz`
   - `SHA256SUMS`
9. **Re-run the workflow once for the same tag** and confirm the file names and the test result are identical.

## After the tag

10. **Install the MSI on a Windows 11 machine that has only .NET 10** (as SPT requires). Launch it, close it, uninstall
    it, and confirm `%LOCALAPPDATA%\YuiTushonkaLauncher` survives the uninstall (it holds the database and logs).
11. **Run the AppImage on a clean Linux install**: launch, close, and double-click a `.sptpack` to see the import
    dialog open the launcher with the file.
12. **`--version`** on every artifact prints the tag.
13. **Update check** against the new release: `YuiTushonkaLauncher --check-updates` says no newer release, and an older
    build says the tag is available.
14. **Attach the manual QA matrix** (`docs/qa/manual-matrix.md`) to the release PR, with this release's rows filled in.
15. **Publish the checksums** in the release body (the workflow does it) and nowhere else: a checksum next to the file it
    signs is not a signature.

## Known gaps for 1.0 (see `docs/plan/91-before-1.0.md`)

- Windows has not been run on real hardware yet; the job-object path of the detached pair is untested.
- No code signing certificate (see the section below), so SmartScreen will warn on first run.
- Byte identity with the official launcher's output is unverified.

## Code signing

Windows will show "Windows protected your PC" until the binary is Authenticode-signed. What that needs:

- An **OV or EV code-signing certificate** (EV gives immediate SmartScreen reputation; OV accrues it over time), or a
  cloud signing service (Azure Trusted Signing, SignPath) if you would rather not hold a `.pfx`.
- `signtool sign /fd SHA256 /tr <timestamp-url> /td SHA256 /a <file>` for the app executable **and** the installer, in
  that order, and the uninstaller that Inno Setup generates.
- The workflow needs the certificate and its password as secrets (`WINDOWS_CERT_PFX`, `WINDOWS_CERT_PASSWORD`) and a
  step before the artifact upload. Nothing else in the build changes.
- Timestamping matters more than the algorithm: without it, every signature expires with the certificate.

Linux has no equivalent registry; publishing `SHA256SUMS` and, later, signing it with a GPG key is the equivalent
statement.
