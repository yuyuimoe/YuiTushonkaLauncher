# Phase 9 — Linux: Proton, Wine and prefixes

**Branch:** `feature/phase-09-linux-proton` · **Depends on:** Phases 4, 11 (process launching).

## Goal

Make SPT run on Linux with the same "one click and it works" experience the official launcher gives,
without hiding what it does.

## Tasks

- [ ] `IRuntimeLocator` (Linux): discover Proton builds in
      `~/.steam/steam/compatibilitytools.d`, `/usr/share/steam/compatibilitytools.d`, the user's added
      search paths (mirroring the official launcher's `Paths.ProtonPaths` + `AddLinuxProtonSearchPath`),
      and plain `wine` binaries; report versions and validity.
- [ ] `umu` integration: launch through `umu-run` when available (contributing a Proton build) with
      `GAMEID`, `PROTONPATH`, `WINEPREFIX`, `STORE=none`; fall back to calling the Proton build
      directly, then to `wine`.
- [ ] Prefix management: per-instance `WINEPREFIX` under the instance folder (default) or a shared
      one, create/initialize, detect a broken prefix, and offer `winecfg` / `winetricks` / `regedit`
      inside the prefix for advanced users.
- [ ] Environment panel: editable `KEY=VALUE` set (`DXVK_HUD`, `PROTON_USE_XALIA`, `MANGOHUD`,
      `--disable-software-renderer` style args), with validation and per-instance persistence.
- [ ] `gamemoded` detection (as the official launcher does) to warn when GameMode is installed but not
      running.
- [ ] Windows: the same abstractions resolve to `Native` and the panel is hidden.
- [ ] UI: Linux settings page (runner select, prefix picker, env editor, "open prefix tools").

## Acceptance criteria

- [ ] The app launches the SPT server and the game through a chosen Proton build in a fresh prefix, and
      both process' stdout/stderr reach the log viewer tagged with the right source.
- Prefix path and env vars survive an app restart and are visible/editable before launch.
- Misconfiguration (Proton binary missing, prefix on a read-only path, `wine` absent) is reported
  before launching anything, with the exact missing path.
- On Windows the whole page is absent and nothing Linux-specific is invoked.

## Notes

- The local reference environment is a Lutris/Wine prefix at `~/Games/spt40` containing
  `base_tarkov` (4.0) and `base_tarkov41` (4.1) — use it for manual verification.
- Prefix creation must not require a TTY; `wineboot` output is logged, not echoed into the UI thread.
- Do not import the official launcher's `LinuxHelper` wholesale; take the discovery rules, not the
  coupling.
