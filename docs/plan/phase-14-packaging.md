# Phase 14 — Packaging and distribution

**Branch:** `feature/phase-14-packaging` · **Depends on:** all previous.

## Goal

Artifacts a player can download and run: Windows installer + Linux package, both framework-dependent
(.NET 10 is guaranteed by SPT itself) with a self-contained fallback.

## Tasks

- [x] `dotnet publish` matrix: `win-x64` and `linux-x64`, framework-dependent (primary) and
      self-contained (fallback). `PublishSingleFile` where Avalonia allows it; **no trimming/AOT**
      ([D14](../plan/04-decisions.md#d14-no-native-aot)).
- [x] Windows: installer (Inno Setup chosen, see below) (per-user install, no admin) or an Inno Setup installer — decide by which
      handles the "SPT already installed elsewhere" case better; add `.sptpack` file association and a
      Start-menu shortcut.
- [x] Linux: AppImage (primary) and `tar.gz`; `.desktop` entry, icon, MIME XML for `.sptpack`,
      `AppStream` metadata.
- [x] Version plumbing: tag → `-p:Version=X.Y.Z`, `FileVersion`, `InformationalVersion`, about-box.
- [x] Update check: compare the running version against the latest GitHub release of this repo (no
      silent self-update in 1.0; notify + link).
- [x] GitHub Actions: build+test on PR; on tag → publish artifacts, attach to release, generate release
      notes from Conventional Commits (grouped by type).
- [x] Release checklist in the repo (checksums published, `CHANGELOG.md` finalized, migration notes
      for database/instance format changes).
- [x] Code signing: document what is needed for Windows (Authenticode) even if not purchased yet;
      publish SHA-256 checksums either way.

## Acceptance criteria

- A clean Windows 11 machine with only .NET 10 (as SPT requires) installs the MSI and launches the app.
- A clean Linux distro runs the AppImage, opens a `.sptpack` from the file manager, and shows the
  import dialog.
- Artifacts report the tagged version everywhere (about box, file properties, `--version`).
- Release workflow is reproducible: re-running it for the same tag produces the same file names and
  passing tests.

## Notes

- Framework-dependent is the default because SPT already mandates .NET 10; the self-contained build is
  insurance, not the primary download.
- Keep the `.sptpack` MIME registration in the installer, not in the app (needs no admin on Linux via
  `xdg-mime`, but does on Windows registry — the MSI handles both).

## Delivered

- **`scripts/publish.sh`** — one script, both platforms, framework-dependent and self-contained (no trimming, no AOT,
  D14). Single-file with `IncludeNativeLibrariesForSelfExtract` (compression only for the self-contained bundles, which
  is where the SDK allows it). It also packs: `tar.gz` for Linux with `install.sh` + `.desktop` + icon + MIME + AppStream,
  a zip for Windows, an AppDir, and `SHA256SUMS`; with `appimagetool` on `PATH` it also produces the `.AppImage` and
  otherwise keeps the AppDir as a tarball and says so.
- **Windows installer**: **Inno Setup**, not WiX. Reasons: per-user install with no admin is a one-line directive
  (`PrivilegesRequired=lowest`), the `.sptpack` association is four HKCU registry lines with `uninsdelete*`, the installer
  can be built on `windows-latest` with a stock package, and the "SPT is already installed elsewhere" question does not
  exist at install time (the launcher asks for the folder on first run). WiX would have needed a file-harvesting step for
  a single-file payload.
- **Version plumbing**: a tag is the version (`-p:GitVersion=false`), so `--version`, the about box, the window title,
  the MSI `VersionInfoVersion` and the release notes all say the same thing. `--version` answers before the database,
  the log file or Avalonia exist. Verified: publishing with `--version v0.9.9` prints `0.9.9` from the artifact.
- **Update check**: `IUpdateChecker` + `GitHubReleaseChecker` (GitHub `releases/latest`, unauthenticated, 15 s timeout,
  own `HttpClient` with a user agent), the `--check-updates` command (exit 1 when the check itself failed, 0 when it
  answered) and a manual "Check for updates" button in Settings with the release link. A development build is never told
  to update — it is ahead of the last release by construction. 19 tests against WireMock.
- **Branding**: one SVG source (`packaging/branding/icon.svg`) rendered to the window/executable icon and to the 256 px
  PNG the desktop entry and AppStream use. It replaces the Avalonia logo placeholder.
- **CI**: `release.yml` on `v*` — Linux job (tests, publish, AppImage), Windows job (publish, ISCC), release job
  (checksums, notes from Conventional Commits, attach everything). `scripts/release-notes.sh` groups commits by type and
  emits the artifact table.
- **`docs/release-checklist.md`**: the pre-tag, build and post-tag steps, the two re-run checks that make a release
  reproducible, and what Authenticode signing would need.

## Verified here, and what still needs a machine

Verified on this machine: the publish matrix, single-file artifacts, `--version` from an artifact, the tarball
contents (`install.sh`, desktop entry, icon, MIME XML, metainfo), the AppDir `AppRun`, `appstreamcli validate`
(passes), `--check-updates` against the real GitHub API (404 "no release yet" — the repository has no releases),
and the version-from-tag path.

Not verified here, and listed in the checklist: the MSI on Windows 11 and the AppImage on a clean Linux install.
`appimagetool` is an AppImage itself and could not be fetched from this environment (the download timed out), so the
AppImage wrapper is built by the Linux CI job; the AppDir it is made from was built and validated here.
