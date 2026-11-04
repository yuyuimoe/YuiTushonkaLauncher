# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Development builds are stamped
`X.Y.Z-pre-<sha>[.dirty]` by the build.

## [Unreleased]

### Added

- Solution split into `Core`, `Patching`, `Infrastructure`, `Ui` and `Tests` projects.
- Central package management (`Directory.Packages.props`), pinned toolchain
  (CSharpier 1.3.0, `dotnet-ef` 10.0.14) and the official launcher's code style.
- Startup composition: dependency injection, Serilog console + rolling file logging, single-instance
  guard, `IAppPaths` filesystem layout and JSON-backed `ISettingsService`.
- Persistence: SQLite + EF Core with `InitialCreate` migration, entity set for instances, catalog,
  addons, dependencies, shareable lists, runtime/server profiles; `IUnitOfWork` with commit/rollback;
  settings moved into the database; `DatabaseInitializer` runs migrations at startup.
- Value objects: `SemanticVersion` and `VersionRange` (caret/tilde/comparator/bracket ranges) with the
  real-world constraint shapes covered by tests; `coverlet.runsettings` documents coverage exclusions.
- Headless `--migrate-only` command for CI smoke tests and troubleshooting.
- Git workflow: GitFlow, Conventional Commits hook, `X.Y.Z-pre-<sha>[.dirty]` versioning, `wt`
  worktrees.
- Documentation: per-phase plan under `docs/plan/`, verified SPT/Forge facts, decisions D1–D17.
