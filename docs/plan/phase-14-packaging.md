# Phase 14 — Packaging and distribution

**Branch:** `feature/phase-14-packaging` · **Depends on:** all previous.

## Goal

Artifacts a player can download and run: Windows installer + Linux package, both framework-dependent
(.NET 10 is guaranteed by SPT itself) with a self-contained fallback.

## Tasks

- [ ] `dotnet publish` matrix: `win-x64` and `linux-x64`, framework-dependent (primary) and
      self-contained (fallback). `PublishSingleFile` where Avalonia allows it; **no trimming/AOT**
      ([D14](../plan/04-decisions.md#d14-no-native-aot)).
- [ ] Windows: MSI via WiX (per-user install, no admin) or an Inno Setup installer — decide by which
      handles the "SPT already installed elsewhere" case better; add `.sptpack` file association and a
      Start-menu shortcut.
- [ ] Linux: AppImage (primary) and `tar.gz`; `.desktop` entry, icon, MIME XML for `.sptpack`,
      `AppStream` metadata.
- [ ] Version plumbing: tag → `-p:Version=X.Y.Z`, `FileVersion`, `InformationalVersion`, about-box.
- [ ] Update check: compare the running version against the latest GitHub release of this repo (no
      silent self-update in 1.0; notify + link).
- [ ] GitHub Actions: build+test on PR; on tag → publish artifacts, attach to release, generate release
      notes from Conventional Commits (grouped by type).
- [ ] Release checklist in the repo (checksums published, `CHANGELOG.md` finalized, migration notes
      for database/instance format changes).
- [ ] Code signing: document what is needed for Windows (Authenticode) even if not purchased yet;
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
