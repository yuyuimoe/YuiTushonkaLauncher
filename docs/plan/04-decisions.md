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
- **added by us**: post-patch verification (size + SHA-256 recorded in `PatchInfo`), and a pre-patch
  guard that refuses to patch a file whose hash matches no known pre-patch variant;
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

