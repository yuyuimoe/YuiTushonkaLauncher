# Review — `docs/raw_plan.md`

Review of the SPT Manager plan against the current repository state and against what the SPT/Forge
ecosystem actually requires. Ordered by severity: blockers first, then gaps, then nits.

Legend: **[BLOCKER]** wrong as written / will produce broken design · **[VERIFY]** plausible but
unconfirmed assumption · **[GAP]** missing requirement · **[NIT]** cosmetic or naming.

---

## A. Blockers

### A1. [BLOCKER] Hard links vs. binary patching are mutually exclusive as designed

Phase 4 hard-links the Tarkov files into every instance; Phase 7 patches `Assembly-CSharp.dll` inside
the instance. A hard link is the *same* inode — patching it in one instance patches the original game
file, and every other instance, permanently.

`Assembly-CSharp.dll` (and anything else the patch touches) must be a **private copy per instance**,
never a link. The backup taken before patching must also live per instance, not next to the shared
original.

This needs to be stated as a rule in section 4 (`Instance`) and in Phase 4: a per-file link policy —
*"link these paths, copy these paths"* — with `Assembly-CSharp.dll` and patched outputs on the copy side.

### A2. [BLOCKER] Missing copy-on-write / "break link before modify" requirement

Same root cause as A1, but broader: if binaries are hard links, any in-place modification (mod update,
config rewrite, patch, user edit) corrupts every instance sharing that file.

The plan needs an explicit invariant — *never write through a link; unlink + copy, then write* — plus a
`FileLinkService` capability such as `EnsureDetached(path)` / `CopyOnWrite(path)` and a link-count check
(`st_nlink` / `GetFileInformationByHandle`). Without it, Phase 5 (`ModUpdateService`), Phase 8 (import)
and Phase 11 (launch) are all silently destructive.

### A3. [BLOCKER] `CreateInstance` says "downloads or locates the base files"

Tarkov cannot be downloaded by third-party tooling: the game files come from the official BSG launcher
under the user's own licence. The manager must **only detect and reuse an existing installation**
(registry/BSG launcher config on Windows, `~/.local/share`/Lutris/Steam prefix scan on Linux) and let
the user point at it.

Reword Phase 4 accordingly: `CreateInstance(sptVersion, tarkovPath, profile)` — never a download of game
files. SPT files themselves *can* be downloaded (`SPT-<version>.7z` from the SPT releases).

### A4. [BLOCKER] The patch/patch-file assumptions are unverified and load-bearing

Section 1, Phase 7 and the closing notes state as fact:

- patches live in `SPT_Data/Launcher/Patches/`,
- the engine class is `SPT.ByteBanger`,
- the target is always `Assembly-CSharp.dll`,
- `zlib.net` is needed for the decompression steps.

These come from a specific launcher version. SPT 4.x changed how the client is distributed and patched
(the launcher was reworked for the Tarkov 1.0 client). Phase 0 is supposed to *produce* this knowledge,
so Phase 7 must not be pre-specified against it. Mark Phase 7's concrete paths/classes as
"confirmed in Phase 0" and keep the abstraction (`IPatcher`, `PatchDescriptor`) free of the assumption.

Related: if SPT 4.x ships a pre-patched client or patches a different assembly, the entire "launcher
core" premise changes — this is the single riskiest assumption in the plan and deserves an explicit
go/no-go checkpoint at the end of Phase 0.

### A5. [BLOCKER] The `ModMetadata` GUID claim is probably wrong

The plan says the GUID is "the custom attribute on the `ModMetadata` class". In practice:

- **Client mods** are BepInEx plugins: the canonical id is the GUID in
  `[BepInPlugin("author.mod", "Name", "1.2.3")]`.
- **Server mods** are TypeScript/JS packages identified by `package.json` (`name`, `version`) and their
  `mod.json`/`BaseMod` metadata — there is no compiled DLL to reflect over.

So a single `ModMetadataReader` reading one custom attribute covers only part of the ecosystem, and
"GUID extracted from the DLL" is not a universal key. The design needs three readers behind
`IModIdentityReader` (BepInPlugin attribute, server-mod manifest, fallback/unknown) and a normalized
identity value object. Otherwise Phase 5 and the Phase 6 migration will fail on every server mod.

---

## B. Gaps

### B1. [GAP] Forge API authentication is missing

Section 2/Phase 1 only mention a descriptive `User-Agent`. The Forge API also requires an **API key**
(bearer token) for most endpoints, with per-user rate limiting. Needed: a token/credential store
(OS keyring or DPAPI/`libsecret` via a small abstraction), a settings UI to enter it, redaction in
Serilog output, and a clear "unauthenticated → degraded/read-only" mode. Note that `.env`/plaintext
storage of a key must be an explicit non-goal.

### B2. [GAP] Persistence decision is left open

"EF Core **or** Dapper" is not a decision. It changes the whole of Phase 2 (migrations, transactions,
change tracking, test strategy). Dapper has no migration runner — you would need
`Microsoft.Data.Sqlite` + a hand-rolled schema-version table. Pick one, and if EF Core, note the
`dotnet ef` tooling + design-time factory requirement.

### B3. [GAP] No SPT-version ↔ Tarkov-version compatibility matrix as a first-class concept

Each instance pins a pair (SPT version, Tarkov version). Upgrading SPT requires a matching Tarkov build;
mods declare SPT compatibility by SemVer. This matrix is what makes updates safe. It appears only
implicitly (`SptVersion`, `ModVersion.SptCompatibility`). It should be a named service
(`ICompatibilityService`) with explicit rules for "instance SPT upgrade" and "Tarkov build mismatch".

### B4. [GAP] Big file-system work has no pre-flight checks

Needed before any multi-gigabyte operation:

- free space estimate (instance size, downloads, temp extraction, backup),
- same-volume check for hard links (mentioned, but no API/behaviour defined),
- filesystem link support probe (FAT32/exFAT, and on Windows: no links on network shares),
- antivirus/file-lock retry policy on Windows (Tarkov files are often locked by a running BSG launcher).

### B5. [GAP] No download cache, no resumable/parallel transfers

Phase 3 caches *API responses*, but not mod archives. Instances share the same mod versions constantly;
without a content-addressed download cache (by SHA-256) plus resume support, every instance re-downloads.
Also missing: global concurrency limits, a lock per file/instance to prevent two concurrent installs
fighting over the same temp dir, and cancellation (`CancellationToken`) hygiene — the plan never mentions
cancellation or `IProgress<T>` outside the patch phase.

### B6. [GAP] Linux hard links need P/Invoke; the plan implies BCL support

.NET has `File.CreateSymbolicLink`, but **no** managed hard-link API. `IFileLinkService` needs
`link()`/`CreateHardLinkW` interop (or a `Mono.Unix`-style helper), with `errno` → domain error mapping
(`EXDEV`, `EPERM`, `EMLINK`, `ENOSPC`). Same for reading link counts. Worth naming explicitly so nobody
plans for a one-liner.

### B7. [GAP] BepInEx itself is not managed

Mods depend on a specific BepInEx version; SPT ships it. Nothing in the plan installs/updates/validates
BepInEx or `doorstop`, and nothing guards the "mod requires BepInEx ≥ x" case. Phase 5 or a dedicated
"runtime prerequisites" step should own it.

### B8. [GAP] `.sptpack` import is a security surface with no threat model

An imported `.sptpack` is untrusted data that writes files and triggers downloads. Needed: schema
validation, hard allowlist of paths (`configs/**` only, no traversal, no absolute paths, no symlinks in
the zip), size caps, and a review screen before anything is written. Phase 8 mentions zip-slip only for
mod extraction.

### B9. [GAP] No UI test strategy, no accessibility/localization

Phase 13 lists unit + integration + manual tests only. Avalonia 12 has `Avalonia.Headless` for
VM/UI tests — worth adopting early, since Phase 12 is entirely UI. Also missing: whether strings are
resource-based (i18n) from day one, and the app's UI language(s). Retrofitting i18n after Phase 12 is
expensive.

### B10. [GAP] Process management details

Phase 11: server must be stopped gracefully (the SPT server accepts a shutdown path; also
`SIGTERM`/`Console.CancelKeyPress`/`WM_CLOSE`), child processes must be reaped (Proton spawns a tree via
`wineserver`), and the app must not leak zombie servers when the UI closes or crashes. No log rotation
policy defined (unified logs + Proton verbose output grow fast).

### B11. [GAP] Phase ordering versus the stated priority

Section 6 says "prioritize Phases 3, 5, 7", but the roadmap runs 2 → 3 → 4 → 5 → 6 → 7 strictly. Either
say "Phase 7 is the go/no-go gate and is prototyped during Phase 0", or reorder so the binary patch
prototype comes right after Phase 1 — it is the risky, differentiating piece and the one most likely to
invalidate the plan.

---

## C. Repository alignment

- **No git repository yet** — this directory is not under version control. Phase 1 should include
  `git init`, a `.gitignore` for .NET (`bin/`, `obj/`, `*.user`), and `.editorconfig`.
- **No solution file**: currently a single project (`YuiTushonkaLauncher.csproj`, `net10.0`,
  Avalonia 12.1.5, `CommunityToolkit.Mvvm` 8.4.2). Phase 1's "create the solution" is a migration of the
  existing scaffold, not a greenfield creation.
- **Naming mismatch**: the plan names projects `SPTManager.*`, the app is `YuiTushonkaLauncher`. Pick
  one prefix (recommend `YuiTushonkaLauncher.*` to keep namespaces consistent with
  `namespace YuiTushonkaLauncher`) — and check trademark-ish concerns around "SPT Manager" as a product
  name.
- **`Models/` is an empty folder** referenced via `<Folder Include="Models\" />`; harmless but will
  disappear once Phase 2 lands real types.
- **Unused-`ViewLocator`** reflection template is already there (`RequiresUnreferencedCode`) — relevant
  for Phase 14 if trimming/AOT is attempted: the template is incompatible with trimming. If
  `PublishTrimmed`/AOT is a goal, Phase 14 needs `ViewLocator` replaced by explicit registrations.

---

## D. Nits

- **D1.** Phase 13's "coverage > 70%" is a poor acceptance criterion on its own — coverage of *domain
  rules* (dependency resolution, link policy, patch validation) matters, not a global percentage.
  Prefer "branch coverage of `Core` and the resolver/patch validators ≥ 80%, plus a fixed list of
  scenario tests".
- **D2.** "~300 req/min" for the Forge rate limit is unverified; get the real number from the API docs
  in Phase 0 rather than designing a cache around a guess.
- **D3.** `zlib.net`'s only NuGet versions are 1.0.3/1.0.4 (very old). Confirm the official launcher
  still needs it before taking a dependency, and check it restores/works on `net10.0`.
- **D4.** `SharpHDiffPatch.Core` exists and is actively released (latest observed: 2.4.2) — good choice.
  Note its API surface (`HDiffPatch.Patch`, `LoggerEvent`, `PatchEvent`) may differ per major version;
  pin the version and re-check the events in Phase 7.
- **D5.** "`.NET 10` is mandatory because of the SPT 4.1+ ecosystem" is not a real constraint — the
  launcher's TFM is independent of what the SPT server/game run on. Keep .NET 10 as a *choice* (it is
  the current LTS-adjacent release and Avalonia 12 targets it), not as a forced requirement. Same for
  "recommended by Avalonia 12" — verify what Avalonia 12 actually requires.
- **D6.** `Addon` vs `Mod` as two near-identical entities with duplicated services and tables invites
  drift. Consider one `Package` aggregate with a `Kind` discriminator and separate views.
- **D7.** Phase 10 (servers, `ServerProfile`) is heavily under-specified compared to its risk — "via API
  or a server file" is doing a lot of work. It also implies a server-side component that does not exist
  yet. Consider moving it after a working single-player flow (post-Phase 12) or scoping it to "import a
  server's exported `.sptpack`".
- **D8.** No non-functional targets anywhere: startup time, memory ceiling (mod lists can be thousands
  of entries), instance counts, DB size. Even rough numbers prevent bad schema/UI decisions.
- **D9.** Phase 14 mentions MSI/WiX, AppImage and `PublishSingleFile`, but not Avalonia's trimming/AOT
  caveats or Linux `.desktop` file + icon + MIME association for `.sptpack` (double-click import).

---

## E. Open questions to resolve before Phase 1

1. **Patch mechanism in SPT 4.x** — does the official launcher still hdiff-patch `Assembly-CSharp.dll`,
   or is the client shipped pre-patched / patched elsewhere? (Determines whether Phase 7 exists as
   described.)
2. **Mod identity** — confirm the authoritative id per mod kind (BepInPlugin GUID for client mods,
   `package.json` name for server mods) and whether the Forge API accepts both as lookup keys.
3. **Forge API auth + exact endpoints/rate limits** — confirm the listed endpoints exist in the current
   API version and what the auth model is (unauthenticated vs. API key vs. OAuth).
4. **Base game files** — confirm detection strategy per platform (BSG registry keys on Windows; Lutris /
   Steam / manual path on Linux) and how to validate "this is a Tarkov 1.x install".
5. **Project naming and repo layout** — `YuiTushonkaLauncher.*` multi-project vs. `SPTManager.*`.
6. **Persistence** — EF Core or Dapper + hand-rolled migrations.
7. **Scope of "replace the official launcher"** — does it include the account/login and game-file-integrity
   flows the official launcher performs, or only the offline patch + launch path?
