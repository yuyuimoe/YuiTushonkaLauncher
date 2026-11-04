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

## Export / import on real data (2026-11-04)

Round trip with the two real mods installed **from the Forge** (`com.mpstark.dynamicmaps` 1.2.1, forge id
1431; `fika.ghostfenixx.svm` 2.2.3, forge id 236), one edited config in each, into a fresh instance:

- The installed versions (read from the DLLs) exist verbatim in the Forge's version list (`1.2.1`,
  `2.2.3`), so an exact-version lookup works; the manifest names `forgeId` + version and nothing else.
- Result: **identical share code on both sides** (`64086ba9…`), `diff -r` of `user/mods`, `BepInEx/plugins`
  and `EscapeFromTarkov_Data/Managed` shows **no difference**, both edited configs arrived, every mod and
  config was verified, and the imported instance is integrity-clean.
- The package for that setup is 3 files and about 1.8 KB, with no binary in it.
- **The real Forge publishes versions with build metadata** (`0.3.1+1` for Dynamic Maps, `1.8.0+h4` for
  SVM). `SemanticVersion` rejected them, and because the whole version list is mapped at once, one such
  version made the listing fail ("unparsable mod version"). Fixed in phase 8: build metadata is parsed and
  kept for display, and ignored by comparison and equality, as semver specifies.
- **`.txt` edits are real** (SVM's `Misc/MOTD.txt`), but the installer's `IsConfig` flag does not include
  `.txt`, so the first export silently skipped it. "Shareable" is now decided by extension, ownership by the
  tracked file list.
- Forge downloads end in the version (`/mod/download/1431/dynamic-maps/1.2.1`); using that last segment as
  a fallback *name* recorded `1.2.1` as a mod's name. The fallback is the GUID.
- Six hostile packages against the real CLI (zip-slip, a `.dll`, a symlink entry, a format-7 manifest, an
  edited manifest, an undeclared entry) were each refused with its specific error and exit code 1; the
  file the zip-slip entry aimed at was never created.

## Linux runtime, measured on this machine (2026-11-04)

- **What is installed:** `umu-run` 1.4.3, `wine`, `winetricks`; **no** `gamemode`. Twelve Proton builds across
  three roots (GE-Proton 10-20…11-1, UMU-Proton 9.0-3.2, proton-EM 10.0-32, proton-cachyos-slr, and Valve's
  Proton 8.0 / 9.0 (Beta) / Hotfix / Experimental under `steamapps/common`).
- **`~/.steam/steam` is a symlink to `~/.local/share/Steam`** and `~/.steam/steam/compatibilitytools.d`
  was **empty** while `~/.local/share/Steam/compatibilitytools.d` held the builds, so the official
  launcher's two roots (`~/.steam/steam/...` and `/usr/share/steam/...`) would have found only
  `proton-cachyos-slr`. The first locator listed every build twice (23 instead of 12): a link in a *parent*
  of the path is not seen by `ResolveLinkTarget` on the leaf. Real paths are now resolved component by
  component.
- **Official launcher rules** (`SPTushonka.Core/Helpers/LinuxHelper.cs`, `ConfigHelper.cs`): a Proton build
  is valid when its folder holds `compatibilitytool.vdf` and is not `LegacyRuntime`; the game is started
  as `umu-run <exe> <args>` with `WINEPREFIX` + `PROTONPATH`, working directory = the game path, default
  env `WINEDLLOVERRIDES="winhttp=n,b"`; launch settings use the Steam format (tokens starting with `-` or
  without `=` are arguments, the rest `NAME=value`); GameMode wraps the command in `gamemoderun` and is
  only offered when `gamemoded -version` runs. Game arguments:
  `-force-gfx-jobs native -token=<profile id> -config={'BackendUrl':'https://<ip>','Version':'live','MatchingVersion':'live'}`.
- **The official game path is the parent of `SPT_Runtime`**, i.e. a single root with the game *and*
  BepInEx. The base installs here split `gamefiles/` (exe, Unity) from `spt/` (`winhttp.dll`,
  `doorstop_config.ini`, BepInEx, `SPT_Runtime`), and neither folder alone can start a modded game.
  How a merged root is presented to Wine is **open** (Q15).
- **Fresh prefix through `umu-run` + GE-Proton11-1: 18.5 s** (first run also downloaded the steamrt4
  runtime), exit 0, `system.reg` + `user.reg` written, 699 MB. `umu` prints
  `Executable not found: wineboot` and still creates the prefix (Proton's setup does it); a second init is
  idempotent. Proton keeps `pfx/` inside `STEAM_COMPAT_DATA_PATH`; umu makes `pfx` a link to the prefix
  itself, so the same folder works with both.
- **Fresh prefix through plain Wine: ~9 s, 386 MB.** `wineboot -u` **exits before `system.reg`/`user.reg`
  exist**; they appear a few seconds later when the wine server goes idle. Judging the prefix at exit
  reported a healthy prefix as broken (fixed: bounded wait).
- **Direct `proton run`** (no umu, `STEAM_COMPAT_DATA_PATH` + `STEAM_COMPAT_CLIENT_INSTALL_PATH`) works: a
  fresh prefix in 2 s, 750 MB.
- **Console output of a Windows program does not reach the pipe.** Proton starts the exe through its own
  helper (`umu.exe`); `cmd.exe /c echo` printed nothing on stdout (also when run by hand). What arrives is
  umu's and Proton's own stderr. A program's effect was proven with a file written inside the prefix, in
  all three modes. This is why the game's own log (`BepInEx/LogOutput.log`) must be tailed as a source in
  phase 11.
- Failure cases against real data, each refused before anything started and with the exact path: a Proton
  build that does not exist, a non-empty folder as prefix (left untouched), a prefix under a read-only
  folder (the folder is named), a profile with no BepInEx override (warning).

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

## The SPT server, measured (2026-11-04)

Read from a real SPT 4.1.6 install (`base_tarkov41`) started as its own `SPT.Server.Linux`, plus the
4.0 install for the older layout. Nothing here is from documentation.

### Starting and stopping

- **Startup:** prints its banner, generates/loads `user/certs/server.crt`, starts the webserver and the
  websocket, loads server mods, then prints `Server has started, happy playing`. **Ready in 3.6–5 s** on this
  machine; `GET /launcher/v2/ping` answers `{"Response":"Pong!"}` (after zlib — the body is `78 da …`).
- **A second server on the same port does not fail loudly:** it prints
  `Failed to start the web server on 0.0.0.0:6969. Socket error: AddressAlreadyInUse (98).` and **exits
  with code 0**. Only the output says what happened.
- **`SIGTERM` is graceful:** it prints `Stopping server...` and exits 0 in about 100 ms. `SIGKILL` works
  too; there is no separate shutdown route in the launcher API.
- **The port really comes from `SPT_Data/configs/http.json`** (`ip`, `port`, `backendIp`, `backendPort`):
  changing `port` to 6970 and `ip` to `127.0.0.1` made it listen on exactly that, verified with `ss -ltnp`.
- **The .NET requirement comes from `SPT.Server.Linux.runtimeconfig.json`:** 4.1.6 needs
  `Microsoft.NETCore.App` + `Microsoft.AspNetCore.App` **10.0.0**; 4.0 needs **9.0.0**.
- **A Linux build has no extension:** the file is `SPT.Server.Linux`, so `Path.ChangeExtension(x, null)`
  yields `SPT.Server` and reads the *Windows* runtimeconfig.

### The launcher API

- **Transport:** HTTPS with a **self-signed certificate** the server creates on first start, and **both
  request and response bodies are zlib-compressed JSON**. Reads are `GET`, changes are `PUT`, no body-less
  change exists. Cookies are not used.
- **Routes that exist** (read from `SPTarkov.Server.Core.dll` and exercised with `curl`): `ping`, `version`,
  `types`, `profiles`, `profile`, `login`, `register`, `remove`, `wipe`, `mods`, `mod-pages`, and the older
  `/launcher/profiles` + `/launcher/profile/info`. Bundles: `/singleplayer/bundles`, `/files/bundle/`.
- **`/launcher/v2/mods` is the server mod list**, an object keyed by a short name:
  `{"SVM":{"ModGuid":"fika.ghostfenixx.svm","Name":"SVM","Author":"GhostFenixx","Version":"2.2.3","SptVersion":"~4.1.3",…}}`.
  It appeared only after a server mod was installed.
- **`mod-pages` was empty** on a stock install with one server mod, so it is not how client mods are
  reported.
- **Client mods live in the profile:** `user/profiles/<id>.json` has an `spt` object the game writes
  (`{"version":"4.1.6 (40743)","mods":[{"name","version","author","dateAdded","url"}]}`). `/profiles` returns
  it as `sptData.mods`, and a profile that was never played has an empty list.
- **The server validates almost nothing:** `register` accepted `"edition":"NoSuchEdition"` and
  `"username":""` (both created profiles), and `remove` with an empty name removed a profile. Duplicate
  names and unknown logins are refused (`"Response":false`). The launcher must validate.
- **`wipe` only flags:** the profile comes back with `"wipe":true` and the file keeps `"wipe": true` in
  `info`; the actual wipe happens when the game starts.
- **Profile shape** (fresh, 4.1.6): `info{id,scavId,aid,username,wipe,edition}`, `characters{pmc,scav}`,
  `inraid`, `insurance`, then `dialogues`, `spt` once played.
- **Editions offered by `types`:** Standard, Left Behind, Prepare To Escape, Edge Of Darkness, Unheard,
  SPT Developer, SPT Easy start, SPT Zero to hero.
- **The server backs profiles up itself** into `user/profiles/backups/<yyyy-MM-dd_HH-mm-ss>/` (profile files
  plus `activeMods.json`); its own log line is `Profile backup created in: …`.
- **`user/` is per instance** (created empty by the installer: `cache`, `certs`, `credentials`, `logs`,
  `mods`, `profiles`), while `SPT_Data/configs/http.json` is a **copy**, not a link, in an instance created
  by us — so editing the port per instance is safe, and still goes through the copy-on-write writer.

### No orphans on Linux

- `setpriv --pdeathsig TERM` makes the kernel signal the child when its **parent thread** dies, and
  `setpriv --dump` run *inside* the wrapper shows `Parent death signal: TERM`.
- The signal is tied to the **thread that created the child**, not the process: a child started from a
  thread-pool thread is killed when the pool retires that thread (measured: the child was gone after ~35 s
  with the launcher alive), while a child started from a thread that never ends survives until the process
  does.
- With that in place, `kill -9` of the launcher took the server with it within about a second (SIGTERM,
  graceful); without `setpriv`, the server survived and was found and stopped by `--server-stale --reap`
  from its pid record.

## The playable layout and its logs, measured (2026-11-04)

From real, played installs: `Program Files/tarkov_runs/spt41_live` (SPT 4.1), `tarkov_dev/spt41_dev` and
`tarkov_dev/spt40_dev` (4.0). These are the shapes a user actually runs.

### Merged, not split

- A runnable folder has **one root**: `EscapeFromTarkov.exe`, `EscapeFromTarkov_Data/`, `UnityPlayer.dll`,
  `winhttp.dll`, `doorstop_config.ini`, `BepInEx/`, `SPT_Runtime/` (4.1+) or `SPT/` (4.0), `Logs/`, plus
  whatever the user added (`Greed.exe`, `Name Plate.exe`, `SSPTMM/`, `dxvk.conf`).
- The official launcher confirms it in code: it runs from inside `SPT_Runtime` and
  `GetGamePath()` returns `Directory.GetParent(cwd)` unless the user set a path; `Path.Join(GetGamePath(),
  "EscapeFromTarkov.exe")` is the game, and `Paths.CoreDllPath` = `BepInEx/plugins/spt/spt-core.dll` relative
  to that same root.
- **What the installer produces is different**: `base_tarkov41/{gamefiles, spt}` — `winhttp.dll` under `spt/`,
  the game under `gamefiles/`. That is a *source of untouched files*, not something SPT can run. The
  installer's own `create-spt-instance.sh` keeps that split and hard-links it, so it does not solve the
  merge either.
- Link counts in a real merged instance: `EscapeFromTarkov.exe` 4, `winhttp.dll` 3, `SPT.Server.Linux` 3,
  **`Assembly-CSharp.dll` 1** with a `.spt-bak` beside it — exactly the isolation the patcher needs
  ([D5](04-decisions.md#d5-assembly-csharpdll-is-never-linked)).
- `SPT_Runtime/user/` in a played 4.1 instance: `cache`, `certs`, `credentials`, `Launcher`, `logs`, `mods`,
  `patchers`, `profileData`, `profiles`, `sptappdata`, `sptRegistry`, `sptSettings`.
- `SPT_Runtime/SPT_Data/Launcher/Patches/SPT-core` holds the delta set.

### Logs, all four of them

| Source | Path | Shape |
|---|---|---|
| Game (SPT) | `<root>/Logs/<date>_<version>/` | **one folder per run**; `application.log`, `assetBundle.log`, `_backend.log`, ` errors.log`, ` traces.log` |
| BepInEx | `<root>/BepInEx/LogOutput.log` | one file, rewritten per run; `redirect_output_log = false` in `doorstop_config.ini` |
| Server | `<root>/SPT_Runtime/user/logs/spt/spt<yyyymmdd>.log` | **daily**, appended |
| Launcher (the official one) | `<root>/SPT_Runtime/user/logs/Launcher.log` | what our own log replaces |

- **The game's logs are enormous**: one real session produced `_backend.log` 81 MB, ` errors.log` 81 MB,
  ` traces.log` 163 MB, `application.log` 30 KB, `assetBundle.log` 582 B. Any view must tail, not load.
- SPT **does not rotate** that `Logs/` folder: it deletes and regenerates it, which is why a run after a crash
  loses the evidence ([92-after-1.0](92-after-1.0.md)).

### Detaching a real server (2026-11-04)

- `systemd-run --user --scope --quiet true` exits 0 on this machine, so the strongest mechanism is available.
- `systemd-run --user --scope --quiet /path/SPT.Server.Linux` **keeps the pid of the program**: the launcher's
  recorded pid was the server itself, so stopping or adopting by pid works without hunting the wrapper.
- Measured on a real instance: with the launcher killed by `kill -9`, the server **stayed alive and kept
  answering `ping`**; the record held its pid, start time, executable, mechanism (`SystemdScope`) and port, and
  `--stale --reap` stopped it with SIGTERM (graceful, exit 0).
- A `setsid` child ends up in its own session (session id differs from the launcher's), which is what makes
  a plain-child death signal irrelevant.

### The launch pipeline on a real install (2026-11-04)

- `--launch v1 --profile yui` on a freshly created merged instance: `PreFlight`, `PatchCheck` (already
  patched), `ServerStart` (2.6 s), `Ready`, `GameStart`, `Detached`, total **2.7 s**, with the game receiving
  `-force-gfx-jobs native -token=<profileId> -config={'BackendUrl':'https://127.0.0.1:6969',…}`.
- **A scope keeps what a pid cannot**: the game substitution (`/bin/sh`) forked, so the recorded pid could
  die while the work lived on. With `--unit=ytt-<id>` the unit is the durable identity:
  `systemctl --user is-active ytt-<id>` said **active**, `list-units` showed it running, and
  `systemctl --user kill --signal=TERM` stopped the whole tree. Without the `.scope` suffix, `is-active`
  looks for a `.service` and reports a scope as inactive — measured.
- `--stale` then reported both members as `Running` with `[SystemdScope]`, and `--stale --reap` stopped
  both and the units disappeared.
- A detached server left without its record is invisible to the launcher: the next launch says the port is
  in use. Worth remembering before deleting anything in `<data>/run/`.

### Reading SPT's logs on a real install (2026-11-04)

- A real `Logs/` folder for one session measured **312 MiB**: `application.log` 30 KiB, `assetBundle.log` 582 B,
  `_backend.log` 78 MiB, ` errors.log` 78 MiB, `traces.log` 156 MiB (the earlier numbers were the uncompressed
  ones; the copies measured here are the same session).
- The launcher's own log lives in the application data folder (`<data>/logs/launcher-<date>.log`), separate from
  the instance.
- Retention measured with the real command: 26 plain files → 20 gzipped into `archive/`, 5 deleted by age, the
  active file untouched, and a second pass did nothing.
- A windowed tail of a file larger than the window drops the partial first line, so a cut word never appears as
  a log entry; the line numbers a view shows are positions within the tail, not file line numbers (counting from
  the start would mean reading the whole file).

### The UI layer, measured while building it (2026-11-04)

- A headless Avalonia application cannot be started twice in one process: `AppBuilder.SetupWithoutStarting`
  is called once behind a lock, and every UI test runs in one xunit collection with parallelisation off.
  Without that collection the test host died during the run (a hard crash, not a failed assertion).
- `Progress<T>` posts to the captured context, so a launch step can arrive *after* a later one. The session
  therefore keeps the furthest step and ignores anything behind it; the result of the pipeline, not a
  progress report, is what puts the button in `Running`.
- `IInstanceService.ListAsync` did not load installed mods, so a mods page built on it showed an empty list
  right after an install: `GetAsync` (mods + files) was added and the list method now includes the mods
  (enough for a card's count).
- A 1000-row list built from cached row objects filters in milliseconds; the row objects are reused, which is
  what keeps a keystroke from rebuilding the page.
- `dotnet test` with a filter that selects only headless tests produces no summary in this harness (the host
  ends without printing), so UI mutation runs use the full suite, one mutant at a time.
## SPT install self-description (measured on 4.1.6 and 4.0.13)

* `SPT_Runtime/SPT_Data/configs/core.json` → `compatibleTarkovVersion` is the exact game build the release demands
  (`0.16.9.40743` for 4.1.6, `0.16.9.40087` for 4.0.13). No external SPT-to-Tarkov table is needed: the install says it.
* `SPT.Server.deps.json` → `libraries` keys (`SPT.Server/4.1.6-RELEASE+…`) carry the SPT version.
* The game build itself is the PE `FileVersion` of `EscapeFromTarkov.exe`.
