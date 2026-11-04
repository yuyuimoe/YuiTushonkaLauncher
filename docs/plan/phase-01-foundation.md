# Phase 1 — Foundation

**Branch:** `feature/phase-01-foundation` · **Depends on:** nothing · **Unlocks:** everything.

**Status:** ☑ done. Delivered: 5-project `.slnx`, central package management, CSharpier + official
launcher code style, DI/Serilog startup, `IAppPaths`, `ISettingsService` (JSON for now), single
instance guard, CI (format + build + test), README/CHANGELOG.

## Goal

Turn the single-project Avalonia scaffold into a solution with the five projects, DI, logging,
configuration and a window that proves the wiring works.

## Tasks

- [x] Create `YuiTushonkaLauncher.slnx` with `Core`, `Patching`, `Infrastructure`, `Ui`, `Tests`.
      Move the existing app files into `Ui` keeping the assembly name `YuiTushonkaLauncher`.
- [x] `Directory.Packages.props` for central package management; pin versions (Avalonia 12.1.5,
      CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions.*, Serilog, EF Core sqlite,
      SharpHDiffPatch.Core, SharpCompress, xunit).
- [x] Composition root in `Program.cs` / `App.axaml.cs`: `Microsoft.Extensions.DependencyInjection` + `Serilog`
      (console sink in Debug, rolling file under the app data dir), `IOptions` binding.
- [x] `IAppPaths` (app data, cache, logs, instances root) resolved once and injected — no static
      path constants scattered around.
- [x] `LauncherSettings` + `ISettingsService` with sensible defaults (instance root, download cache,
      log retention, language).
- [x] Single-instance guard (mutex on Windows, lock file elsewhere) so two copies cannot fight over
      the same instance folder.
- [x] `MainWindow` shows version (`AssemblyInformationalVersion`) and the resolved paths; proves DI,
      logging and versioning.
- [x] `GenerateDocumentationFile` everywhere; code style enforced in build (`EnforceCodeStyleInBuild`).
      `TreatWarningsAsErrors` is still off: turn it on once phase 2 lands without warnings.
- [x] `CHANGELOG.md` (Keep a Changelog) seeded with `Unreleased`.
- [x] `README.md` with build/run/test commands and the code-style section.
- [x] `.config/dotnet-tools.json` pinning CSharpier and `dotnet-ef`; `.github/workflows/ci.yml` with a
      format gate (`dotnet csharpier check .`) plus build/test on Linux and Windows.

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
- `JsonSettingsService` is a bootstrap implementation: phase 2 moves settings into SQLite and the
  registration changes in one place ([D2](../plan/04-decisions.md#d2-ef-core-over-dapper-with-sqlite)).
- Formatting is owned by CSharpier, semantic preferences by `.editorconfig` — both copied from the
  official launcher ([D18](../plan/04-decisions.md#d18-code-style-is-the-official-launchers)).
