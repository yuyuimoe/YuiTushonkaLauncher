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
- Forge API client: typed endpoints over `filter[...]`/`query=`, burst + sustained rate limiting under
  the advertised 300/min, `cache-control`-aware response cache, retries with `Retry-After` and jitter,
  archive downloads with redirect following, `Range` resume and streaming SHA-256, plus the SPT
  release catalogue sync.
- Instances: hard-link based creation from an SPT install (per-path link policy, `Assembly-CSharp.dll`
  always copied), copy-on-write writer, layout detection (4.0 vs 4.1+), SPT/Tarkov version probing,
  patch-state detection, rename/delete, pre-flight checks (same volume, free space, link probe) and
  rollback on failure.
- Mods: identity read from assembly metadata (`[BepInPlugin]` and `IModMetadata` literals, no code
  execution), archive inspection and zip-slip-proof extraction, game-root → instance path mapping,
  install/uninstall/pin with recorded hashes, and an integrity check that flags edited configs and
  missing files.
- Headless commands: `--migrate-only`, `--inspect-base <path>` and
  `--create-instance <name> --from <base> --into <dir>` and
  `--install-mod <archive> --instance <name>`.
- Git workflow: GitFlow, Conventional Commits hook, `X.Y.Z-pre-<sha>[.dirty]` versioning, `wt`
  worktrees.
- Documentation: per-phase plan under `docs/plan/`, verified SPT/Forge facts, decisions D1–D17.
