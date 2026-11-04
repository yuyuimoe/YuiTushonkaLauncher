# Phase 9 — Linux: Proton, Wine and prefixes

**Branch:** `feature/phase-09-linux-proton` · **Depends on:** Phases 4, 11 (process launching).

**Status:** ☑ done, except the dialogs (phase 12) and the full launch of the game (phase 11, which owns the
pipeline). Delivered: `IRuntimeLocator`, `IRuntimeLauncher` (plan + pre-flight, three runtimes),
`IPrefixService`, `IRuntimeProfileService`, `LaunchOptions` (Steam launch-options format), a minimal
`IProcessRunner`, a migration (`UseGameMode`, extra Proton roots, umu path) and the commands `--runtimes`,
`--runtime-profile-add`, `--runtime-assign`, `--runtime-plan`, `--prefix-init`, `--runtime-run`. 732 tests;
Core 97.7 %, Infrastructure 93.5 %. Verified on this machine with real `umu-run` + GE-Proton, direct
`proton run` and plain Wine (see [D20](04-decisions.md#d20-the-runtime-layer-plans-first-runs-second-and-never-guesses)).

## Goal

Make SPT run on Linux with the same "one click and it works" experience the official launcher gives,
without hiding what it does.

## Tasks

- [x] `IRuntimeLocator` (Linux): discover Proton builds in
      `~/.steam/steam/compatibilitytools.d`, `/usr/share/steam/compatibilitytools.d`, the user's added
      search paths (mirroring the official launcher's `Paths.ProtonPaths` + `AddLinuxProtonSearchPath`),
      and plain `wine` binaries; report versions and validity.
- [x] `umu` integration: launch through `umu-run` when available (contributing a Proton build) with
      `GAMEID`, `PROTONPATH`, `WINEPREFIX`, `STORE=none`; fall back to calling the Proton build
      directly, then to `wine`.
- [x] Prefix management: per-instance `WINEPREFIX` under the instance folder (default) or a shared
      one, create/initialize, detect a broken prefix, and offer `winecfg` / `winetricks` / `regedit`
      inside the prefix for advanced users.
- [x] Environment panel (model, validation and persistence; the panel itself is phase 12): editable `KEY=VALUE` set (`DXVK_HUD`, `PROTON_USE_XALIA`, `MANGOHUD`,
      `--disable-software-renderer` style args), with validation and per-instance persistence.
- [x] `gamemoded` detection (as the official launcher does) to warn when GameMode is installed but not
      running.
- [x] Windows: the same abstractions resolve to `Native` and the panel is hidden.
- [ ] UI: Linux settings page — moved to phase 12 (runner select, prefix picker, env editor, "open prefix tools").

## Acceptance criteria

- [~] The app launches the SPT server and the game through a chosen Proton build in a fresh prefix, and
      both process' stdout/stderr reach the log viewer tagged with the right source. *Half done here:* a Windows
      program ran through Proton in a fresh prefix and its (umu/Proton) stderr arrived tagged; the server, the
      game and the log viewer belong to phases 10–11.
- [x] Prefix path and env vars survive an app restart and are visible/editable before launch.
- [x] Misconfiguration (Proton binary missing, prefix on a read-only path, `wine` absent) is reported
  before launching anything, with the exact missing path.
- [x] On Windows the whole layer is inert and nothing Linux-specific is invoked.

## Deviations

- **Process execution is a seed, not the manager.** Prefix creation needs to run `wineboot`, so a small
  `IProcessRunner` (run to completion, tagged lines, kill the tree on timeout/cancel, no TTY) exists now;
  phase 11 builds the long-lived `ProcessManager` on top of the same idea.
- **Pre-flight is a first-class value** (`LaunchPlan`), which the plan only implied.
- **Four extra Proton roots** beyond the official two: `~/.local/share/Steam/compatibilitytools.d` (this
  machine's real one), Flatpak Steam, and Valve's `steamapps/common/Proton*`. Links are resolved so a build is
  listed once.
- **Settings gained two fields** (`ExtraProtonRoots`, `UmuRunPath`) and `LauncherSettings` got value
  equality for its list.
- **`RuntimeProfile.UseGameMode`** is new (migration `AddLinuxRuntimeSettings`).
- **`--runtime-run`** exists only to prove a program runs through the runtime; it is a diagnostic.
- **The real launch of the game is not done here**: see open questions 15 and 20.

## Bugs found on real data

- Every Proton build was listed twice because `~/.steam/steam` is a symlink to `~/.local/share/Steam`.
- Plain Wine: `wineboot` exits before the registry is written, so a good prefix was reported broken.
- `LauncherSettings` equality broke as soon as it held a list (3 existing tests caught it).

## Notes

- The local reference environment is a Lutris/Wine prefix at `~/Games/spt40` containing
  `base_tarkov` (4.0) and `base_tarkov41` (4.1) — use it for manual verification.
- Prefix creation must not require a TTY; `wineboot` output is logged, not echoed into the UI thread.
- Do not import the official launcher's `LinuxHelper` wholesale; take the discovery rules, not the
  coupling.
