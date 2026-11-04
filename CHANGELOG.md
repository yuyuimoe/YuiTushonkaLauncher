# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Development builds are stamped
`X.Y.Z-pre-<sha>[.dirty]` by the build.

## [Unreleased]

### Added

- Solution split into `Core`, `Patching`, `Infrastructure`, `Ui` and `Tests` projects.
- Central package management (`Directory.Packages.props`), pinned toolchain
  (CSharpier 1.3.0, `dotnet-ef` 10.0.14) and the official launcher's code style.
- Startup composition: dependency injection, Serilog console + rolling file logging, single-instance
  guard, `IAppPaths` filesystem layout and JSON-backed `ISettingsService`.
- Persistence: SQLite + EF Core with `InitialCreate` migration, entity set for instances, catalog,
  addons, dependencies, shareable lists, runtime/server profiles; `IUnitOfWork` with commit/rollback;
  settings moved into the database; `DatabaseInitializer` runs migrations at startup.
- Value objects: `SemanticVersion` and `VersionRange` (caret/tilde/comparator/bracket ranges) with the
  real-world constraint shapes covered by tests; `coverlet.runsettings` documents coverage exclusions.
- Forge API client: typed endpoints over `filter[...]`/`query=`, burst + sustained rate limiting under
  the advertised 300/min, `cache-control`-aware response cache, retries with `Retry-After` and jitter,
  archive downloads with redirect following, `Range` resume and streaming SHA-256, plus the SPT
  release catalogue sync.
- Instances: hard-link based creation from an SPT install (per-path link policy, `Assembly-CSharp.dll`
  always copied), copy-on-write writer, layout detection (4.0 vs 4.1+), SPT/Tarkov version probing,
  patch-state detection, rename/delete, pre-flight checks (same volume, free space, link probe) and
  rollback on failure.
- Mods: identity read from assembly metadata (`[BepInPlugin]` and `IModMetadata` literals, no code
  execution), archive inspection and zip-slip-proof extraction, game-root → instance path mapping,
  install/uninstall/pin with recorded hashes, and an integrity check that flags edited configs and
  missing files.
- Dependencies and addons: dependency resolution with cycle detection and install ordering, addons
  validated against the parent mod version and stored next to it, and an update plan that honours pins
  and SPT compatibility.
- Migration: adopt an existing SPT folder without moving a file, scan its mod folders by GUID (merging
  a "both halves" mod into one entry) and register the mods with their file hashes.
- Binary patching: finds the SPT deltas, plans one decision per file, backs up before patching and
  replaces the file by rename (never in place, so hard links into the base install are safe), refuses
  builds the delta does not support, repairs a damaged file from its backup, and restores on request.
- Export / import: a `.sptpack` is a few KB listing exact mod versions plus the config files you edited, with
  no binaries. Importing shows a plan first, downloads the exact versions from the Forge, checks the content
  against what the exporter had, and never overwrites a config you edited unless you say so. Packages are
  validated hard (zip-slip, symlinks, binaries, size bombs, tampering) before anything is written.
- Fixes found on real data: versions with build metadata (`1.8.0+h4`) no longer break a mod's version list;
  downloads are no longer named after their version; addons remember their Forge id.
- Linux runtime: finds Proton builds (Steam, Flatpak, Valve's own, your folders), `umu-run`, Wine and
  GameMode; plans how to run a program through `umu-run`, Proton or Wine and reports every problem
  (missing Proton, prefix on a read-only path, no `wine`, no BepInEx DLL override) with the exact path
  before anything starts; creates and repairs Wine prefixes without ever touching a folder that is not one;
  stores runtime profiles with their prefix and environment. On Windows the whole layer is inert.
- Fixes found on real data: every Proton build was listed twice (`~/.steam/steam` is a link); a healthy
  Wine prefix was reported broken because `wineboot` exits before the registry is written.
- Headless commands: `--migrate-only`, `--inspect-base <path>` and
  `--create-instance <name> --from <base> --into <dir>` and
  `--install-mod <archive> --instance <name>` and `--migrate-scan <instance>` and `--patch-plan|--patch|--restore <instance>` and `--export <instance> <file>` and
  `--import <file> --instance <name> [--apply]` and `--runtimes`, `--runtime-profile-add`, `--runtime-assign`,
  `--runtime-plan`, `--prefix-init`, `--runtime-run`, `--server-info`, `--server-run`, `--server-profiles`,
  `--server-profile-add|remove|wipe|select`, `--server-port`, `--server-sync`, `--server-stale`.
- Launch pipeline: `--launch` checks the instance (SPT's merged layout, every file the game needs, the runtime
  command and the profile), patches only when needed, starts or adopts the server, waits for it, resolves the
  profile and starts the game detached, reporting each step by name. `--launch-plan` does all of it without
  starting anything. Patching, restoring and the layout migration refuse while the instance has a running
  process, naming it, and so do pack export and import.
- Packaging: `scripts/publish.sh` publishes both platforms framework-dependent (primary, since SPT needs .NET 10) and
  self-contained (fallback), single-file, with a Windows Inno Setup installer (per-user, `.sptpack` association,
  shortcut) and Linux `tar.gz` + AppImage with desktop entry, MIME type and AppStream metadata; checksums are published
  next to the artifacts.
- Versioning: a tag names the release everywhere (`--version`, about box, installer properties); a development build
  keeps its sha and `-pre` suffix and is never told to update.
- Update check: manual, against the newest GitHub release, with a link; nothing is downloaded or replaced.
- CI: a tag builds and publishes the artifacts, with release notes generated from Conventional Commits.
- QA: a coverage gate that fails below 90 % on Core and Infrastructure (97.7 % / 93.6 % today), a named test for
  each of the seven domain invariants, performed-and-recorded manual QA rows, performance smoke tests that print their
  numbers, a nightly-only live Forge workflow, and a mutation harness that refuses to judge a run whose test count
  changed. Enforced along the way: an unrecognised mod is never offered an automatic update.
- Logs: the launcher reads what SPT writes — the game's per-run folder, `BepInEx/LogOutput.log`, the server's
  daily file and its own log — by tailing a window at the end, and refuses files above 64 MiB with a message that
  points at a text editor (one measured session wrote 322 MiB). Export gives exactly the selected lines;
  `--diagnostics` writes a bundle with versions, instance state, hashes, mods and the last lines of each source;
  retention gzips rotated files into `archive/` and deletes by age then count, never the file in use.
- Window: a rail with instances, mods, patch, the Forge browser, the server panel, the logs and settings; a
  launch panel that walks the pipeline and a status bar that names the instance, the server, the patch state
  and the downloads. Pages are plain view models over the services, with explicit view templates instead of
  the reflection locator.
- Screens: instance cards with create/adopt (a plan is shown before anything is copied), the mods list with
  install from file or URL, pin and uninstall, addons and configs tabs, the patch plan and its restore, the
  Forge search with categories, featured and Fika filters, the server's state, output, profiles and mod-list
  sync, and a log panel that tails a source and saves a diagnostics bundle.
- Localisation: `Assets/Locales/{en,pt-BR}.json` in SPT's nested shape, dotted keys, English fallback,
  `--lang` (including `pt` and `pt_BR` spellings), a language switch that repaints the window, and no literal
  user-facing string in XAML.
- Theming: light/dark following the OS, one accent, dense rows for long mod lists; settings for language,
  theme, folders, concurrency, log retention and the two "when the game starts" switches.
- Launch button state machine: `LaunchSession` maps every step to the state a button shows, allows cancelling only
  until the game starts and offers Stop only once the pair runs.
- Remembered profile: "always use this profile on this server", stored per instance and per endpoint, so a
  launch never guesses which profile to open.
- Server lifecycle: finds the instance's SPT server and its .NET requirement, refuses to start with a named
  reason (missing file, missing runtime, busy port, port claimed by another instance), and starts it with
  readiness by `ping` polling instead of sleeping. Readiness never trusts the exit code, because a busy port
  makes the real server exit **0**. Stop is `SIGTERM` first, then a kill; the server is tied to the launcher
  (`setpriv --pdeathsig TERM` on Linux, a job object on Windows) so even a `kill -9` of the launcher leaves no
  server, and a leftover from a crash is found from a pid record and stopped.
- The SPT launcher API: `/launcher/v2/{ping,version,types,profiles,profile,login,register,remove,wipe,mods,mod-pages}`
  over HTTPS with SPT's self-signed certificate and zlib-compressed JSON, plus the profile's recorded client
  mods.
- Profiles: list, create, select, delete and wipe through the server, validated here because the server
  validates nothing (it accepts an unknown edition or an empty name), with an explicit confirmation and a
  verified copy of the profile file before anything destructive.
- Ports: `http.json` is the single source, edited through the copy-on-write writer, mirrored in the database,
  with the next free port suggested and a refusal to give two instances the same one.
- Server sync: compares an instance with what a server runs and installs the exact versions from the Forge,
  honouring pins; the client-mod half is compared, never written, because the game records it.
- Fixes found on real data: `SPT.Server.Linux` has no extension to strip, so the locator read the Windows
  runtimeconfig; a child that exits at once crashed the spawner on `Process.StartTime`; a busy port ends in
  exit code 0; `MainModule` can fail for another process and now falls back to `/proc/<pid>/exe`; the free-port
  search found nothing when started below 1024.
- Headless commands: `--server-info`, `--server-run`, `--server-profiles`,
  `--server-profile-add|remove|wipe|select`, `--server-port`, `--server-sync`, `--server-stale`.
- Git workflow: GitFlow, Conventional Commits hook, `X.Y.Z-pre-<sha>[.dirty]` versioning, `wt`
  worktrees.
- Documentation: per-phase plan under `docs/plan/`, verified SPT/Forge facts, decisions D1–D21.

### Fixed

- **Running the launcher from an IDE showed no window.** Building the window resolves the process spawner, whose
  constructor asked the machine whether `systemd-run` works and *blocked* on that child process; the continuation was
  posted to the Avalonia dispatcher, which has not started pumping yet, so start-up deadlocked before the first frame.
  The probe is now decided on first use instead of in a constructor, `ProcessRunner` never captures a caller's
  synchronisation context, and two regression tests keep the start-up path free of that dependency.
- A crash on a background thread or an unobserved task no longer disappears without a log line.
