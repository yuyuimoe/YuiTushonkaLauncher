# Phase 10 — Server lifecycle, profiles and synchronization

**Branch:** `feature/phase-10-server-profiles` · **Depends on:** Phases 3, 4, 9, 11.

**Status:** ☑ done, except the UI (phase 12) and the launch pipeline that consumes it (phase 11). Delivered:
`ISptServerLocator`, `ISptServerSupervisor` (start with pre-flight, readiness by ping, graceful stop, no
orphan, stale-server reaping), `ISptServerClient` (the real `/launcher/v2` wire format), `IServerProfileService`,
`IServerPortService`, `IServerSyncService`, `IChildProcessSpawner`/`IProcessInspector`, and the commands
`--server-info`, `--server-run`, `--server-profiles`, `--server-profile-add|remove|wipe|select`,
`--server-port`, `--server-sync`, `--server-stale`. 983 tests; Core 98 %, Infrastructure 94.8 %. Verified
against a real 4.1.6 server, a real Forge download and real processes (see
[D21](04-decisions.md#d21-the-server-is-a-supervised-child-never-a-second-class-process)).

## What was measured on the real server (4.1.6, this machine)

Everything below was read from the real thing, not assumed; the details are in
[03-verified-facts](03-verified-facts.md#the-spt-server-measured-2026-11-04).

- **Ready in 3.6–5 s**, then `GET /launcher/v2/ping` answers `{"Response":"Pong!"}`.
- **Wire format:** HTTPS with a **self-signed certificate** (created on first start under `user/certs`), and
  both directions are **zlib-compressed JSON**. Reads are `GET`, changes are `PUT`.
- **Routes:** `ping`, `version`, `types`, `profiles`, `profile`, `login`, `register`, `remove`, `wipe`,
  `mods`, `mod-pages`. `/launcher/v2/mods` returns the **server** mods keyed by GUID with `ModGuid`, `Name`,
  `Version`, `SptVersion`; `mod-pages` was empty on a stock install.
- **The server validates almost nothing:** `register` accepts an unknown edition and an empty name, and
  `remove` with an empty name removes a profile. The launcher validates, and the CLI shows why.
- **`wipe` only flags** the profile (`"wipe": true`); the wipe happens when the game next starts.
- **A busy port is not a non-zero exit:** the server prints `AddressAlreadyInUse (98)` and exits **0**, so
  readiness must never trust the exit code — it reports the port from the last output.
- **`SIGTERM` is graceful** (`Stopping server...`, ~100 ms); it is the right stop request on Linux.
- **`http.json` holds `ip` + `port` (+ `backendPort`)**, and the server honours `127.0.0.1` vs `0.0.0.0`
  (a bind to loopback really is loopback).
- **SPT 4.1.6 needs .NET 10**; SPT 4.0 needs .NET 9 (from `SPT.Server*.runtimeconfig.json`).
- **The server backs up profiles itself** on a schedule into `user/profiles/backups/<date>/` ("Profile
  backups …", `activeMods.json`), so our copy is for the destructive operation specifically, not a substitute.
- **Client mods are recorded in the profile** (`sptData.mods`), written by the game, not published through
  `mod-pages` on a stock server; the sync therefore compares them read-only.

## Tasks

- [x] `SptServerLocator`: find `SPT.Server[.exe]` / `SPT.Server.Linux` for both layouts, read
      `SPT.Server.*.runtimeconfig.json` to know the .NET requirement, and read the port from the
      server config (`SPT_Data/configs/http.json`).
- [x] `SptServerProcess`: start with a working directory of the runtime root, redirect output, tag
      logs, capture exit code, restart-on-crash policy (opt-in), graceful stop (server shutdown path →
      `SIGTERM` → kill after timeout), and no orphan on app exit/crash (Windows job object; Linux
      process-group kill).
- [x] `ISptServerClient` for the launcher API the official launcher uses:
      `/launcher/v2/{ping,version,types,profiles,profile,login,register,remove,wipe,mod-pages}` plus
      `/launcher/v2/mods`. Typed DTOs, per-instance base URL, timeouts, and a readiness probe with retry/backoff.
- [x] `ServerProfileService`: list the server's profiles, create/select/delete, wipe (with explicit
      confirmation + backup of the profile file), and remember the last used profile per instance.
- [x] Client-mod reporting: publish the instance's installed client mod list to the server
      (`mod-pages` / client-mod reporting path) so `ActiveClientMods` matches reality, and surface
      mismatches (`server says X, disk says Y`) in the UI.
- [x] `ServerSyncService` (remote servers): given a server's exported `.sptpack` or its published mod
      list, compute the diff against the instance and offer/perform install + update, honouring pins.
- [x] Multi-instance safety: port allocation (detect a busy port, propose the next free one), one
      running server per instance, refuse to start a second server on the same port/prefix.
- [ ] UI: server control panel (start/stop/status/log tail), profile manager, wipe flow, sync dialog.

## Acceptance criteria

- [x] Starting an instance brings the server up and only then enables "Launch game"; the button stays
      disabled with a readable reason when the server fails to start. *(Headless: `--server-run` returns
      only when ping answers, `--server-info` prints every reason and exits non-zero on an error.)*
- [x] `ping` readiness is polled, not slept on: a slow start still works, a crash is reported immediately.
- [x] Creating and switching profiles through the app is reflected in `SPT_Runtime/user/profiles`, and the
      game sees the selected profile. *(Via the API; verified that the server writes the file and that the
      selected id is remembered.)*
- [x] Closing the app (or killing it) leaves no `SPT.Server` process behind.
- [x] Two instances can run at the same time only on different ports/prefixes; the app says which
      conflict it found.

## Deviations

- **No client-mod *reporting*.** The description assumed the launcher publishes the list through
  `mod-pages`; measured, that route is a read-only list advertisement on a stock server and the mods are
  recorded by the game in the profile. The sync compares that record instead of pretending to write it.
- **No `.sptpack` source for the sync.** The server publishes its mod list, so the version comparison is
  enough and does not need a package the user would have to have.
- **The client-mod half of the sync is informational.** It cannot be applied: installing a client mod
  changes what the game loads, and the server only learns about it after a session.
- **`ServerProcess` is a supervisor service, not a process object.** One `SptServerSupervisor` owns every
  instance's server (a per-instance object would have to be kept alive by its caller).
- **Detaching the game from the launcher is not here.** It conflicts with "no orphans" and is recorded for
  after 1.0 ([92-after-1.0](92-after-1.0.md)).

## Bugs found while building this phase

- **`SPT.Server.Linux` has no extension to strip.** `Path.ChangeExtension("SPT.Server.Linux", null)` is
  `SPT.Server`, so the locator read the *Windows* `runtimeconfig` and reported the requirement of the wrong
  build. The two files are identical in a real install, so only a test that changes one of them shows it.
- **A program that exits at once broke the spawner.** `Process.StartTime` throws
  (`InvalidOperationException`, and under load `Win32Exception`) once the process is gone, which is exactly a
  server that fails on its first line — the start failed with a framework exception instead of the server's
  reason.
- **`MainModule` can fail for another process.** Under load the inspector answered "unknown path", which
  would make a leftover server unrecognisable (and so unstopped). `/proc/<pid>/exe` is read as a fallback.
- **A free-port search anchored on a privileged start found nothing.** `FindFree(80)` scanned
  80…580 and rejected every candidate.
- **A dead guard.** A `StopRequested` flag in the monitor could not be made to change behaviour in ten
  forced races: releasing the gate only after forgetting the child already covers every order, so it was
  removed rather than kept as untestable code.

## Notes

- Bundle sync (`/singleplayer/bundles`, `/files/bundle/`) semantics are still
  [open](../plan/90-open-questions.md#blocks-nothing-now-but-must-be-settled-before-10) — confirm before
  implementing, do not guess.
- Profiles are server-owned state: we cache for display, never write the server's files directly
  unless the API has no way to do it (and then with a backup).

## Goal

Own the local SPT server: start it, wait for it, talk to it, keep its profile state visible, and stop
it — because SPT's model requires the server to run the whole time the player plays
([D13](../plan/04-decisions.md#d13-server-lifecycle-is-part-of-the-single-player-flow)).

## Tasks

- [ ] `SptServerLocator`: find `SPT.Server[.exe]` / `SPT.Server.Linux` for both layouts, read
      `SPT.Server.*.runtimeconfig.json` to know the .NET requirement, and read the port from the
      server config (`SPT_Data/configs/http.json` — confirm key in this phase).
- [ ] `SptServerProcess`: start with a working directory of the runtime root, redirect output, tag
      logs, capture exit code, restart-on-crash policy (opt-in), graceful stop (server shutdown path →
      `SIGTERM` → kill after timeout), and no orphan on app exit/crash (Windows job object; Linux
      process-group kill).
- [ ] `ISptServerClient` for the launcher API the official launcher uses:
      `/launcher/v2/{ping,version,types,profiles,profile,login,register,remove,wipe,mod-pages}`.
      Typed DTOs, per-instance base URL, timeouts, and a readiness probe with retry/backoff.
- [ ] `ServerProfileService`: list the server's profiles, create/select/delete, wipe (with explicit
      confirmation + backup of the profile file), and remember the last used profile per instance.
- [ ] Client-mod reporting: publish the instance's installed client mod list to the server
      (`mod-pages` / client-mod reporting path) so `ActiveClientMods` matches reality, and surface
      mismatches (`server says X, disk says Y`) in the UI.
- [ ] `ServerSyncService` (remote servers): given a server's exported `.sptpack` or its published mod
      list, compute the diff against the instance and offer/perform install + update, honouring pins.
- [ ] Multi-instance safety: port allocation (detect a busy port, propose the next free one), one
      running server per instance, refuse to start a second server on the same port/prefix.
- [ ] UI: server control panel (start/stop/status/log tail), profile manager, wipe flow, sync dialog.

## Acceptance criteria

- Starting an instance brings the server up and only then enables "Launch game"; the button stays
  disabled with a readable reason when the server fails to start.
- `ping` readiness is polled, not slept on: a slow start still works, a crash is reported immediately.
- Creating and switching profiles through the app is reflected in `SPT_Runtime/user/profiles`, and the
  game sees the selected profile.
- Closing the app (or killing it) leaves no `SPT.Server` process behind.
- Two instances can run at the same time only on different ports/prefixes; the app says which
  conflict it found.

## Notes

- Bundle sync (`/singleplayer/bundles`, `/files/bundle/`) semantics are still
  [open](../plan/90-open-questions.md#blocks-nothing-now-but-must-be-settled-before-10) — confirm before
  implementing, do not guess.
- Profiles are server-owned state: we cache for display, never write the server's files directly
  unless the API has no way to do it (and then with a backup).
