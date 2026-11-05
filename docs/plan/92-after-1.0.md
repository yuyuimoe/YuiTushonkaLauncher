# After 1.0

Known caveats that are **deliberately out of scope until 1.0 ships**. They are small, they do not block
a release, and they can be adjusted afterwards. Unlike [91-before-1.0.md](91-before-1.0.md), nothing
here has to be closed for the release. Add an item only with the user's agreement and say why it waits.

| # | Caveat | Why it waits | Interacts with |
|---|---|---|---|
| 1 | **The game's `Logs` folder is not rotated.** SPT writes a `Logs` folder at the root of the game folder with logs that matter a lot for debugging. SPT does not rotate them: they are deleted and generated again on the next start, so a user who reopens the game after a crash loses the very logs that explain it. | A launcher-side fix is small (copy `Logs` into our archive before the game starts, or on exit) and does not change the format of anything. | [D17](04-decisions.md#d17-logs-rotate-compress-and-expire) (archive and retention already exist for our own logs); phase 11 `LogService`; [Q15](90-open-questions.md) (where that "root" is on our split layout) |
| 2 | **The game runs as a child of the launcher, so an OOM-kill of the game can take the launcher with it.** The official launcher starts the game as a child process; when the kernel's OOM killer fires, the launcher goes with it. Wanted: spawn the game with no direct relationship to the launcher, if possible. | The first release can ship with the plain child process; detaching is an optimisation of resilience. | **Conflicts with phase 11's "no orphans" rule** — see below. |

## Item 2: decided (2026-11-04) — both processes are detached

**The maintainer decided: the server *and* the game are spawned with no direct relationship to the
launcher** — implemented in phase 11 as [D22](04-decisions.md#d22-the-server-and-the-game-are-detached-and-adopted). That settles the tension below in favour of detaching both, and it overrides the phase 10/11
rule "killing the app leaves no child process alive" ([D21](04-decisions.md#d21-the-server-is-a-supervised-child-never-a-second-class-process)).
The consequences that phase 11 must carry:

- **Nothing is killed on launcher exit.** The launcher stops what it owns only when asked (an explicit
  Stop), or when it adopts a leftover and the user asks for it to be stopped.
- **Both need adoption.** A pid record per instance, per process (server, game), verified against the
  process start time and the executable path, so a restarted launcher shows "your server and game are still
  running" instead of starting a second one. `--server-stale` already does this for the server; the game
  needs the same.
- **The pair must stay consistent.** A game whose server died is broken, so the launcher must notice a dead
  server while the game runs and say so (and offer a restart), not silently pretend.
- **A prefix-bound detached process still needs its environment.** `PROTONPATH`, `WINEPREFIX` and the other
  owned variables must be captured in the record (or re-derived from the instance) so adoption shows the
  truth; nothing can be inherited from a launcher that no longer exists.

## Item 2 (settled): the design, see [D22](04-decisions.md#d22-the-server-and-the-game-are-detached-and-adopted)

Kept for the record of why the tension existed at all.

Phase 10 and 11 promise that *killing the app leaves no child process alive*. That is the right default
for the **server** and for a launch that is still starting. A detached game does the opposite on purpose:
it survives the launcher. Before building it, decide:

- Detach **only the game**, never the server. If the launcher dies and the game lives, the server it
  needs would die with the launcher, so either the server is detached too (and a later launcher
  session must find and adopt it) or the game is stopped with it — which defeats the purpose.
- How a restarted launcher **re-attaches**: a pid file per instance, verified against the process
  start time and executable path (a reused pid must never be mistaken for the game).
- Linux: `setsid` / a new session or a systemd scope (`systemd-run --user --scope`), so the OOM killer
  or a cgroup kill of the launcher's unit does not reach it; Windows: a job object **without**
  `KILL_ON_JOB_CLOSE`, plus `CREATE_BREAKAWAY_FROM_JOB`. Both need real measurement, not a guess.
- The OOM killer picks by score: a detached game still dies if *it* is the biggest process. What this
  buys is that the **launcher and server survive** and can say "the game was killed by the OOM killer"
  (exit code 137 / signal 9 plus a kernel log hint) instead of vanishing with it.

Phase 11 therefore keeps `ProcessManager` behind an interface that already separates "start", "adopt" and
"stop", so item 2 later changes a policy, not the architecture.
