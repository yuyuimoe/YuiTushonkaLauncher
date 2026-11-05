# Decisions

Resolved by the maintainer on 2026-11-04, informed by [verified facts](03-verified-facts.md) and the
[review](../raw_plan_review.md). These override the raw plan wherever they disagree.

## D1. Project naming

Prefix stays **`YuiTushonkaLauncher`** (repo and app already exist): `YuiTushonkaLauncher.Core`,
`.Patching`, `.Infrastructure`, `.Ui`, `.Tests`. Namespaces follow the prefix, so
`YuiTushonkaLauncher.Core.Modding` etc.

## D2. EF Core over Dapper with SQLite

`Microsoft.EntityFrameworkCore.Sqlite` + migrations; no hand-rolled SQL. Migration commands (house
style, from `AGENTS.md` conventions in sibling repos):

```
dotnet ef migrations add <Name> -p YuiTushonkaLauncher.Infrastructure -s YuiTushonkaLauncher.Ui
dotnet ef database update  -p YuiTushonkaLauncher.Infrastructure -s YuiTushonkaLauncher.Ui
```

The `DbContext` is configured through one `AddLauncherDbContext(...)` extension, used by app and
tests, so provider options never diverge. SQLite with `PRAGMA journal_mode=WAL` and
`foreign_keys=on`. Schema evolution is **additive-first**: no destructive migration without a data
copy step.

## D3. Forge API client: no auth, 300/min, cached

- Base URL is configuration with default `https://sp-mod.com/api/v0`.
- **No API key.** A descriptive `User-Agent` (`YuiTushonkaLauncher/<version> (+https://sp-mod.com)`)
  is sent on every request so we are identifiable.
- Rate limiting with `System.Threading.RateLimiting` (as the official launcher does), but driven by
  the server's own headers: read `x-ratelimit-limit` / `x-ratelimit-remaining`, back off on `429`,
  and keep a conservative sliding window until the first response is seen.
- Respect `cache-control: max-age` (900 s) with an in-memory + SQLite response cache keyed by URL.
- All calls are cancellable (`CancellationToken`) and retried with jittered backoff on
  `408/429/5xx`; downloads resume with `Range` when the host allows it.

## D4. Tarkov files are user supplied, SPT files are downloaded

The manager **never** downloads Tarkov. The user points it at a folder produced by the official SPT
installer (already patched or patchable), exactly like `base_tarkov` / `base_tarkov41` here.
What we do download:

- SPT runtime files (from the `SPT-Tushonka/build` release the Forge API links, or a local archive the
  user already has);
- mods and addons from the Forge API;
- never BattlEye, never `EscapeFromTarkov.exe`, never game data.

If the pointed folder is missing the SPT runtime, the UI explains which installer output is expected
instead of trying to fetch the game.

## D5. Assembly-CSharp.dll is never linked

`EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll` is **copied** into every instance at creation
time, in both layouts, whether or not it is already patched. A patch (or a game update) that wrote
through a link would corrupt the base install and every other instance. This supersedes the raw
plan's "hard link the binaries" phrasing and matches the user's existing script.

## D6. Copy on write before every write

Every write path goes through `ILinkAwareFileWriter`, which checks the link count and detaches
(rename-based copy, same directory, atomic) before writing. Rationale in
[01-architecture](01-architecture.md#copy-on-write-invariant). A test asserts the base install's
inodes are unchanged after every scenario that writes into an instance.

## D7. Per path link policy

Instead of "link everything, then unshare one file", the instance builder uses the explicit policy
table in [01-architecture](01-architecture.md#link-policy). Unknown paths default to **copy**.
`BepInEx/plugins`, `BepInEx/config` and `SPT_Runtime/user/**` are always private per instance.

## D8. Patching uses SharpHDiffPatch and .delta files

Mirror the official launcher's behaviour rather than inventing a new one:

- deltas: `*.delta` discovered under `<runtime>/SPT_Data/Launcher/Patches`, applied to the game root;
- engine: `SharpHDiffPatch.Core` (`HDiffPatch.Initialize` + `Patch`), pinned version, wrapped in
  `YuiTushonkaLauncher.Patching`;
- backup: `<file>.spt-bak`, created once, never overwritten; patch is always `backup → target`;
- restore: walk for `*.spt-bak` (skipping reparse points on Linux), delete + copy back;
- **added by us**: post-patch verification (size checked against the delta header, SHA-256 recorded in
  `PatchInfo`), and a pre-patch guard that refuses a target whose size matches neither the delta's
  declared input nor its declared output. The guard reads the **delta header** instead of a table of
  known hashes: nothing to ship or keep current, and it works for any future delta (phase 7 deviation,
  see `03-verified-facts.md#patching`);
- **never written in place**: the engine writes `<file>.spt-patch`, the result is verified, then renamed
  over the target. A rename gives the instance a new inode, so a hard link into the base install can
  never be modified (D6) and a crash leaves the original;
- a target that matches neither build is **re-patched from `.spt-bak`** when the backup is the
  pre-patch build (an SPT upgrade, or a half-written file), and refused when it is not;
- `SPT.ByteBanger` is not part of the design — it no longer exists in 4.x.

## D9. Two mod identity readers

`IModIdentityReader` with:
`BepInPluginIdentityReader` (client DLL → `[BepInPlugin]` GUID/name/version) and
`ServerModIdentityReader` (server DLL → type implementing `IModMetadata`, read through
`MetadataLoadContext`). Both produce `PackageId(kind, guid)` + `ModVersion` info. Unparseable DLLs
become `Recognition.GuidMissing`, which blocks auto-update but never blocks installation or launch.

MetadataLoadContext caveat: resolving a type that implements an interface from another assembly needs
that assembly (and its references) in the resolver's path list — the reader feeds it
`SPT_Runtime/*.dll` + `BepInEx/core/*.dll` from the instance, never the app's own assemblies.

## D10. Separate tables for mods and addons

The Forge models them separately (`/mods` vs `/addons`, `addon.mod_id`, and addon versions carry
`mod_version_constraint` instead of `spt_version_constraint`), and the sibling
`TushonkaMirror` archive does the same (`Mod`/`Addon` entities over separate tables). We mirror that:
`Mods`, `ModVersions`, `Addons`, `AddonVersions`, no shared inheritance; a UI-only `IPackage` read
model avoids duplicated view code.

## D11. i18n and UI tests from day one

- All user-facing strings come from locale files (`Assets/Locales/<culture>.json`) loaded at startup;
  `pt-BR` and `en` ship first. Schema mirrors SPT's `SPT_Data/Launcher/Locales/*.json` so community
  translations can be reused.
- `Avalonia.Headless.XUnit` covers view models and critical views from Phase 12 onward (and
  `Avalonia.Headless` unit-level checks of bindings where useful).

## D12. SPT and Tarkov compatibility matrix

The pairing that must be enforced everywhere:

| Layer | Source of truth | Rule |
|---|---|---|
| Tarkov build ↔ SPT version | `/spt/versions` (`version`, `mod_count`, `link`) + the installer that produced the base folder | an instance records the pair it was created from; upgrading SPT requires a Tarkov build that the target SPT version supports |
| Mod version ↔ SPT version | `ModVersion.SptConstraint` (`^4.1.0`, `>=4.0.0 <5.0.0`) and `IModMetadata.SptVersion` | a mod may be installed only if its constraint admits the instance's SPT version |
| Addon version ↔ mod version | `AddonVersion.mod_version_constraint` | an addon pins a **mod** version range, not an SPT one |
| Installed mod ↔ installed mod | `IModMetadata.ModDependencies` (guid → range), `Incompatibilities`, Forge `/mods/dependencies` | resolution refuses to produce a set that violates any constraint |

`ICompatibilityService` exposes three questions: `CanInstall(package, instance)`,
`CanUpgradeSpt(instance, targetSpt)`, `Resolve(requests, instance)`. All three are pure functions over
the matrix, which is why they live in Core and are the primary coverage target.

## D13. Server lifecycle is part of the single-player flow

SPT's model requires the local server to run **while** the player is in a raid, so "server management"
is not an optional multiplayer feature — it is the launch path:

- start the server, wait for readiness (`/launcher/v2/ping`), then launch the game;
- the launcher-side profile operations go through the server API the official launcher uses:
  `/launcher/v2/{ping,login,register,profiles,profile,wipe,mod-pages,version}`;
- the client mod list is reported to the server (`mod-pages` / client-mod reporting) so the server's
  `ActiveClientMods` matches what is installed;
- bundle sync (`/singleplayer/bundles`, `/files/bundle/`) is reachable through the same client;
- stopping the app stops the server (graceful → `SIGTERM` → kill), with no orphaned processes.

`ServerProfile` keeps port/URL/credentials-per-instance; the server's own profile list is the source
of truth for profiles, we cache it for the UI.

## D14. No native AOT

Dropped from the plan. Reason: reflection-heavy DI, `MetadataLoadContext`, Avalonia's XAML loader and
EF Core are all AOT-hostile, and the payoff (startup time) is small for a desktop app that lives for
hours. Trimming stays *possible* but off by default; `ViewLocator`'s reflection template is replaced
by explicit view registration in Phase 12 so the option remains open.

## D15. .NET 10 is mandatory

`net10.0` is required because SPT itself ships and requires .NET 10, so the runtime is guaranteed on
every machine that can play SPT. That is also why the app can be framework-dependent and still
"work out of the box": no runtime bundling needed on the primary path (self-contained stays available
as a fallback artifact).

## D16. Doorstop and BepInEx are managed for developers

Advanced users get first-class control over the injection layer: pinned `doorstop_config.ini`
(`enabled`, `target_assembly`, `redirect_output_log`, `[UnityMono] debug_enabled/debug_address/
debug_suspend`), BepInEx version display/repair, and logs (BepInEx `LogOutput.log`) surfaced in the
log viewer. Prepatcher JSON (`user/patchers/{ModGuid}`) is detected and validated because a mod
declaring `HasPrepatcher=true` without the file will fail to load.

## D17. Logs rotate, compress and expire

Per-source Serilog files + a session-scoped stream for the UI. On rotation the previous file is
gzipped into `user/logs/archive/`; retention by count and age (configurable, Prism-Launcher-like),
with export of a selection as `.txt`/`.log`. Unified view merges app / server / game / Proton with
per-source filters and timestamps normalized to local time.

## D18. Code style is the official launcher's

Follow `SP-Tushonka/launcher` exactly, so contributions can move between the two projects:

- **CSharpier** owns formatting, pinned as a local tool (`.config/dotnet-tools.json`, currently 1.3.0).
  `dotnet tool restore` then `dotnet csharpier format .`; CI runs `dotnet csharpier check .` and fails
  the PR otherwise. `.csharpierignore` excludes build output, migrations and hand-aligned path tables.
- **`.editorconfig`** carries the semantic rules (CSharpier ignores them): 140-column limit, 4-space
  indent, file-scoped namespaces, braces always, block-bodied members (expression bodies only for
  lambdas), `var` preferred, `_camelCase` private/internal fields, PascalCase constants, `using` outside
  the namespace, `CS1591` and `CA1822` suppressed.
- `EnforceCodeStyleInBuild` is on, so violations surface as build warnings instead of review comments.

## D19. A `.sptpack` is a list of names, never a payload

A package is untrusted input that can ask the launcher to download and write things, so it is designed to
carry as little authority as possible (phase 8). The defaults listed in open question 1 are now code:

- **No binaries, no URLs.** A package holds `manifest.json`, a generated `README.txt` and edited config
  files. It names mods by `forgeId` + exact version; the download address always comes from the Forge's own
  answer and must be `https`. An executable extension (`.dll .exe .bundle .pak .dat .so …`) is rejected
  wherever it appears, with its own error, before the allowlist (`.json .ini .toml .yaml .yml .txt .cfg`).
- **A package can only write files a mod it names already owns.** Each config entry must name its owner
  (`modGuid`); at apply time the path must be a tracked file of that installed mod. Anything else is not
  written. Paths are checked by pure rules (`PackRules`) and again against the real file system
  (`PackPathGuard`: containment plus no symbolic link anywhere on the way).
- **Everything is validated before anything is returned**, in a fixed order: entry count, entry names
  (zip-slip, absolute, backslash, device names, trailing dots, symlinks, duplicates), forbidden types,
  bytes counted while reading (header sizes are attacker-controlled), format version before schema (a newer
  format gets an upgrade hint), strict schema, share code, per-config hash.
- **Downloaded content is checked against the exporter's.** Each mod carries `contentSha256`, a digest of the
  hashes *as shipped*; after installing, the import recomputes it. A mismatch (a host serving a different
  file under the same version) removes a fresh install and fails, unless the user accepts changed content.
- **Review before write.** `PlanImportAsync` writes nothing; the CLI applies only with `--apply`; a local
  edit of a config wins over the package unless the user chooses to overwrite it.
- **The share code is not a signature.** It is the SHA-256 of the canonical composition (guid, version, kind,
  pin, forge id, content digest, config hashes, SPT version, layout) and excludes timestamps, instance name
  and display names. It catches damage and accidental edits and makes two exports of the same composition
  comparable; anyone who can build a package can compute it. Authenticity (signing, a producing-install
  hash) is deliberately not claimed — see open question 1.
- **Applying a config moves the integrity baseline.** `InstalledFile.Sha256` becomes the imported hash and
  `OriginalSha256` keeps what the mod shipped, so an imported instance is integrity-clean and a re-export
  reproduces the same package.

## D20. The runtime layer plans first, runs second, and never guesses

How a Windows program starts on Linux (phase 9) follows the official launcher's discovery rules and
`umu-run` model, with these differences:

- **A plan is a value.** `IRuntimeLauncher.Plan` returns a command plus every problem found, and writes
  nothing, so misconfiguration is reported with the exact path *before* anything starts. Errors block;
  warnings (a missing `winhttp=n,b` override, umu absent, GameMode not running, a prefix still to be
  created) do not.
- **Preference order:** `umu-run` with a Proton build → Proton's own `proton` script → plain `wine`. A
  Proton profile uses umu whenever it is installed. All three were run for real (a Windows `cmd.exe` writing
  a file inside a fresh prefix).
- **The launcher owns six variables** (`WINEPREFIX`, `PROTONPATH`, `GAMEID`, `STORE`,
  `STEAM_COMPAT_DATA_PATH`, `STEAM_COMPAT_CLIENT_INSTALL_PATH`). A profile cannot override them: two
  settings fighting over the prefix is worse than a refusal.
- **`WINEDLLOVERRIDES=winhttp=n,b` is a default, not a hardcode.** BepInEx loads through Unity Doorstop's
  `winhttp.dll`, which Wine only uses when told to. New profiles start with it (as the official launcher's
  `DefaultEnv`); removing it yields a warning that names the consequence.
- **Never wipe, never guess a prefix.** A folder that is not empty and has none of Wine's files is refused
  untouched; a partial prefix is repaired in place by `wineboot`; nothing deletes a prefix. After
  `wineboot` exits the launcher waits (bounded) for `system.reg`/`user.reg`: Wine's server writes them
  seconds later.
- **The prefix lives in the instance** (`<instance>/wineprefix`) unless the profile names one, so an
  instance is self-contained. Its cost (0.4–0.7 GB) is recorded as an open question.
- **Wine's output is logged, not echoed**, and no TTY is needed: stdin is closed so a tool that asks a
  question fails instead of hanging; the whole process tree is killed on timeout or cancel; a daemon that
  keeps the pipes open cannot hang the runner.
- **On Windows the layer is inert.** Every request resolves to a direct native command, the locator
  searches nothing, and the Linux settings stay hidden.


## D21. The server is a supervised child, never a second-class process

The SPT server is not "something the user starts"; it is part of the launch (D13). Phase 10 fixes how we own it.

- **One supervisor owns every instance's server.** `ISptServerSupervisor` keeps at most one server per
  instance, keyed by instance id, with a per-instance gate: concurrent start/stop calls queue instead of
  racing, and the status is a value the UI can read at any time.
- **Plan before start, always.** `PlanAsync` reports the executable, the working directory, the endpoint,
  the .NET requirement and every problem (missing file, missing runtime, busy port, port configured for
  another instance, dead-server signal unavailable) with the exact path. Errors block; "another instance is
  configured for this port" and "listens on the whole LAN" only warn.
- **Readiness is a ping, never a sleep**, and a ping counts only while the process we started is alive: a
  different server on the same port would answer just as well, and the real server exits **0** on a busy
  port, so the exit code proves nothing.
- **Children are tied to the launcher's life.** Linux: `setpriv --pdeathsig TERM` in front of the command,
  started from one dedicated thread that never ends (the signal belongs to the creating *thread*); Windows: a
  job object with kill-on-close. When the machine cannot do it (no `setpriv`), the plan says so and a leftover
  is found later from the pid record.
- **A leftover is recognised by more than its pid:** the record stores the instance, the start time, the
  executable path and the launcher's pid, and a candidate must match the start time and the path; a recycled
  pid is never touched. A server whose launcher is still alive is never a leftover.
- **Stopping is polite first.** `SIGTERM`, then kill after the grace period; the same stop owns the monitor's
  view, so an exit the user asked for is never reported as a crash nor restarted.
- **The server owns its profiles.** Everything goes through its API; the launcher validates because the
  server does not, refuses to destroy anything without an explicit confirmation, and copies the profile file
  (verified byte-for-byte) before asking the server to delete or wipe.
- **One source for the port: `http.json`.** The database row mirrors it; the writer is copy-on-write, so an
  instance never edits the base install's file.
- **The API is TLS-with-nothing-to-verify.** SPT's certificate is self-signed and created on first start, so
  the client accepts any certificate, exactly like the official launcher. No secret crosses it: a profile is
  selected by name.

## D22. The server and the game are detached, and adopted

Decided by the maintainer on 2026-11-04: **both** processes are spawned with no direct relationship to the
launcher. An OOM kill or a crash of the launcher must not take them, and the launcher never kills them by
merely exiting.

- **Mechanism:** `systemd-run --user --scope` when the distro has systemd (its own cgroup, so a cgroup-wide
  OOM kill of the launcher's scope does not reach it), otherwise `setsid`. The mechanism used is recorded.
  Windows: a job object **without** `KILL_ON_JOB_CLOSE` plus `CREATE_BREAKAWAY_FROM_JOB`.
  This replaces [D21](#d21-the-server-is-a-supervised-child-never-a-second-class-process)'s
  `--pdeathsig` tie.
- **The promise flips.** "Killing the app leaves no child process" becomes "an orphan is found, shown and
  stoppable": the phase 10/11 acceptance criterion is rewritten, not silently dropped.
- **One record per instance for the pair** (`server` + `game`), written *before* the process is released, so
  there is no window in which a process exists and the record does not. Adoption requires pid **and** start
  time **and** executable path to match; a recycled pid is never touched.
- **Coherence is the launcher's job while it is open.** A game whose server died is presented as broken, with
  the reason and a manual "restart server". Nothing auto-restarts (a mod crashing the server would loop) and
  nothing kills the game (that would throw away the user's session).
- **Closing the launcher asks**, defaulting to letting them run, and can be silenced by a setting. Two
  convenience settings the maintainer asked for: "close the launcher when the game starts" and "minimise the
  launcher when the game starts".
- **Patching and migrating refuse while the pair runs**, naming the process: with detached processes,
  "nothing else writes to these files" is no longer a safe assumption.

## D23. The 1.0 gate is three items, and the releases are sequenced

Settled with the maintainer on 2026-11-05, after a grilling session over every open question.

- **The gate** — what must be true before the `1.0.0` tag: (1) the app runs on **real Windows** (installer,
  instance, server, mod install, launch, logs, job object/breakaway), with the Tarkov files present on that
  machine; (2) **`.7z` and `.rar` mods install**, which is most of the popular catalogue (see D24);
  (3) a **visual smoke** passed with a written list of what is ugly but not broken (`docs/qa/known-ugly.md`).
- **Not gate items, deliberately:** byte identity with the official launcher (done whenever a clean install is
  patched by the official launcher — evidence, not a blocker), the mutation-harness anomaly, designed
  light/dark palettes (the layout is temporary and will be redesigned), and everything in D26–D30 that does
  not have a user waiting for it.
- **Sequence:** tag `v0.1.0` on the state that exists (it exercises the release pipeline for the first time),
  then 7z + remote servers (D24, D25), then `v0.2.0`, then the gate, then `1.0.0`. The gate is checked
  against an app that already has the remote-server feature inside it, not against a version that must gain
  it afterwards.
- **The lesson that produced this rule:** the project had 13 "before 1.0" items and no release. A gate that
  never closes is not a gate.

## D24. Archives: 7z and rar are read, the format comes from the bytes, packs stay zip

Measured before deciding: of the 50 most-downloaded mods that answered a HEAD request, **19 ship `.7z`** and 28
`.zip` — and the `.7z` ones include BigBrain and Waypoints, the two dependencies SAIN declares. The current
installer opens everything with `ZipFile`.

- **Read** `.7z`, `.rar` and `.zip` through **SharpCompress** (managed, no native dependency). Writing stays
  `.zip`: a `.sptpack` must remain a file a person can open and audit.
- **The format is detected from the content**, never from the name: the Forge download URL ends in the version
  (`/mod/download/902/bigbrain/1.5.0`), so the current code saves every download as `mod.zip` regardless of
  what it is. Magic bytes decide (`7z¼¯'`, `Rar!`, `PK`).
- The archive inspection that classifies entries runs on the same reader, so a `.7z` gets the same entry
  vetting as a `.zip` — no second, weaker path.

## D25. A server that is not this machine

The maintainer asked for a **server picker**: choosing the local entry starts the instance's server with it;
choosing a remote one (`host` other than loopback) connects to it and lists its profiles.

- **The address travels as the launch argument** we already build:
  `-config={'BackendUrl':'https://host:port',…}`. Nothing is written to a config file, so
  [D6](#d6-copy-on-write-before-every-write)'s invariant is untouched.
- **The picker appears when there is more than one server**, remembers the last choice per instance, and
  offers "always use this one".
- **A remote server is trusted on first use, without blocking.** Its self-signed certificate is accepted
  (requiring a CA would reject every real SPT server and break Fika), and the **fingerprint is recorded**; if it
  changes, the launcher says so instead of silently accepting a new one. Loopback keeps accepting anything —
  it is the server we started ourselves.
- **Remote profiles can be listed, selected, created, renamed and wiped**, because that is what a player with
  friends needs before the permission model exists. Destructive actions on a remote server ask for the profile
  name to be typed and name the server they will act on. A future server-side permission sync can tighten
  this; the launcher's dialogs are written so that tightening is a policy change, not a rewrite.

## D26. `.sptpack` stays integrity-only for 1.0

- No signatures, no producing-install hash: the format keeps `formatVersion` 1 and gains a **reserved
  `signature` field that is null**, so a v2 that signs does not have to break v1 files.
- Because names inside a package are unauthenticated, the review screen shows, for every entry: the declared
  name, its **GUID**, the **Forge-confirmed name** (or "not on the Forge"), the version, and a
  **"not verified" badge at the top of the dialog** — the reader must know what they are looking at before
  reading the list.
- Shell integration (MIME type, double-click import, registry/`.desktop`) waits for 1.1: without signed
  packages, registering a file type that opens a privileged flow only adds surface.

## D27. Update sources, and nothing downloads itself

- **Mods:** the Forge first; if the mod is not on the Forge (D28's external link), the project's GitHub or
  GitLab releases; otherwise "check manually". A mod that is not on the Forge gains an optional repository link
  recorded per installed mod, and the launcher may compare the installed version with the repository's latest
  release.
- **The app itself:** GitHub Releases plus the existing manual check. The Forge has no application-release
  endpoint, and inventing one is backend work, not launcher work.
- **Nothing is downloaded without a click.** An available version is shown, with the page to go to.

## D28. Small decisions, all settled in the same session

| Point | Decision |
|---|---|
| Detached process output (Q21) | The process writes to **a file it owns**; the launcher follows the file. No inherited pipe, so a launcher crash cannot leave a process writing into a dead pipe. Windows keeps the pipe until it has been run for real. |
| Wine prefix (Q19) | A **golden prefix cloned with reflinks where the file system supports them, a copy otherwise**, per instance. |
| `IsConfig` and `.txt` (Q14) | `.txt` counts as config, same extension rule for export and integrity. |
| Exporting a log selection above 64 MiB (Q18) | Allowed, written to a file the user chooses; the viewer still stops at the limit. |
| `http.json` on `0.0.0.0` (Q18) | Warned and **offered** at instance creation (checkbox to pin `127.0.0.1`); never rewritten silently. |
| Mods the Forge does not have (Q16) | Reported and linked; an optional repository link per mod (D27). No "copy from this install". |
| The server's profile backups (Q17) | Left to the server; the launcher only shows where they are. |
| `GAMEID` for umu (Q18) | `umu-sptushonka` — it exists in the umu database (store `none`). `umu-default` does not. |
| Linux hardening (Q10) | Documented, no `winetricks`/`winecfg` buttons. |
| `LayoutVersion` (Q17) | **Inferred from the disk**; the folder is the source of truth. |
| `--stale --scan` (Q24) | Linux only: `/proc/<pid>` matched by **executable inside the instance root *and* the port it holds**; `--reap` kills only what matched both. Nothing on Windows until it has been run there. |
| Light/dark palettes (Q25) | Out of the gate: the layout is temporary and will be redesigned. |
| Dependents index (Q31) | The complete catalogue walk stays, with **the date of the index and a refresh button** in the tab. |
| Description parser (Q32) | Frozen: no tables, footnotes or reference links unless a real description needs them. |

## D29. Two documents that make gaps visible instead of remembered

- `docs/tech-debt.md` — one line per accepted debt: where it hurts, why it was accepted, what would close it.
  It never blocks a release, and it starts with the mutation-harness anomaly, the dependency graph's 99
  requests per rebuild, and the game's `-config` built by string concatenation.
- `docs/qa/known-ugly.md` — filled in during the visual smoke (D23): one line per screen, what is ugly, and
  what is *not* broken. Passing the smoke means: every screen opened in light and dark, with 1000 rows, and in
  a small window, with scrolling where it is needed.
