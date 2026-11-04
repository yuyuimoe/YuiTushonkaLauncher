# Phase 11 — Layout, launch flow and logs

**Branch:** `feature/phase-11-launch-logs` · **Depends on:** Phases 4, 5, 7, 9, 10.

## Status

☐ next. Designed by a grilling session on 2026-11-04; every decision below is the maintainer's, and the
facts it rests on were measured on real installs (`tarkov_runs/spt41_live`, `tarkov_dev/spt40_dev`).

## Goal

One deterministic launch pipeline — verify → patch if needed → server → game — over **the layout SPT
actually expects**, with both processes detached from the launcher, and a log system a player can use when
something breaks.

## The layout decision this phase starts with

The official launcher runs from inside `SPT_Runtime` and computes the game path as the parent of the current
directory (`SPTushonka.Core/Configuration/ConfigHelper.cs: GetGamePath`), and Unity Doorstop needs
`winhttp.dll` next to `EscapeFromTarkov.exe`. So the playable root is **merged**:

```
<instance>/EscapeFromTarkov.exe      <instance>/winhttp.dll      <instance>/doorstop_config.ini
<instance>/EscapeFromTarkov_Data/    <instance>/BepInEx/         <instance>/Logs/
<instance>/UnityPlayer.dll           <instance>/SPT_Runtime/     (4.0: <instance>/SPT/)
```

Measured on a real instance: `SPT_Runtime/user/{cache,certs,credentials,Launcher,logs,mods,patchers,
profileData,profiles,sptappdata,sptRegistry,sptSettings}`, and `Assembly-CSharp.dll` with link count 1 while
the rest of the game is hard-linked to the base.

Consequences, all of them tasks below:

- **A split install (`gamefiles/` + `spt/`, what the SPT installer produces) is not a runnable SPT install.**
  It is *the source of original, untouched game files* used to create merged instances. It is still accepted
  as a base (`--inspect-base`, `--create-instance`), labelled as such, never as an instance.
- **A merged folder is a valid instance source** (`--migrate-scan`), because that is what a user of the
  official launcher already has.
- **Inside an instance the paths are the identity map.** `BepInEx/plugins/…`, `SPT_Runtime/user/mods/…`,
  `EscapeFromTarkov_Data/Managed/…` are all relative to the instance root. `LinkPolicy` and
  `InstancePathMapper` stop translating; the translation belongs to *instance creation* (split base →
  merged instance) and nowhere else.
- **Existing instances must be migrated** (split → merged), automatically, idempotently and with a plan.

## Tasks

### 11.0 Layout (first, because everything else depends on it) — ☑ done

Delivered: `InstallShape` (split vs merged), `InstancePaths` (instance-relative paths, base unwrapping,
shape detection, legacy-path normalisation), `LinkPolicy.Decide(baseRelative, isDirectory, shape)`,
`InstancePlanEntry.Destination`, creation that lays both halves at the root, `LayoutMigrationService`
(rename-based, idempotent, unshares the patched binary, rewrites stored paths, refuses while the server
runs), `--migrate-layout [NAME] [--apply]`, `InstancePathMapper` as the identity, and package imports that
still match a pre-change `.sptpack`. Verified on a real install: 10 236 files migrated in 1.2 s with the
hard links intact, 1027 tests.

- [x] `LinkPolicy`/`InstancePathMapper`: identity mapping inside an instance; the split→merged translation
      lives in the creation plan only. `EscapeFromTarkov_Data`, `BepInEx`, `SPT_Runtime` (4.1+) / `SPT` (4.0),
      `winhttp.dll`, `doorstop_config.ini`, `Logs/` and the per-instance `user/` all at the root.
- [x] Layout detection (`InstanceLayout` today only splits 4.0/4.1; add **split vs merged** as its own axis)
      and a marker so a folder is never mistaken for an instance.
- [x] `InstanceService.CreateAsync`: plan split base → merged instance (link everything, copy what the
      policy says, never link `Assembly-CSharp.dll` — [D5](04-decisions.md#d5-assembly-csharpdll-is-never-linked)),
      and create `user/`, `Logs/` as per-instance.
- [x] **Migration v1 → v2** (`--migrate-layout`): detect an instance whose root has no `EscapeFromTarkov.exe`,
      plan every file (link / copy / unshare), execute, then remove `gamefiles/` and `spt/` only when everything
      is in place; idempotent; refuses while any process of that instance runs.
- [x] Revise every consumer of a path: integrity, mod install (`InstancePathMapper`), patching (the target is
      now `<root>/EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll`, the delta set still comes from
      `<root>/SPT_Runtime/SPT_Data/Launcher/Patches`), export/import, `--inspect-base` wording.
- [x] Update the phase 4/5/7/8 tests that encoded the split paths, and keep the merged-vs-split difference
      visible in their names.

### 11.1 Detached processes (decided, [D22](04-decisions.md#d22-the-server-and-the-game-are-detached-and-adopted)) — ☑ done

Delivered: `RunMechanism`/`RunProcess`/`InstanceRun` and `IInstanceRunStore` (one record per instance, written
before release), `DetachStrategy` (`systemd-run --user --scope` when a user manager answers, else `setsid`,
else a plain child), `IRunAdoptionService` (pid + start time + executable path; a recycled pid is forgotten,
never stopped), the supervisor writing the server half into the pair, `--stale [--reap]` with
`--server-stale` as an alias, and two settings (`GameStartWindow`, `AskBeforeStoppingProcesses`) with their
migration. Verified against a real server: it survived `kill -9` of the launcher, kept answering `ping`,
`--stale` named it with its mechanism and `--stale --reap` stopped it. 1033 tests; 9/9 mutants killed.

- [x] `IChildProcessSpawner`: replace `setpriv --pdeathsig TERM` with **`systemd-run --user --scope` when
      available, otherwise `setsid`**, recording which was used; Windows: job object **without**
      `KILL_ON_JOB_CLOSE` + `CREATE_BREAKAWAY_FROM_JOB`. Remove the "dies with the launcher" promise.
- [x] One `InstanceRun` per instance holding **both** processes
      (`{instanceId, server{pid,start,exe,port,mechanism,env}, game{pid,start,exe,mechanism}}`), written
      before the process is released; adoption requires pid + start time + executable path to match.
- [x] Adoption on start: show "server and game still running", adopt them, never start a second one, offer
      Stop. `--server-stale` generalises into `--stale [--reap]` covering the game too.
- [x] Nothing is killed when the launcher exits. An explicit Stop ends the pair; the close dialog offers
      "stop server and game" (default: keep running) and can be silenced by a setting.
- [x] The window behaviour the user asked for: **"close the launcher when the game starts"** and
      **"minimise the launcher when the game starts"**, as `GameStartWindow` (the UI that applies it is phase 12).
- [x] Pair coherence: the record keeps the game half when the server stops; with the launcher open, a dead server while the game runs is a state with its reason
      and a manual "restart server"; nothing auto-restarts and nothing kills the game.

### 11.2 Launch pipeline

- [ ] `LaunchService`: `PreFlight → PatchCheck → ServerStart (or adopt) → Ready → GameStart → Detached`,
      emitting `LaunchStep` events; **cancel is allowed until `GameStart`**; afterwards the action is a Stop
      of a detached game, which is a different button.
- [ ] `PreFlight`: instance present and merged, files intact, disk space, runtime available, profile chosen.
- [ ] Profile choice: **always asked**, pre-selected with the remembered one, with "always use this profile on
      server `NAME/IP`" stored on `ServerProfile` (`RememberedProfileId`, `RememberedProfileUsername`,
      `RememberedForEndpoint`) through a migration. The game token is the profile id; no profile means the
      launch refuses and says so.
- [ ] Patch guard: `--patch`/`--restore`/`--migrate-layout`/export **refuse while the instance's server or game
      runs**, naming the process.
- [ ] Game command: `EscapeFromTarkov.exe -force-gfx-jobs native -token=<profileId>
      -config={'BackendUrl':'https://<host>:<port>','Version':'live','MatchingVersion':'live'}`, working
      directory = instance root, launched through the runtime profile ([D20](04-decisions.md#d20-the-runtime-layer-plans-first-runs-second-and-never-guesses)),
      with `LoadsDoorstop = true` because BepInEx needs the `winhttp` override under Wine.

### 11.3 Logs

- [ ] `LogService`: Serilog with per-source sinks (app/server/game/bepinex/proton), an in-memory ring buffer for
      the live view, rolling files under `user/logs/`, gzip on rotation into `user/logs/archive/`, retention by
      count+age, export of a selection ([D17](04-decisions.md#d17-logs-rotate-compress-and-expire)).
- [ ] Sources, measured: the newest run folder `<instance>/Logs/<date>_<version>/` (`application.log` by default;
      `_backend.log`, ` errors.log`, ` traces.log` available but off), `<instance>/BepInEx/LogOutput.log`, and the
      server's daily file `<instance>/SPT_Runtime/user/logs/spt/spt<yyyymmdd>.log`.
- [ ] **Tail, never load.** Above **64 MiB** the follow for that file stops, the view says so and offers "open
      with the system editor" + "open the folder". One real session produced 81 + 81 + 163 MiB, so this is the
      normal case for `traces`/`errors`, not an edge case.
- [ ] Log window: multi-source view with filters, level colouring, search, follow-tail, copy, export selection
      (`.txt`/`.log`), opening an archived `.gz` transparently.
- [ ] Crash diagnostics: on a failed step, a bundle (versions, instance state, last N lines, file hashes) the
      user can paste into a bug report.
- [ ] UI: launch button state machine matching the `LaunchStep` names, with cancel only where the contract
      allows it. (The window itself is phase 12; the state machine and its tests are here.)

## Acceptance criteria

- [x] A fresh instance created from the split base runs: game path resolution finds the `.exe`, `winhttp.dll`
      and `BepInEx` at the root, and the server's working directory is `<root>/SPT_Runtime`.
- [x] A split install is refused as an instance, and accepted (labelled) as a base.
- [x] Migrating a v1 instance produces a merged one with byte-identical files, and running it twice changes nothing.
- [ ] Launching a fresh, unpatched instance patches it, starts the server, launches the game, and all sources
      appear live with correct tagging.
- [x] Killing the launcher (SIGKILL) leaves **both** processes running; reopening adopts both, and Stop ends both.
      *(Verified for the server; the game joins the same record in 11.2.)*
- [x] A record whose pid was reused, or whose executable path differs, is never adopted.
- [ ] A log file above 64 MiB stops tailing with a readable message instead of freezing the UI.
- [ ] Log rotation compresses the previous file and retention removes only files beyond the configured
      count/age — asserted with a fake clock.
- [ ] Export produces a file whose content matches the selected range exactly.
- [ ] A failed step names the file/step/exit code, never "something went wrong".
- [ ] `--patch`, `--restore`, export and `--migrate-layout` refuse while the instance's processes run.

## Notes

- The server must be fully stopped before the patch/restore steps of the next run; the pipeline enforces the
  ordering rather than relying on the user. With detached processes this means "refuse", not "assume".
- Bundle sync (`/singleplayer/bundles`, `/files/bundle/`) is still open (open question 5).
- A detached process still holds the launcher's stdout/stderr pipes, because that is what keeps the failure
  reason visible (a busy port, a missing runtime). If the launcher dies, a later write to that pipe can fail
  inside the process. The log files are the durable source and 11.3 reads them; the pipe risk is recorded in
  `91-before-1.0.md`.
- Archiving SPT's `Logs/` folder (it deletes and recreates it every start) stays after 1.0, now with its
  measured cost ([92-after-1.0](92-after-1.0.md)).
