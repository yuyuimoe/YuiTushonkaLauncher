# Phase 10 — Server lifecycle, profiles and synchronization

**Branch:** `feature/phase-10-server-profiles` · **Depends on:** Phases 3, 4, 9, 11.

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
