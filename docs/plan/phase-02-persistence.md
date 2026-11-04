# Phase 2 — Persistence

**Branch:** `feature/phase-02-persistence` · **Depends on:** Phase 1.

## Goal

EF Core + SQLite with migrations, the full entity set, and a repository-free-but-testable data layer.

## Tasks

- [ ] `LauncherDbContext` in `Infrastructure` with `DbSet`s for the entities in
      [02-domain-model](../plan/02-domain-model.md); owned types for `SptVersion`, `PatchInfo`,
      `Range`-backed constraints (map to `TEXT`, compare in code).
- [ ] `AddLauncherDbContext(...)` extension: SQLite file under `IAppPaths.DataDir`, `WAL`,
      `foreign_keys=on`, `busy_timeout`, retry on `SQLITE_BUSY`.
- [ ] Design-time factory so `dotnet ef` works with the UI as startup project.
- [ ] Initial migration + a test that applies it to a temp file and validates the schema.
- [ ] `ISettingsService` implemented over a single-row table, cached in memory.
- [ ] `IUnitOfWork`/transaction helper for multi-step operations (install = download + extract + link +
      rows, all-or-nothing).
- [ ] Seed: default `RuntimeProfile` (Native) and the known SPT/Tarkov pairs from `/spt/versions` are
      *not* seeded — they are cached on first sync (Phase 3).
- [ ] Concurrency: every row carries a `RowVersion`/`UpdatedAt` guard where two windows could edit it.

## Acceptance criteria

- Create/read/update/delete an `Instance`, `Mod`, `ModVersion`, `Addon`, `ModList`, `PatchInfo` in
  tests, including the owned value objects round-tripping.
- Migration applies to an empty database and to a database one migration behind (upgrade path test).
- The database file is created under the configured data dir, never next to the executable.
- 90 % line coverage on the `Infrastructure/Persistence` namespace.

## Notes

- Do not expose `DbContext` outside Infrastructure; services take domain-level interfaces.
- Soft delete only where recovery matters (`Mod`, `Addon`); instances are hard-deleted with their
  folder after confirmation.
