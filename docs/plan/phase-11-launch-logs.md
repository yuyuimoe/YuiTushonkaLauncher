# Phase 11 — Launch flow and logs

**Branch:** `feature/phase-11-launch-logs` · **Depends on:** Phases 7, 9, 10.

## Goal

One deterministic launch pipeline (verify → patch if needed → server → game) and a log system that a
player can actually use when something breaks.

## Tasks

- [ ] `LaunchService` pipeline with explicit steps and cancellation:
      1. pre-flight (instance present, files intact, disk space, runtime available);
      2. patch state check → patch if needed → verify;
      3. start server → wait for readiness;
      4. start game (native or via the Linux runtime);
      5. monitor both, propagate exit codes, tear down in reverse order.
      Every step emits a `LaunchStep` event so the UI can show "Patching… Starting server… Launching…".
- [ ] `ProcessManager`: uniform child-process wrapper (start, stream output lines with source tags,
      exit code, graceful then forced stop, no orphans). Windows: job object; Linux: process group +
      `PR_SET_PDEATHSIG` equivalent.
- [ ] `LogService`: Serilog with per-source sinks (app/server/game/proton/bepinex), in-memory ring
      buffer for the live view, rolling files under `user/logs/`, gzip on rotation into
      `user/logs/archive/`, retention by count+age, and export of a selection
      ([D17](../plan/04-decisions.md#d17-logs-rotate-compress-and-expire)).
- [ ] BepInEx log pickup: tail `BepInEx/LogOutput.log` (and the console log) as its own source, since
      client-mod failures only appear there.
- [ ] Log window: multi-source view with filters, level colouring, search, follow-tail, copy, and
      "export selection" (`.txt`/`.log`); opening an archived `.gz` transparently.
- [ ] Crash diagnostics: when a step fails, capture a bundle (versions, instance state, last N log
      lines, file hashes) the user can copy into a bug report.
- [ ] UI: launch button state machine (Idle → PreFlight → Patching → StartingServer → Launching →
      Running → Stopping), with cancel at any step.

## Acceptance criteria

- Launching a fresh, unpatched instance patches, starts the server, launches the game, and the log
  window shows all three sources live with correct tagging.
- Killing the app mid-launch leaves no child process alive; the next launch works.
- Log rotation compresses the previous file and retention removes only files beyond the configured
  count/age — asserted by a test with a fake clock.
- Export produces a file whose content matches the selected range exactly.
- A failed step shows a specific, actionable message (which file/step/exit code), not "something went
  wrong".

## Notes

- The server must be fully stopped before the patch/restore steps of the next run; the pipeline
  enforces that ordering rather than relying on the user.
- Log volume is high (Proton is verbose): the ring buffer is bounded and the UI must not grow
  unbounded.
