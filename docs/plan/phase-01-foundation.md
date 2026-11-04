# Phase 1 — Foundation

**Branch:** `feature/phase-01-foundation` · **Depends on:** nothing · **Unlocks:** everything.

## Goal

Turn the single-project Avalonia scaffold into a solution with the five projects, DI, logging,
configuration and a window that proves the wiring works.

## Tasks

- [ ] Create `YuiTushonkaLauncher.slnx` with `Core`, `Patching`, `Infrastructure`, `Ui`, `Tests`.
      Move the existing app files into `Ui` keeping the assembly name `YuiTushonkaLauncher`.
- [ ] `Directory.Packages.props` for central package management; pin versions (Avalonia 12.1.5,
      CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions.*, Serilog, EF Core sqlite,
      SharpHDiffPatch.Core, SharpCompress, xunit).
- [ ] Generic host in `Program.cs`: `Microsoft.Extensions.DependencyInjection` + `Serilog`
      (console sink in Debug, rolling file under the app data dir), `IOptions` binding.
- [ ] `IAppPaths` (app data, cache, logs, instances root) resolved once and injected — no static
      path constants scattered around.
- [ ] `Settings` entity + `ISettingsService` with sensible defaults (instance root, download cache,
      log retention, language).
- [ ] Single-instance guard (mutex on Windows, lock file elsewhere) so two copies cannot fight over
      the same instance folder.
- [ ] `MainWindow` shows version (`AssemblyInformationalVersion`) and the resolved paths; proves DI,
      logging and versioning.
- [ ] `TreatWarningsAsErrors` on for the four non-UI projects; `GenerateDocumentationFile` everywhere.
- [ ] `CHANGELOG.md` (Keep a Changelog) seeded with `Unreleased`.
- [ ] `README.md` with build/run/test commands.

## Acceptance criteria

- `dotnet build -c Release` and `dotnet test` succeed on Windows and Linux.
- App starts, logs to file, and displays `0.1.0-pre-<sha>` (or `.dirty`).
- Starting it twice shows a friendly "already running" message instead of a second process.
- No project references from `Core`/`Patching` to `Infrastructure`/`Ui` (verify with
  `dotnet list reference`).

## Notes

- The repo already pins Avalonia 12.1.5 + Mvvm 8.4.2; do not downgrade to match the official
  launcher's 12.1.4.
- `ViewLocator` (reflection) stays for now; it is replaced in Phase 12
  ([D14](../plan/04-decisions.md#d14-no-native-aot)).
