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

- [x] Coverage tooling: `coverlet` + `dotnet test --collect:"XPlat Code Coverage"`, report merged and
      gated in CI at 90 % for `Core`/`Infrastructure` (excluding generated migrations, DTOs and
      `Program.cs`; the exclusion list lives in one file and is justified inline).
- [x] Test data: fixture DLLs for both mod kinds (client `[BepInPlugin]`, server `IModMetadata`), a
      tiny base install tree, delta fixtures generated from a dwarf binary, and recorded Forge JSON.
- [x] Invariant tests from [02-domain-model](../plan/02-domain-model.md#invariants-worth-a-test-each) —
      one test each, named after the invariant.
- [x] Manual QA matrix (Linux rows executed; Windows rows open), executed and recorded per release: Windows 11 + Linux (Fedora or Ubuntu) ×
      {fresh install, adopted install, patched install, Proton prefix, native Windows} ×
      {install, update, pin, export, import, launch, stop}.
- [x] Performance sanity: 1000-mod list render, 5 GiB instance clone timing, memory ceiling during
      download+extract (no full-archive buffering).
- [x] CI: `dotnet build -c Release` + `dotnet test` on `windows-latest` and `ubuntu-latest`, coverage
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

## Delivered

- **Coverage gate**: `tools/YuiTushonkaLauncher.Tools.CoverageGate` (a console app, referenced by the tests so the gate
  itself is covered). It reads every `coverage.cobertura.xml` under `TestResults`, keeps the best result per assembly,
  applies a 90 % floor to `Core` and `Infrastructure`, prints a table and exits non-zero below the floor. Wired into
  `ci.yml` on the Ubuntu leg; measured now: **Core 97.7 %, Infrastructure 93.6 %, Patching 97.2 %**.
- **Invariant tests**: `YuiTushonkaLauncher.Tests/Qa/InvariantTests.cs`, one test per invariant, named after it.
  Invariant 4 is asserted as far as SPT's format allows: the guard is the declared input size and the output hash is
  verified, because a delta declares no input hash (recorded in `91-before-1.0.md`). **Invariant 7 was not enforced**:
  the update service never looked at `Recognition`, so an unrecognised mod whose GUID the Forge happened to know would
  have been offered an update. `ModUpdateService` now returns `UpdateDecision.Unrecognized` for those rows, and the test
  covers exactly that collision.
- **Test data**: the fixture DLLs, the miniature base install, the delta fixtures and the recorded Forge JSON were
  already in place from phases 1–8; nothing had to be invented for this phase.
- **Manual QA matrix**: `docs/qa/manual-matrix.md` — Linux rows 1–8 and 11 executed on 2026-11-04 with the output quoted;
  Windows and the real-game rows stay open.
- **Performance**: `docs/qa/perf.md` with the numbers from `Qa/PerfSmokeTests` (1000 mods load in 164 ms, 100 filters in
  3 ms, an instance from 200+ files in 4 ms, an 11 MiB log tail in 4 ms allocating 3.1 MiB) plus the real-data numbers
  measured in earlier phases. The tests print the numbers and fail only on generous ceilings.
- **CI**: coverage gate on the Ubuntu job, and `.github/workflows/live.yml` for the `Live=Forge` tests on a nightly cron
  and on demand only.
- **Mutation harness** (`tools/mutation-check.py`): one mutant per fresh `dotnet test` process, a baseline test count
  first, and an **UNJUDGED** verdict when the count changes — the failure mode behind open question 27. Re-checked with
  it: the active-card mutant is killed, and the busy-guard mutant is now correctly reported as unjudged (the run
  collects 1132 tests instead of 1157) instead of a false "survived".
