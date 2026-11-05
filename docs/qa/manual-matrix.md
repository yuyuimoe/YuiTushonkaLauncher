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
| 8 | Build and run the Inno Setup installer | ☑ | Built `YuiTushonkaLauncher-0.2.0-rc.2-win-x64-setup.exe` with Inno Setup 6.7.4 (per-user, no admin): silent install exit 0, installed to `%LOCALAPPDATA%\Programs\YuiTushonkaLauncher`, the installed `--inspect-base C:\SPTarkov` answered `shape Merged, layout Spt40, SPT 4.0.9`, and the `HKCU\Software\Classes\application/x-sptpack` association was created |
| 9 | Uninstall and keep the data | ☑ | `unins000.exe /VERYSILENT` exit 0; the install directory is gone, `%LOCALAPPDATA%\YuiTushonkaLauncher` survives with `launcher.db` (221 184 bytes), `cache`, `downloads`, `instances`, `logs`, `run`; the `.sptpack` association was removed |
| 10 | Full `--launch` with the real game | ☑ | `--launch win1 --profile qa-user --game-program ...\EscapeFromTarkov.exe`: `server pid 9824 … game pid 2260 … Detached`; port 6969 answered; the launcher exited |
| 11 | Job object / breakaway survival | ☑ | After the launcher exited, `--stale` listed `server pid 9824 … Running [WindowsJob]` and `game pid 2260 … Running [WindowsJob]`; `--stale --reap` reported `Stopped 2 process(es)` and both were gone seconds later; `CREATE_BREAKAWAY_FROM_JOB` plus no `KILL_ON_JOB_CLOSE` works on hardware |

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

- The real game's first frame: the VM has no GPU, so `EscapeFromTarkov.exe` starts and stays alive but never renders.
  The process launch and the detach are proven; the game's own screen is not.
- `--stale --scan`, which stays 1.1 ([D28](../plan/04-decisions.md)).

### The installer's version bug this run produced

The `.iss` fed `VersionInfoVersion={#MyAppVersion}`, and the workflow fed the tag (`v0.2.0-rc.2`). Inno Setup's
`VersionInfoVersion` only accepts numbers, so the build failed with *Value of [Setup] section directive
"VersionInfoVersion" is invalid* for every tag — the Windows job had never run, because every release so far was a
pre-release and the Windows job is gated on a label-free tag. The fix: `launcher.iss` takes `MyVersionInfoVersion`
separately (the text version may carry a `v` and a `-label`), and the workflow strips the `v` and the label and pads
the number to three parts before calling `ISCC.exe`. Proven on the VM: with `MyAppVersion=0.2.0-rc.2` and
`MyVersionInfoVersion=0.2.0`, the installer compiled and produced
`YuiTushonkaLauncher-0.2.0-rc.2-win-x64-setup.exe`.

## Release 0.2.0-rc.4 — Linux pipeline and GUI launch (2026-11-05)

Host: Fedora (the development machine), real display `:0`, default `XDG_DATA_HOME`. The tag carries the
update-checker fix (it reads the release list, so pre-releases are seen). A pre-release is Linux only ([D23](../plan/04-decisions.md)),
so the Windows job reported *skipped*; the Windows path was proven on the VM during `0.2.0-rc.2` (above).

| # | Flow | Result | Evidence |
|---|------|:------:|----------|
| 1 | Workflow produces the Linux artifacts | ☑ | `YuiTushonkaLauncher-0.2.0-rc.4-x86_64.AppImage`, `-linux-x64.tar.gz`, `-linux-x64-selfcontained.tar.gz`, `SHA256SUMS`; the Windows job reported *skipped* |
| 2 | Checksums match the published files | ☑ | `sha256sum -c SHA256SUMS`: all three artifacts `OK` |
| 3 | `--version` prints the tag | ☑ | the AppImage and the framework-dependent tarball both print `0.2.0-rc.4` |
| 4 | Update check against the new release | ☑ | the released build: `Newest release: v0.2.0-rc.4 … This build is up to date (or ahead of the last release)`; an older build (`0.1.0-rc.1`) reported `A newer release is available` |
| 5 | Launch and close the GUI | ☑ | `DISPLAY=:0` launch reached `Startup: window ready`; `timeout -s TERM 25` ended it with exit 124 and no process was left behind |
| 6 | Re-run the workflow for the same tag | ☑ | a second run for `v0.2.0-rc.4` produced the same file names and a passing test run; the gzip tarball differs by 91 bytes because gzip records the build time, while the AppImage and the self-contained tarball are byte-identical |
| 7 | Double-click a `.sptpack` on Linux | ☐ | needs a desktop session with the association installed; the Windows installer's association was proven in `0.2.0-rc.2` |
