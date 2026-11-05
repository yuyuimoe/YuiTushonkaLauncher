# Tech debt

Accepted debt, visible instead of remembered. **It never blocks a release**
([D29](plan/04-decisions.md#d29-two-documents-that-make-gaps-visible-instead-of-remembered)). One line per
item: where it hurts, why it was accepted, what would close it.

| # | Where it hurts | Why accepted | What closes it |
|---|---|---|---|
| 1 | `tools/mutation-check.py` under-reports in the UI layer: with a mutant in `PageViewModel` a run collects fewer tests than the baseline and is marked UNJUDGED (Q27). | The count guard turns a false "SURVIVED" into "UNJUDGED", and the behaviour is covered by an ordinary test verified by hand. | Find why discovery lists all tests but the run stops early for that class only. |
| 2 | `DependencyGraph` rebuilds by walking the whole catalogue: 99 requests for 2 083 mods, once a week (Q31). | There is no reverse endpoint; the alternative is an incomplete "dependents known so far". | A reverse index on the Forge side, or an incremental walk that resumes where it stopped. |
| 3 | The game's launch arguments are built by string concatenation: `-config={{'BackendUrl':…}}` with single quotes, in `LaunchService`. | The client parses a JSON-ish literal, not JSON; quoting rules for paths with spaces were not needed yet. | Serialize with a formatter and add a test with a host name that needs escaping. |
| 4 | `ForgeModRowViewModel` carries the whole `ForgeMod` and the view model recomputes derived strings per binding. | Fine at 25 rows per page; measurement showed no cost (Q29). | Revisit if the result list grows beyond a page. |
| 5 | Double-clicking a `.sptpack` while the launcher is already running opens the "already running" window and drops the file: there is no IPC to hand it to the live instance. | The association's common case is opening a pack with nothing else running; forwarding needs a socket, a naming scheme and a timeout on both platforms. | A small per-user endpoint (`IInstanceRunStore`-style) that the running instance listens on and the second process writes the path to. |
| 6 | The pack import review lives in `PackOpenService`, not on a page: it can be reached from the shell association but there is no Import button in the UI, and no export dialog at all. | The association was the thing the release needed; the phase 8 UI task stayed open. | The export/import section on the instance page, reusing `PackService.PlanImportAsync`/`ImportAsync`. |
