# Open questions

Everything still unresolved, with who/what resolves it.

## Resolved in the grilling session of 2026-11-05

Every question below that was still open was put to the maintainer in four rounds and answered. The items are
kept with their original text for context; each line here says where the decision now lives.

| Question | Decision |
|---|---|
| The release gate, tag sequence, and what waits for 1.1 | [D23](04-decisions.md#d23-the-10-gate-is-three-items-and-the-releases-are-sequenced) |
| `.sptpack` authenticity — integrity only, reserved `signature`, review screen, MIME in 1.1 | [D26](04-decisions.md#d26-sptpack-stays-integrity-only-for-10) |
| 7z/rar archives, format by content, packs stay zip | [D24](04-decisions.md#d24-archives-7z-and-rar-are-read-the-format-comes-from-the-bytes-packs-stay-zip) |
| Servers that are not this machine: picker, TOFU fingerprint, remote profile management | [D25](04-decisions.md#d25-a-server-that-is-not-this-machine) |
| Update sources for the app and for mods — nothing downloads itself | [D27](04-decisions.md#d27-update-sources-and-nothing-downloads-itself) |
| Detached process output, Wine prefix, `.txt` as config, log export above 64 MiB, `http.json` on `0.0.0.0`, mods absent from the Forge, server profile backups, umu `GAMEID`, Linux hardening, `LayoutVersion`, `--stale --scan`, palettes, dependents index, description parser | [D28](04-decisions.md#d28-small-decisions-all-settled-in-the-same-session) |
| Where accepted debt and "ugly but not broken" are written down | [D29](04-decisions.md#d29-two-documents-that-make-gaps-visible-instead-of-remembered) |

The facts that decided several of them, measured during the session: 19 of the 50 most-downloaded mods that
answered ship `.7z` (including BigBrain and Waypoints); the client plugin drives `singleplayer/bundles` and
`user/cache/bundles` itself, so the launcher does not; the backend address travels as the
`-config={'BackendUrl':…}` launch argument, not as a file; and the umu database has `umu-sptushonka`, not
`umu-default`.


## Blocks Phase 8 (export/import)

1. **Threat model for `.sptpack` — resolved.** Decided in the 2026-11-05 session: integrity only, `formatVersion` 1
   with a reserved `signature` field, always show the GUID and the Forge-confirmed name, MIME in 1.1
   ([D26](04-decisions.md#d26-sptpack-stays-integrity-only-for-10)). The original text follows for context.

1. **Threat model for `.sptpack` — defaults implemented, authenticity still open.** The baseline is now
   code and documented as [D19](04-decisions.md#d19-a-sptpack-is-a-list-of-names-never-a-payload): no
   binaries, no URLs from the package, writes only into files a named mod owns, strict validation, content
   digest against the exporter's, review before write. What is **not** decided: should a package be
   *signed*, and should it embed a hash of the producing install? Without either, the share code proves
   integrity only, never who made the package. Still worth a dedicated grilling session before 1.0, because
   it changes the format (a `formatVersion` 2).

2. **Share-code semantics — decided.** The code is the SHA-256 of the canonical *composition* (not of the
   file): it excludes `createdAt`, `appVersion`, `instanceName` and display names, so two exports of the
   same composition match. See D19.

## Blocks nothing now, but must be settled before 1.0

3. **SPT version upgrade path.** In-place `Restore()` → swap runtime → re-`Patch()` on the same
   instance, or "create a new instance and copy `user/`"? Affects `ICompatibilityService.CanUpgradeSpt`
   and the UI flow.
4. **Multi-instance server ports — decided in phase 10.** `SPT_Data/configs/http.json` is the single
   source (verified: changing it changes what the server binds); our instances get their own copy, so each
   instance edits only its own file through the copy-on-write writer, and the database row mirrors it
   ([D21](04-decisions.md#d21-the-server-is-a-supervised-child-never-a-second-class-process)).
5. **Bundle sync.** `/singleplayer/bundles` + `/files/bundle/` look like an asset sync we might need
   to drive. **Still open after phase 10:** the routes exist in the server assembly but were not exercised;
   the mod sync did not need them (the server publishes its mod list). Exercise them before phase 11 or 12
   if the game expects the launcher to drive the download; a wrong guess wastes download bandwidth.
6. **`.sptpack` MIME + shell integration — done.** Windows registers `application/x-sptpack` in `HKCU` (the
   installer's optional "associate" task); Linux ships a `.desktop` entry with `Exec=YuiTushonkaLauncher %f`, the MIME
   XML, and an `install.sh` that sets it as the default. The app reads the path out of its command line and opens a
   review, never a silent import (`PackOpenService`, `PackOpenTests`;
   [D30](04-decisions.md#d30-windows-ships-in-every-release-and-the-shell-association-opens-a-pack-review)). The desktop
   double-click itself is the one row still unticked in `docs/qa/manual-matrix.md`: it needs a desktop session with the
   association installed.
7. **Update channel for the app itself.** The official launcher has a signed manifest updater
   (`UpdateManifest`/`ManifestSignature`/`UpdateTransaction`). Do we build an equivalent, or ship via
   GitHub releases only?

## Non-blocking watch items

8. **Trimming/AOT** — dropped ([D14](04-decisions.md#d14-no-native-aot)); revisit only if startup time
   becomes a complaint and `ViewLocator` is gone.
9. **7z/rar mod archives** — the Forge hosts arbitrary archives (Dropbox links, 7z, rar). Phase 5
   starts with zip; 7z support reuses the `SevenZip` abstraction pattern from the official launcher.
   Confirm which formats actually appear in the catalogue before building a native dependency.
10. **Linux hardening** — the official launcher's `LinuxHelper` runs `winecfg`/`winetricks` inside the
    prefix. Decide how much of that belongs in a launcher (convenience) versus a distro tool.
11. **Patched-with-an-older-delta detection.** The guard sees sizes only, so a file patched by a previous
    SPT version (same input, different output) looks like "neither build" and is re-patched from its
    backup — correct, but a *same-size* modification is indistinguishable. A hash of the expected output
    would close it; the delta carries none. Options: record the SHA-256 of our own output in `PatchInfo`
    (already done) and compare on the next run, or ship per-release hashes with the app.
12. **Byte-identity with the official launcher.** Needs a fresh install patched by the *current* official
    launcher; only an 8 KiB-older output exists locally. Cheap to settle whenever someone runs the
    official launcher once on a new 4.1.6 install.
13. **Pack UI wording for untrusted names.** The names in a package are unauthenticated, so the review
    screen must always show the GUID and the Forge-confirmed name next to them (the CLI already prints the
    GUID). Settle the layout in phase 12.
14. **Config files the installer calls non-config.** `IsConfig` still misses `.txt` (and the integrity check
    therefore does not list an edited `MOTD.txt` under `ModifiedConfigs`). The export no longer depends on
    the flag; decide in phase 12 whether integrity should use the same extension rule.
15. **The game root — decided 2026-11-04.** The instance root **is** SPT's merged root (`EscapeFromTarkov.exe`,
    `winhttp.dll`, `BepInEx/`, `SPT_Runtime/` together), which is what the official launcher's
    `GetGamePath()` and Doorstop both require; the split `gamefiles/`+`spt/` shape the installer produces is
    kept only as the untouched source used to create instances, and is never runnable. Existing instances are
    migrated automatically. See [phase 11](phase-11-launch-logs.md).
    *(Original text, kept for context:)* Official: one folder holds the game, BepInEx and
    `SPT_Runtime`, and `winhttp.dll` sits next to `EscapeFromTarkov.exe` (Doorstop needs that). Our
    instances keep `gamefiles/` and `spt/` apart, so neither is a valid working directory. Candidate
    answers: (a) build a per-instance *merged root* of hard links (cheap, uses the link layer we have, but
    new files written by BepInEx/mods land in the merged root, not in `spt/`); (b) an overlay mount (not
    portable, needs privileges); (c) copy `gamefiles/` into `spt/`. Needs a grilling session at the start of
    phase 11, and a look at what the user's own Lutris setup does with the two folders.
16. **Windows has never run this.** The native path is tested with a fake platform only. See
    [91-before-1.0.md](91-before-1.0.md) item 6.
17. **GameMode.** The `gamemoderun` wrapper and the daemon warning are tested with fakes; GameMode is not
    installed here.
18. **umu `GAMEID`.** Profiles use `umu-default`; check umu's database for a Tarkov entry and for what a
    store of `none` costs.
19. **Prefix size and cloning.** A prefix is 0.4–0.7 GB and lives in the instance by default. Options:
    shared prefix per profile (cheap, but mods/registry shared), per-instance copy of a golden prefix with
    reflinks where the file system supports them, or leave it. Also decide whether *clone instance* and
    *export* should skip it (the pack already ignores everything but config files).
20. **Output of Windows programs under Proton.** stdout does not reach the pipe (measured). Phase 11 must
    treat the game's log files as the primary game source and umu/Proton stderr as the "proton" source.


16. **Server mods that the Forge does not have.** `--server-sync --apply` installs the exact versions the
    server runs, but a mod that is not on the Forge (a private build, a hand-edited assembly) can only be
    reported, never fetched. Decide in phase 12 what the UI offers: a link to the author's page, a "copy from
    this install" flow, or nothing. Today the CLI names the GUID and the version it needs.
17. **The server's own profile backups.** 4.1.6 writes `user/profiles/backups/<date>/` on a schedule and
    keeps them; ours are per destructive operation. Decide whether the launcher should expose, prune or
    compress the server's archive (and whether that belongs with the log retention of [D17](04-decisions.md#d17-logs-rotate-compress-and-expire)),
    or leave it entirely to the server.
18. **Which `ip` a new instance gets.** The installer's `http.json` may say `0.0.0.0` (one base install here
    did), which makes the server reachable from the LAN. We warn, but do not change it: rewriting someone's
    config silently is worse. Decide in phase 12 whether the instance creation flow should default it to
    `127.0.0.1`.
19. **`SPT.Server` on Windows was not run.** The server path is platform-neutral (native program, job object),
    but only Linux was exercised; see [91-before-1.0](91-before-1.0.md), item 6.
20. **The game as a child of the launcher — decided (2026-11-04).** Both the game *and* the server are
    detached, adopted from their pid records on the next start, and never killed by the launcher exiting
    ([92-after-1.0](92-after-1.0.md#item-2-decided-2026-11-04--both-processes-are-detached)). The mechanism
    and the UI consequences are phase 11's design.

16. **`--server-stale` naming.** Phase 10 generalises it into `--stale [--reap]`, which lists both the server
    and the game of an instance (the pair lives in one record). The old flag can be kept as an alias for one
    release or dropped outright; decide when the CLI is documented for users (phase 14).
17. **Migration numbering.** The split→merged migration is "layout v2" of an instance. Whether the database
    stores a `LayoutVersion` column or the layout is inferred from the files on disk (an instance root with
    an `.exe` is merged) is a phase 11 implementation choice; inference means a half-migrated folder has to be
    detected by the plan, which the migration does anyway.
18. **Game logs above 64 MiB** stop following and point at the system editor (decided). Whether the same limit
    applies to *exporting* a selection from such a file is a small UX choice for phase 12.

21. **The pipe of a detached process.** Keeping stdout/stderr redirected is what makes "the server exited
    with code 0 because the port was busy" readable, but the pipe belongs to the launcher. Measured: the
    server survives `kill -9`, and then writes into a pipe nobody reads. Decide before 1.0 whether to keep
    the pipe (visibility, small risk) or redirect to a file the process owns (no risk, no live output).
22. **`systemd-run --scope` waits for the command.** The launcher's own process object is the wrapper, and
    the pid it reports is the wrapped program (measured), so stop and adoption work. Whether to switch to
    `--no-block` (true reparenting to the scope, no waiting) is worth a measurement on a slow machine before
    it is changed.

23. **Export and import now use the busy guard — done (11.3).** Both refuse while the instance has a running
    process, naming it; the pack registration test shows the dependency on the server layer.
24. **A busy port that belongs to our own untracked server.** Measured: deleting the run record leaves a
    detached server nobody can adopt, and the launch then says "port already in use". The message now suggests
    `--stale`; whether the launcher should also scan for a process whose executable is the instance's server
    (an adoption without a record) is open.

25. **The theme is applied to `Application.RequestedThemeVariant`, but the window itself has no per-variant
    resources beyond the Fluent defaults.** The rail and accent colours are the same in both variants so a
    high-contrast system theme keeps working; a designed light/dark pair would need two palettes and a check
    on a real screen. Decide before the visual pass (26).
26. **The already-running window is localised now.** It used to be English-only because it runs before the
    container exists; it now builds a `JsonLocalizer` from `--lang` and falls back to English, which is what
    that window always did.
27. **Mutation checking the UI layer — the harness is fixed, the anomaly is not.** `tools/mutation-check.py`
    runs one mutant per fresh `dotnet test` process and refuses to judge when the collected test count differs
    from the baseline, which turns the old false "SURVIVED" into UNJUDGED. The underlying oddity remains: with
    a mutant in `PageViewModel`, a run collects 1132 tests instead of 1157 (discovery still lists all of them,
    so the run stops early). It only happens with that class mutated and never in a normal run; the behaviour
    is covered by an ordinary test that was verified by hand. Do not read a UI mutation number without the
    count guard.
28. **No one has looked at the pixels** (still open; the Linux matrix rows 1–8 and 11 are recorded, the screen
    itself is not). Every screen is driven headlessly; a manual pass on Windows and
    Linux (light/dark, 1000-row list, a small window) is the phase-13 job.

29. **A perf assertion measured the wrong thing for a while.** `GC.GetAllocatedBytesForCurrentThread` sampled before and
    after an `await` reports another thread's cumulative total; the log-tail test therefore "measured" 3 MB or 53 MB
    depending on where the continuation landed, and failed one run in six. It now blocks on the task, measures
    process-wide, and runs in a non-parallel collection. Numbers in `docs/qa/perf.md` were taken after the fix.

30. **Licence — decided: LGPL-3.0-or-later.** `LICENSE` carries the GNU LGPL v3 text with an SPDX line, the README states
    it, and the AppStream metadata declares `LGPL-3.0-or-later` (validated with `appstreamcli`). The installer and the
    about box show the version only, which is all LGPL asks for.

## Open questions found while building the description parser and the server list (2026-11)

* **Q31 — how long the first dependents answer takes.** Resolved in part: the walk now starts when the Forge window
  opens (not on the click), is shared between pages, and is not restarted by navigating; measured **99 requests for
  2 083 mods** against the live API (42 catalogue pages + 57 dependency batches), producing 23 dependents for BigBrain. The catalogue walk is about 42 catalogue pages plus as many
  batched dependency questions (≈ 84 requests for 2 083 mods) and it is only cached for seven days. It runs
  behind a status line ("reading the catalogue… page N of M"), but nobody has timed it on a real machine yet, and
  whether a user will accept the wait once a week is a question, not a fact.
* **Q32 — the description parser is not CommonMark.** It covers headings, paragraphs, both list kinds, code, quotes,
  rules, images and inline emphasis and links; tables, footnotes and reference-style links are read as plain text. That
  is deliberate — mod descriptions are short — but it is a limitation to state, not a parser to grow.
* **Q33 — a server entry that is not local does nothing yet.** *Resolved: [D25](04-decisions.md#d25-a-server-that-is-not-this-machine) — the address travels as a launch argument, and the picker is the flow. The original text follows.* It is stored, tested and reported, but nothing points
  the game at it: the game's server address is still the instance's own. Deciding whether a remote entry should rewrite
  the game's configuration (and how that interacts with D6, the copy-on-write invariant) is still open.
