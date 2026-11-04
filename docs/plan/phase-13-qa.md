# Phase 13 — Testing and QA

**Branch:** continuous; the phase-13 checklist gates 1.0.

## Goal

**90 % line coverage on `Core` and `Infrastructure`**, plus scenario tests for the flows that break in
the real world. Coverage is a floor, not the target: the resolver, link layer, patch planner and
manifest code get scenario tests regardless of what the percentage says.

## Layers

| Layer | Tool | Scope |
|---|---|---|
| Unit | xunit + FluentAssertions | Core rules: `Range` parsing/ordering, link planner, `RelativePath`, resolution, manifest canonicalization |
| Integration (HTTP) | WireMock.Net + recorded fixtures | Forge client: pagination, `mods=`/`spt_version=`, updates buckets, 400/404/429, redirect download |
| Integration (FS) | temp dirs on tmpfs/ext4 | link/copy-on-write, instance create/delete, safe extraction, patch apply/restore |
| Integration (DB) | SQLite in temp file | migrations, transactions, upgrade path |
| UI | `Avalonia.Headless.XUnit` | main flows through view models |
| Process | fake executables (shell scripts / .bat) | launch pipeline, log tagging, shutdown, no orphans |
| Live (opt-in) | real API, `[Trait("Live","Forge")]` | `/ping`, `/mods?per_page=1`, `/spt/versions` |

## Tasks

- [ ] Coverage tooling: `coverlet` + `dotnet test --collect:"XPlat Code Coverage"`, report merged and
      gated in CI at 90 % for `Core`/`Infrastructure` (excluding generated migrations, DTOs and
      `Program.cs`; the exclusion list lives in one file and is justified inline).
- [ ] Test data: fixture DLLs for both mod kinds (client `[BepInPlugin]`, server `IModMetadata`), a
      tiny base install tree, delta fixtures generated from a dwarf binary, and recorded Forge JSON.
- [ ] Invariant tests from [02-domain-model](../plan/02-domain-model.md#invariants-worth-a-test-each) —
      one test each, named after the invariant.
- [ ] Manual QA matrix, executed and recorded per release: Windows 11 + Linux (Fedora or Ubuntu) ×
      {fresh install, adopted install, patched install, Proton prefix, native Windows} ×
      {install, update, pin, export, import, launch, stop}.
- [ ] Performance sanity: 1000-mod list render, 5 GiB instance clone timing, memory ceiling during
      download+extract (no full-archive buffering).
- [ ] CI: `dotnet build -c Release` + `dotnet test` on `windows-latest` and `ubuntu-latest`, coverage
      gate, and the live Forge tests on a nightly schedule only.

## Acceptance criteria

- Coverage ≥ 90 % lines on `Core` and `Infrastructure`, enforced in CI (build fails below it).
- Every invariant has a named test that would fail if the invariant were removed.
- Manual QA matrix complete and attached to the release PR.
- No test depends on the network, a running game, or the user's real home directory (all temp-rooted,
      cleaned up on failure).

## Notes

- Do not chase coverage with tests that assert nothing; a lower number with honest scenario tests is
  better than a green percentage over mocks.
- The fake-executable trick keeps the launch pipeline testable without a real Tarkov install.
