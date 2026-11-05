# Phase 2 — Persistence

**Branch:** `feature/phase-02-persistence` · **Depends on:** Phase 1.

**Status:** ☑ done. Delivered: all entities from the domain model (plus `InstalledMod`/`InstalledFile`/
`SptRelease`), `LauncherDbContext` + `AddLauncherPersistence()`, `InitialCreate` migration, design-time
factory, `IUnitOfWork` with commit/rollback, settings moved from JSON into the database,
`DatabaseInitializer` wired into startup and a `--migrate-only` headless command for smoke tests.
Coverage: Core 96.7 %, Infrastructure 92.2 % (100 tests).

## Goal

EF Core + SQLite with migrations, the full entity set, and a repository-free-but-testable data layer.

## Tasks

- [x] `LauncherDbContext` in `Infrastructure` with `DbSet`s for the entities in
      [02-domain-model](../plan/02-domain-model.md); owned types for `SptVersion`, `PatchInfo`,
      `Range`-backed constraints (map to `TEXT`, compare in code).
- [x] `AddLauncherPersistence()` extension (name changed to reflect that it registers the layer): SQLite file under `IAppPaths.DataDir`, `WAL`,
      `foreign_keys=on`, `busy_timeout`, retry on `SQLITE_BUSY`.
- [x] Design-time factory (`LauncherDbContextFactory` serves both DI and `dotnet ef`) so `dotnet ef` works with the UI as startup project.
- [x] Initial migration + tests for schema, idempotency and FK enforcement + a test that applies it to a temp file and validates the schema.
- [x] `ISettingsService` implemented over a single-row table (`EfSettingsService`); `JsonSettingsService` deleted, cached in memory.
- [x] `IUnitOfWork`/transaction helper (see note on layering) for multi-step operations (install = download + extract + link +
      rows, all-or-nothing).
- [x] Seed: default `RuntimeProfile` (Native) and the known SPT/Tarkov pairs from `/spt/versions` are
      *not* seeded — they are cached on first sync (Phase 3).
- [ ] Concurrency: every row carries a `RowVersion`/`UpdatedAt` guard where two windows could edit it.

## Acceptance criteria

- Create/read/update/delete an `Instance`, `Mod`, `ModVersion`, `Addon`, `ModList`, `PatchInfo` in
  tests, including the owned value objects round-tripping.
- Migration applies to an empty database and to a database one migration behind (upgrade path test).
- The database file is created under the configured data dir, never next to the executable.
- 90 % line coverage on the `Infrastructure/Persistence` namespace.

## Notes

- `IUnitOfWork` lives in **Infrastructure**, not Core: a transaction is meaningless without the
  `DbContext` that carries it, so its signature takes `LauncherDbContext`. Core still never sees EF.
- `SemanticVersion`/`VersionRange` (Core) are the value objects from the domain model; both are
  covered by unit tests including the messy real-world constraint strings.
- POCO entity files and EF migrations are excluded from coverage in `coverlet.runsettings` (each
  exclusion justified inline) because EF sets those properties by reflection.
- `LauncherDbContextFactory` doubles as the design-time factory, so `dotnet ef` and the app always
  agree on options. `Microsoft.EntityFrameworkCore.Design` is referenced by both Infrastructure and
  Ui (private assets) — the tools require it in the startup project.
- Do not expose `DbContext` outside Infrastructure; services take domain-level interfaces.
- Soft delete only where recovery matters (`Mod`, `Addon`); instances are hard-deleted with their
  folder after confirmation.
