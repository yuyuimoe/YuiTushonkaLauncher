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

## Release 0.2.0-rc.2 — Windows, on a VM (2026-11-05)

Host: Windows 10 21H2 (build 19044) in a libvirt VM, 8 GB RAM, .NET 10.0.0 runtime installed, SPT 4.0.9 at
`C:\SPTarkov` (Tarkov 0.16.9.40087, shape Merged, layout Spt40), launcher `0.2.0-rc.1-11-gae2fc16` (self-contained
zip, built from `feature/windows-first-run`).

| # | Flow | Result | Evidence |
|---|------|:------:|----------|
| 1 | Inspect a base install | ☑ | `--inspect-base C:\SPTarkov`: `shape Merged, layout Spt40, SPT 4.0.9, Tarkov 0.16.9.40087`, patch state `Patched` |
| 2 | Create an instance | ☑ | `--create-instance win1 --from C:\SPTarkov --into C:\Users\agent\instances\win1`: `10 029 linked (58 125 MiB), 893 copied (257 MiB), patch state Patched` in 19.9 s |
| 3 | Hard links are real links | ☑ | `fsutil hardlink list`: `UnityPlayer.dll` shares its File ID with the base; `EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll` has one name and a different File ID (copied, never linked) |
| 4 | Patch plan, restore, patch | ☑ | `--patch-plan win1` → `0 to patch, 1 already patched`; `--restore win1` → `Restored EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll from its backup` (15 994 432 bytes); `--patch win1` → `15 994 432 -> 15 899 648`, sha256 `ae35ba86… -> faef6f0b…`, identical to the base's patched binary |
| 5 | Install a mod from the Forge | ☑ | `--install-mod https://sp-mod.com/mod/download/902/bigbrain/1.4.0 --forge-id 902 --instance win1`: `.7z` read by content, `1 file(s)`, `+ BepInEx/plugins/DrakiaXYZ-BigBrain.dll`, integrity `clean=True` |
| 6 | Start and stop the server | ☑ | `--server-run win1 --seconds 45`: `Server of 'win1' ready at https://127.0.0.1:6969/ after 10.3 s (pid 9404)`; SPT's own log: `Server: 4.0.9`, `Started webserver at https://127.0.0.1:6969`, `Server has started, happy playing`; stopped by the supervisor's kill after the 15 s grace period (Windows has no clean stop for a windowless console process) |
| 7 | Read the server's log | ☑ | `SPT\user\logs\spt\spt20261105.log`: 17 550 bytes, `Server has started, happy playing` |

### The console fact this run produced

SPT 4.0.x on Windows calls `GetConsoleMode` on its standard output at startup and **aborts with
`System.Exception: Unable to get console mode`** when that handle is a pipe. The first artifact of this branch
redirected stdout and stderr to pipes, and the server died before it answered ping; the base install's own log
recorded the exception. With SPT 4.1.x the call is gone (the `SPT.Server.dll` of 4.1.6 does not contain
`SetConsoleOutputMode` at all), so the pipe path stays for 4.1 and later.

Fix in this branch: `ChildProcessStart.RequiresConsole`, set by the supervisor for SPT 4.0.x on Windows. The child is
created with `CREATE_NO_WINDOW` and no redirection, so it keeps the console Windows makes for it and its output goes to
its own log file instead of the launcher. See `docs/plan/03-verified-facts.md`.

### Still not run on Windows

- The Inno Setup installer (`.exe`), its per-user install, and the uninstall.
- A full `--launch` (server + game, detached) with the real `EscapeFromTarkov.exe`.
- The job object and `CREATE_BREAKAWAY_FROM_JOB` as a *survival* test (the launcher exits, the pair keeps running). The
  job no longer carries `KILL_ON_JOB_CLOSE`, but whether that lets the pair outlive the launcher has not been observed
  on hardware yet.
- `--stale --scan`, which stays 1.1 ([D28](../plan/04-decisions.md)).
