# Performance sanity

Measured on the development machine (Fedora, .NET 10, release build). The numbers come from
`YuiTushonkaLauncher.Tests/Qa/PerfSmokeTests` and from the headless commands run against the real 4.1.6 base
install; the tests print them (`--logger "console;verbosity=detailed"`) and fail only on a generous ceiling, so a
slow machine breaks nothing while a change that turns a linear pass into a quadratic one does.

## Automated (2026-11-04)

| What | Measured | Ceiling in the test |
|---|---|---|
| Load 1000 installed mods from SQLite (`GetAsync`, with files) | **164 ms** | 5 s |
| 100 filters over those 1000 rows (in-memory) | **3 ms** (≈0.03 ms per filter) | 2 s |
| Create an instance from a 200+ file fake base (hard links) | **4 ms** | 20 s |
| Tail of a 43 MiB log (200 lines) | **8 ms**, **3.3 MiB** allocated | 5 s, 20 MiB |
| Filtering a 1000-row list in the UI (row reuse) | 50 filters under **2 s** (phase 12 test) | 2 s |

## Against real data (earlier runs, same machine)

| What | Measured |
|---|---|
| Create an instance from the real 4.1.6 base (10 236 files) | **0.45 s**, 10 203 linked / 33 copied |
| Layout migration of the same instance | **1.2 s** for 10 236 files, hard links intact |
| SPT server start to a ready ping | **3.6–5 s** |
| Full launch (pre-flight → patch check → server → game detached) | **2.7–3.8 s** |
| Proton prefix init — umu + GE-Proton11-1 | 18.5 s, 699 MB |
| Proton prefix init — wine | 9 s, 386 MB |
| Proton prefix init — direct `proton run` | 2 s, 750 MB |
| One game session's log folder | 322–325 MB (81 + 81 + 163 MiB) |

## What is not measured

- **The allocation number is process-wide by necessity.** `GC.GetAllocatedBytesForCurrentThread` around an `await` is
  meaningless (the continuation can run on another thread — that mistake once produced "53 MB allocated"), so the tail
  test waits for the task and compares process-wide totals, alone in a non-parallel collection.
- **Frame timing.** No benchmark harness renders a real frame loop; the phase 12 tests prove the list reuses its row
  objects instead of rebuilding them, which is the part that would cause per-frame allocation.
- **A 5 GiB instance clone.** The real instance here is ~40 GiB of links; the measurement above is the same code path
  with 10 236 files, and hard links make the cost proportional to the copied files (~33), not to the base size.
- **Memory ceiling during a large download + extract.** Extraction streams to disk (`SafeExtractor`), and the largest
  real archive exercised (a 236 MB mod) was installed in the phase-5 run; the peak was not instrumented.
- **Proton's own memory use while the game runs.**
