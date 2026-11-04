# Verified facts

Everything here was measured on this machine or fetched live on **2026-11-04**, not recalled. When a
phase needs a fact about SPT, it goes here first.

Local evidence:

- `BG = /home/yui/Games/spt40/drive_c/Program Files` (Lutris/Wine prefix).
- `$BG/base_tarkov` — SPT **4.0** layout. `$BG/base_tarkov41` — SPT **4.1** layout.
- Both contain `create-spt-instance.sh` (the user's existing hard-link instance script) and
  `gamefiles/` + `spt/`.
- Official launcher: cloned to `/tmp/sptlauncher` (`SP-Tushonka/launcher`, branch `4.1.x-dev`) and
  `/tmp/sptmm` (branch `mod-manager`). Re-clone with
  `git clone --depth 1 -b <branch> https://github.com/SP-Tushonka/launcher`.

## Organization, site and API base

- GitHub org is **`SP-Tushonka`**; `sp-tarkov` is legacy (pre-4.0). Branches that exist:
  `4.1.x-dev`, `5.0.x-dev`, `mod-manager`, `legacy/3.9|3.10|3.11|4.0`. Tags look like
  `5.0.0-BEM-20261010`.
- Site and API: **`https://sp-mod.com`**, API `https://sp-mod.com/api/v0`. `forge.sp-tarkov.com` is
  dead (NXDOMAIN). Assets (`thumbnail`, avatars) on `files.sp-mod.com` with hotlink protection
  (needs a `Referer: https://sp-mod.com/`).
- **No authentication.** `GET /api/v0/ping` → `{"success":true,"data":{"message":"pong"}}`, and
  `/mods`, `/addons`, `/spt/versions`, `/mod-categories` all answer 200 unauthenticated.
- **Rate limit: 300 per minute** — response headers `x-ratelimit-limit: 300`,
  `x-ratelimit-remaining: 299`. Successful GETs also carry `cache-control: max-age=900, public`.
- Envelope: `{ success, data, links, meta }`; Laravel pagination (`current_page`, `last_page`,
  `per_page`, `total`, `links[]`). Errors: `{ success:false, code, message }` with `VALIDATION_FAILED`
  (400) or `NOT_FOUND` (404).

### Endpoints confirmed live

| Endpoint | Notes |
|---|---|
| `GET /ping` | health |
| `GET /mods` | free text is `query=`; filtering is **`filter[...]`** (see below); `sort` (`name`,`downloads`,`favourites_count`,`featured`,`created_at`,`updated_at`,`published_at`, prefix `-` for DESC); `include=versions`; `page`, `per_page`. Returns `guid`, `hub_id`, `slug`, `fika_compatibility`, `category_id`, `owner`, `additional_authors`, and only the latest 6 versions when `include=versions`. |
| `GET /mod/{id}` | single mod including `description` (HTML). The plural `/mods/{id}` is a 404. |
| `GET /mod/{id}/versions` | **singular `mod`** — `/mods/{id}/versions` is a 404. Fields: `version`, `link` (download), `content_length`, `spt_version_constraint` (semver range, may come with stray spaces), `downloads`, `fika_compatibility`, `description` (HTML). Sometimes also `spt_versions` (concrete versions the API resolved). |
| `GET /mods/updates` | requires **both** `mods=` and `spt_version=`. `mods` format: `identifier:version,...` where identifier is a numeric mod id **or** a GUID: `?mods=827:1.3.1&spt_version=4.1.6`. Buckets and shapes (verified live): `updates[]` = `{current_version:{id,mod_id,guid,name,slug,version}, recommended_version:{id,version,link,content_length,fika_compatibility,spt_versions[]}}`; `blocked_updates[]` = same pair shape; `up_to_date[]` = `{id,mod_id,guid,name,version,spt_versions[]}`; `incompatible_with_spt[]` = `{id,mod_id,guid,name,version,reason,latest_compatible_version}` with `reason: "no_version_for_spt"`. |
| `GET /mods/dependencies` | same two params; returns a map `"<id>:<version>" → dependency list`. |
| `GET /addons` | fields include `mod_id`, `is_detached`, `detached_at`; paginated (139 pages at review time). |
| `GET /addon/{id}/versions` | fields: `version`, `link`, `content_length`, **`mod_version_constraint`** (not `spt_version_constraint`), `description`, `downloads`. |
| `GET /addons/dependencies` | analog of the mods one. |
| `GET /mod-categories` | `id`, `hub_id`, `title`, `slug`. |
| `GET /spt/versions` | `version`, `version_major/minor/patch`, `mod_count`, `link` (release URL), `color_class`. SPT 4.x links point at `github.com/SP-Tushonka/build`; 3.x at `github.com/sp-tarkov/build`. |

### Filtering is `filter[...]`, and the obvious names are silently ignored

Only `filter[...]` works. `?guids=`, `?ids=`, `?category_id=`, `?search=` are **ignored** (the API
returns unfiltered page 1, HTTP 200, no warning) — verified by comparing `meta.total` against the
expected subset. The authoritative filter list comes from the API's own error message:

```
filter: id, hub_id, name, slug, guid, teaser, featured, contains_ads, cheat_notice,
        category_id, category_slug, created_between, updated_between, published_between,
        spt_version, fika_compatibility, include_legacy
```

- `filter[guid]=a,b,c` and `filter[id]=1,2,3` accept **comma-separated batches** — this is how the
  migration feature resolves installed GUIDs in bulk.
- `filter[spt_version]=4.1.6` restricts to mods with a version compatible with that SPT version
  (699 results at review time).
- Brackets must be percent-encoded (`filter%5Bguid%5D`); the live server accepts the encoded form.
- Unknown filter names answer `400 INVALID_QUERY_PARAMETER` with the list above — a cheap way to
  discover the surface, and the reason our client never sends made-up parameters.
- `query=` is full-text over name/teaser/description; a GUID there yields fuzzy results, so exact
  matching must be done client-side (we verify every returned `guid`).

Notes:

- **Downloads are redirects to third parties.** `GET /mod/download/{id}/{slug}/{version}` answers
  `307` with `location` pointing at e.g. `dropbox.com/...&dl=1`. So: follow redirects, tolerate
  unknown/absent `Content-Length`, and treat `content_length` from the API as advisory.
- **The API exposes no content hash.** Integrity has to be computed locally (SHA-256) on first
  download and then reused.
- Mod ids and GUIDs are both valid lookup keys — GUIDs are what a local install can offer.

## Disk layout

### SPT 4.1 (`base_tarkov41`)

```
gamefiles/                       # the Tarkov install, produced by the SPT installer
  EscapeFromTarkov.exe, UnityPlayer.dll, "Register Game.exe"
  EscapeFromTarkov_Data/
    Managed/Assembly-CSharp.dll            # PATCHED in place
    Managed/Assembly-CSharp.dll.spt-bak    # original, kept by the installer/launcher
    Plugins/x86_64/hwecho.dll              # also a patch target
    {Resources,StreamingAssets,il2cpp_data}, boot.config, app.info
  MonoBleedingEdge/, NLog/, Install_EFT/BattlEye/
spt/
  winhttp.dll, doorstop_config.ini, .doorstop_version     # Unity Doorstop
  BepInEx/
    core/          # BepInEx 5 Mono: BepInEx.Preloader.dll, 0Harmony.dll, Mono.Cecil…
    plugins/spt/   # spt-core.dll, spt-common.dll, spt-custom.dll, spt-singleplayer.dll…
    config/, patchers/, cache/
  SPT_Runtime/
    SPT.Launcher.exe / SPT.Launcher.Linux   # the launcher being replaced
    SPT.Server[.exe|.Linux], SPT.Server.dll, SPTarkov.*.dll, *.deps.json, runtimeconfig.json
    SPT_Data/
      checks.dat                            # base64 JSON: [{Path, Hash}] manifest of SPT_Data
      configs/*.json (airdrop, bot, core, http, inraid, …)
      database/, images/, wwwroot/
      Launcher/{Locales/*.json, Patches/SPT-core/EscapeFromTarkov_Data/Managed/*.delta}
    user/{certs,logs,profiles,sptsettings,sptappdata,cache,mods,patchers,sptRegistry/registry.json}
```

### Version and patch facts read from the installs

- **SPT version**: `SPT.Server.deps.json` → `libraries` keys look like
  `SPT.Server/4.1.6-RELEASE+731d7a2.20260918`. First `SPT.Server/<x.y.z>` match wins; the 4.0 base
  reports `4.0.13`, the 4.1 base `4.1.6`.
- **Tarkov version**: the PE `FileVersion` resource of `EscapeFromTarkov.exe` says `0.16.9.40743`
  (ProductVersion `0.16.9.5-40743-f137e819`). `System.Diagnostics.FileVersionInfo` is Windows-only, so
  the launcher reads the resource directly (`PeVersionReader`).
- **Patch state is observable**: `base_tarkov41` has
  `gamefiles/EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll` (links=1, 16 225 280 B) next to
  `Assembly-CSharp.dll.spt-bak` (links=3, 15 994 432 B) → patched. `base_tarkov` (4.0) has **no**
  `.spt-bak`, so its state is `Unknown` by our rule. Note the base's `.spt-bak` is itself hard-linked
  (3 links) — one more reason an instance must copy the backup instead of linking it.
- **Layout** is detectable without SPT metadata: `spt/SPT_Runtime` → 4.1+, `spt/SPT` → 4.0.
- `.NET 10` is confirmed by `SPT.Server.runtimeconfig.json` (`tfm: net10.0`, frameworks 10.0.0).

### Creating an instance for real (2026-11-05)

`--create-instance smoke --from base_tarkov41 --into ~/Games/ytt-instances`:

```
Plan: 10203 linked (58785 MiB), 34 copied (32 MiB), 4 skipped, 8 directories
Created smoke at …/smoke in 00:00:00.36 (patch state stored: True)
```

- `UnityPlayer.dll` links=4 afterwards (shared), instance `Assembly-CSharp.dll` links=1 and the base's
  is still links=1 → the copy rule holds in practice.
- Per-instance `spt/SPT_Runtime/user/{cache,certs,logs,mods,profiles}` created; `BepInEx/plugins`
  contains only `spt/` (SPT's own plugins), user mods and profiles from the base are not copied.
- **Cross-volume refusal works**: `/tmp` is a different device than `$HOME` here, and the pre-flight
  aborted with the actionable message instead of a raw `EXDEV`.
- **`du` lies with hard links** (it reported 58 G for an instance that physically costs ~32 MiB): use
  link counts, not apparent size, to reason about disk usage. The base root also contained
  `create-spt-instance.sh`, which the "unknown top-level → copy" rule brought along; that is the
  intended safe default.

### SPT 4.0 (`base_tarkov`)

Same shape but the runtime dir is `spt/SPT/` instead of `spt/SPT_Runtime/`. The manager must detect
the layout instead of hard-coding one (`InstanceLayout`).

## Patching

`SPT.ByteBanger` **is gone**. The current launcher (`SPTushonka.Core/Patching/FilePatcher.cs`) does:

- patches are files named `*.delta`, discovered recursively under
  `<runtime>/SPT_Data/Launcher/Patches` (today only
  `SPT-core/EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll.delta`);
- the target path is derived from the delta's path relative to the patch root, with `.delta` stripped
  and joined onto the **game root**;
- before the first patch, the target is copied to `<file>.spt-bak` (only if the backup is absent);
  the patch is then applied **from the backup → target**, so re-patching is idempotent;
- the engine is `SharpHDiffPatch.Core`: `new HDiffPatch(); Initialize(delta); Patch(source, target, false)`;
- `Restore(root)` walks for `*.spt-bak`, deletes the patched file and restores the backup; on Linux it
  skips reparse points to avoid leaving the game tree;
- current code has `// TODO: We should do checksum validation at some point` — no hash verification.

Implication: an instance's patch state is observable from disk (`.spt-bak` presence + hashes of
`Assembly-CSharp.dll`), which is exactly what `PatchInfo` needs.

### Measured on the real 4.1.6 install (2026-11-04)

- The delta is `HDIFF13&zstd`, 4,459,070 bytes. Its header declares **input 15,994,432 → output
  16,233,472 bytes**; the vanilla `Assembly-CSharp.dll.spt-bak` is exactly 15,994,432. So the header
  alone identifies the pre-patch build, with no table of known hashes.
- Applying it takes **~0.3 s**. Applying the same delta twice gives the **same SHA-256** (deterministic).
- The patch **changes the type table** (15,137 → 15,088 types in 4.1.6). It does not "add SPT
  namespaces" and the vanilla assembly is not `GClass`-obfuscated — both were wrong guesses that the
  first version of the engine test asserted and the real data refuted.
- **The "patched" DLL the official installer left in `base_tarkov41` is 16,225,280 bytes — 8,192 bytes
  smaller than what today's delta produces.** It was patched (6 Aug) with an *earlier* delta set; the
  runtime was updated later (24 Oct). Metadata is identical to our output (14,366 distinct type shapes,
  90,657 methods, 0 differences), the bytes are not. Consequence: **sizes alone cannot tell "patched
  with this SPT version" from "patched with an older one"**, and byte equality against that file can
  never hold. The planner treats such a file as "neither build" and re-patches from the backup.
- A patched file with no `.spt-bak` can be recognised (size equals the delta's output) but not
  restored: the original has to be supplied again.
- Hard links: the base's `.spt-bak` has 3 links, the DLL 1. Patching writes to a temporary file and
  renames it over the target, so the base install's bytes and link counts are untouched (verified by
  hash before and after).

## Mods in SPT 4.x are C#

Client side (BepInEx 5, Mono):

- plugin DLLs under `BepInEx/plugins/**`; SPT's own plugins live in `BepInEx/plugins/spt/`;
- identity = the `[BepInPlugin(guid, name, version)]` attribute; e.g. `spt-core.dll` declares
  `com.SPT.core` (verified by string scan);
- `BepInEx/plugins/**` must be per-instance.

Server side (C# since 4.0):

- `SPTarkov.Server.Core.dll` defines `SPTarkov.Server.Core.Models.Spt.Mod.IModMetadata` (verified by
  decompiling the local 4.1 runtime with `ilspycmd`):

  ```csharp
  public interface IModMetadata {
      string ModGuid { get; init; }                    // reverse-DNS recommended
      string Name { get; init; }
      string Author { get; init; }
      List<string>? Contributors { get; init; }
      Version Version { get; init; }                   // semver 1.0.0 (not 1.0.0.0)
      Range SptVersion { get; init; }                  // semver range, e.g. ~4.0.0
      bool HasPrepatcher { get; init; }                // prepatch JSON at user/patchers/{ModGuid}
      List<string>? Incompatibilities { get; init; }
      Dictionary<string, Range>? ModDependencies { get; init; }
      string? Url { get; init; }
      string License { get; init; }
  }
  ```

- the loader materialises `SptMod { Directory, ModMetadata, Assemblies }` — i.e. a server mod is a
  **directory containing a C# assembly that implements `IModMetadata`**;
- load order: `[Injectable]` type priority first, `ModGuid` as tiebreaker;
- the server tracks `ActiveClientMods` / `LoadedMods`, an `ActiveModsFilename` persisted into the
  profile, and receives the client mod list from the launcher (`ReceiveClientMods`,
  `SendClientModsRequest`, `LauncherV2ModsResponse`).

DeepWiki's `sp-tarkov/server` wiki is **stale** (it describes the 3.x TypeScript server with
`package.json` mods); `SP-Tushonka/launcher` is not indexed there at all. Prefer the local runtime and
the cloned sources.

## Official launcher internals worth reusing

- `net10.0`, Avalonia `12.1.4`, `AvaloniaUseCompiledBindingsByDefault=true`, `.slnx`, CSharpier,
  `Build.props` with `SptVersion` + `LauncherRevision` + commit tag in `InformationalVersion`.
- Local server API it drives: `/launcher/v2/{ping,types,login,register,remove,version,mod-pages,profiles,profile,wipe}`
  plus `/singleplayer/bundles` and `/files/bundle/` for bundle sync.
- Paths: `user/{logs,profiles,Launcher/LauncherSettings.json,sptRegistry/registry.json,sptsettings,sptappdata,cache/bundles}`;
  SPT patches `<runtime>/SPT_Data/Launcher/Patches`; EFT settings at
  `%APPDATA%/Battlestate Games/Escape From Tarkov/Settings`; Proton lookup in
  `~/.steam/steam/compatibilitytools.d` and `/usr/share/steam/compatibilitytools.d`.
- `mod-manager` branch (from `Revert "Removes Forge API and Mod Manager"`) already contains a Forge
  client + mod manager we can learn from, not copy blindly: `Forge/RateLimiter.cs`
  (`System.Threading.RateLimiting`, sliding window 35/10 s + 160/60 s — conservative under the real
  300/min), `Forge/{ForgeModVersion,ForgeModUpdates,ForgeBase,…}`, `Mods/{ModManager,ModHelper,
  DownloadTask,InstallTask,UpdateTask}`, `SevenZip/{Windows,Linus}SevenZip`,
  `Update/{UpdateManifest,ManifestSignature,UpdateTransaction,UpdatePreflight,UpdateRecovery}` and
  `tools/LauncherManifest`.
- Their mod cache lives at `user/Launcher/ModCache/<mod guid>`; archive paths they must not treat as
  mod content: `BepInEx`, `BepInEx/plugins`, `SPT_Runtime`, `SPT_Runtime/user`,
  `SPT_Runtime/user/mods`.
- Locales: ~30 JSON files under `SPT_Data/Launcher/Locales/` (incl. `pt-BR.json`) — our i18n schema
  should look like theirs so translations can be shared.

## The user's existing instance script

`$BG/base_tarkov*/create-spt-instance.sh` is the behaviour to reproduce (and improve):

1. validate name `[A-Za-z0-9_-]+`, require `base_tarkov/{spt,gamefiles}` and an existing parent dir;
2. refuse cross-device targets via `stat -c %d` comparison;
3. `cp -al` each child of the base into the instance (recreate dirs, hard-link regular files);
4. **unshare `Assembly-CSharp.dll`**: `cp -p` to a temp file, then `mv -f` over it (new inode);
5. `trap cleanup_on_error ERR` → `rm -rf` the partial instance;
6. warn when no `Assembly-CSharp.dll` was found.

That is the acceptance behaviour for [phase 4](phase-04-instances.md), with two additions: copy-on-write
for everything else that gets written later, and an explicit per-path link policy instead of "link
everything then fix one file".
