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
| `GET /mods` | filters: `ids`, `hub_ids`, `guids` (case-insensitive), `category_id`, `category_slug`, `search`, `sort` (`name`,`downloads`,`favourites_count`,`featured`,`created_at`,`updated_at`,`published_at`, prefix `-` for DESC), `include=versions,license,category,source_code_links`, `per_page`, `page`. Returns `guid`, `hub_id`, `slug`, `fika_compatibility`, `category_id`, `owner`, `additional_authors`, and only the latest 6 versions when `include=versions`. |
| `GET /mod/{id}/versions` | **singular `mod`** — `/mods/{id}/versions` is a 404. Fields: `version`, `link` (download), `content_length`, `spt_version_constraint` (semver range, may come with stray spaces), `downloads`, `fika_compatibility`, `description` (HTML). |
| `GET /mods/updates` | requires **both** `mods=` and `spt_version=`. `mods` format: `identifier:version,...` where identifier is a numeric mod id **or** a GUID: `?mods=31:1.0.7,com.donut.scavcat:1.0.0&spt_version=4.1.6`. Response buckets: `updates`, `blocked_updates`, `up_to_date`, `incompatible_with_spt` (`reason: "no_version_for_spt"`, `latest_compatible_version`). |
| `GET /mods/dependencies` | same two params; returns a map `"<id>:<version>" → dependency list`. |
| `GET /addons` | fields include `mod_id`, `is_detached`, `detached_at`; paginated (139 pages at review time). |
| `GET /addon/{id}/versions` | fields: `version`, `link`, `content_length`, **`mod_version_constraint`** (not `spt_version_constraint`), `description`, `downloads`. |
| `GET /addons/dependencies` | analog of the mods one. |
| `GET /mod-categories` | `id`, `hub_id`, `title`, `slug`. |
| `GET /spt/versions` | `version`, `version_major/minor/patch`, `mod_count`, `link` (release URL), `color_class`. SPT 4.x links point at `github.com/SP-Tushonka/build`; 3.x at `github.com/sp-tarkov/build`. |

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
