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
- Dependencies and addons: dependency resolution with cycle detection and install ordering, addons
  validated against the parent mod version and stored next to it, and an update plan that honours pins
  and SPT compatibility.
- Migration: adopt an existing SPT folder without moving a file, scan its mod folders by GUID (merging
  a "both halves" mod into one entry) and register the mods with their file hashes.
- Binary patching: finds the SPT deltas, plans one decision per file, backs up before patching and
  replaces the file by rename (never in place, so hard links into the base install are safe), refuses
  builds the delta does not support, repairs a damaged file from its backup, and restores on request.
- Export / import: a `.sptpack` is a few KB listing exact mod versions plus the config files you edited, with
  no binaries. Importing shows a plan first, downloads the exact versions from the Forge, checks the content
  against what the exporter had, and never overwrites a config you edited unless you say so. Packages are
  validated hard (zip-slip, symlinks, binaries, size bombs, tampering) before anything is written.
- Fixes found on real data: versions with build metadata (`1.8.0+h4`) no longer break a mod's version list;
  downloads are no longer named after their version; addons remember their Forge id.
- Linux runtime: finds Proton builds (Steam, Flatpak, Valve's own, your folders), `umu-run`, Wine and
  GameMode; plans how to run a program through `umu-run`, Proton or Wine and reports every problem
  (missing Proton, prefix on a read-only path, no `wine`, no BepInEx DLL override) with the exact path
  before anything starts; creates and repairs Wine prefixes without ever touching a folder that is not one;
  stores runtime profiles with their prefix and environment. On Windows the whole layer is inert.
- Fixes found on real data: every Proton build was listed twice (`~/.steam/steam` is a link); a healthy
  Wine prefix was reported broken because `wineboot` exits before the registry is written.
- Headless commands: `--migrate-only`, `--inspect-base <path>` and
  `--create-instance <name> --from <base> --into <dir>` and
  `--install-mod <archive> --instance <name>` and `--migrate-scan <instance>` and `--patch-plan|--patch|--restore <instance>` and `--export <instance> <file>` and
  `--import <file> --instance <name> [--apply]` and `--runtimes`, `--runtime-profile-add`, `--runtime-assign`,
  `--runtime-plan`, `--prefix-init`, `--runtime-run`.
- Git workflow: GitFlow, Conventional Commits hook, `X.Y.Z-pre-<sha>[.dirty]` versioning, `wt`
  worktrees.
- Documentation: per-phase plan under `docs/plan/`, verified SPT/Forge facts, decisions D1–D17.
