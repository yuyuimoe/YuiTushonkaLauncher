# Manual QA matrix

Executed per release. A row is only ticked when someone ran it on the platform named in the header; the evidence
column says what was run and what it printed.

**Legend:** ☑ executed · ☐ not executed · **n/a** not applicable on that platform.

## Release 0.1.0-pre (`develop` @ 2bb7c23 + phase 13)

Host for the Linux rows: Fedora, `XDG_DATA_HOME` and `XDG_CONFIG_HOME` pointed at a scratch folder, base install
`.../base_tarkov41` (SPT 4.1.6, Tarkov 0.16.9.40743), game replaced by a `sleep` script.

| # | Flow | Linux | Windows 11 | Evidence |
|---|------|:-----:|:----------:|----------|
| 1 | Inspect a base install | ☑ | ☐ | `--inspect-base`: `shape Split, layout Spt41Plus, SPT 4.1.6, Tarkov 0.16.9.40743` |
| 2 | Create an instance from a split base | ☑ | ☐ | `--create-instance qa1`: `10 203 linked, 33 copied, patch state Patched` in 0.45 s |
| 3 | Patch state and patch plan | ☑ | ☐ | `--patch-plan qa1`: `1 to patch … delta 15 994 432 -> 16 233 472`, uses the `.spt-bak` as the pre-patch source |
| 4 | Export a pack | ☑ | ☐ | `--export qa1 <file>`: 685-byte pack, `0 mod(s), 0 addon(s), 0 config(s)`, share code printed |
| 5 | Import review (no writes) | ☑ | ☐ | `--instance qa1 --import <file>`: reads the manifest and prints `Review only: nothing was written` |
| 6 | Create a server profile | ☑ | ☐ | `--server-profile-add qa1 qa-user --edition Standard`: profile id printed, temporary server stopped |
| 7 | Launch (server + game, detached) | ☑ | ☐ | `--launch qa1 --profile qa-user --game-program /tmp/ytt-game.sh`: `server pid …, game pid …, 3.8 s` |
| 8 | Leftover detection and reaping | ☑ | ☐ | `--stale`: both processes listed as `Running [SystemdScope]`; `--stale --reap`: `Stopped 2 process(es)` |
| 9 | Import apply (writes files) | ☐ | ☐ | Covered by `PackServiceTests` and the phase-8 real run on a modded instance; not repeated here (the pack made in row 4 has no mods) |
| 10 | Update / pin / uninstall a mod | ☐ | ☐ | Covered by the phase-5/6 tests and the phase-6 real run; not repeated here |
| 11 | Proton prefix init and run | ☑ | n/a | Phase 9: `--prefix-init` with umu + GE-Proton11-1 in 18.5 s (699 MB), wine 9 s (386 MB), direct `proton run` 2 s (750 MB); proof file written by the game in the prefix |
| 12 | Wine / Proton launch of the real game | ☐ | ☐ | Not possible here: the game files are the user's, and the launch matrix that matters is on a machine with a GPU |
| 12 | Theme light/dark and 1000-row list on a real screen | ☐ | ☐ | Headless only; see open question 28 — this is the row that still needs a human |

### What "executed" means here

Rows 1–8 and 11 were run by the agent on this machine on 2026-11-04 and their output is quoted above. Rows 9–10 are
covered by automated tests (integration, against real archives) rather than by hand, because the result is a file tree
and the tests compare hashes. Rows 12 need hardware this environment does not have (a Windows install, a GPU-visible
game); they stay open and are listed in `docs/plan/91-before-1.0.md`.

### Rows to run on Windows before 1.0

1, 2, 3, 7, 8 (with the Windows job object instead of the systemd scope), 12 (native Windows, no Proton), and the
breakaway-job path of the detached pair.
