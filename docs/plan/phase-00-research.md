# Phase 0 — Research and evidence

**Status:** ☑ done — output is [03-verified-facts](../plan/03-verified-facts.md).

**Goal:** replace every assumption in the raw plan with a measured fact, and identify which assumptions
the product depends on.

## What was done

- [x] Cloned the official launcher (`SP-Tushonka/launcher`, `4.1.x-dev` and `mod-manager`) and read
      the patch engine, path constants and startup flow.
- [x] Decompiled the local SPT 4.1 server runtime (`ilspycmd`) to extract `IModMetadata`, `SptMod`,
      `PathConfig` and the mod/active-mod plumbing.
- [x] Verified the Forge API live at `https://sp-mod.com/api/v0`: envelope, pagination, rate-limit
      headers, the working endpoint set, the *effective* `mods=` parameter format, and the
      addon-version constraint field.
- [x] Established the on-disk layout of SPT 4.0 (`spt/SPT`) vs 4.1 (`spt/SPT_Runtime`), and the mod
      roots (`BepInEx/plugins` for client, `SPT_Runtime/user/mods` for server).
- [x] Read the user's existing `create-spt-instance.sh` and recorded its behaviour as the phase-4
      acceptance baseline.
- [x] Confirmed patching still uses `SharpHDiffPatch.Core`, with `*.delta` files and `.spt-bak`
      backups; `SPT.ByteBanger` is gone.
- [x] Confirmed the API needs **no** authentication, and that downloads redirect to third parties.

## Still open from research (moved, not forgotten)

- Bundle sync semantics (`/singleplayer/bundles`, `/files/bundle/`) — see
  [90-open-questions](../plan/90-open-questions.md).
- Where the 4.x server reads its listen port from (`SPT_Data/configs/http.json` is the candidate).
- Which archive formats actually appear in the catalogue (zip vs 7z vs rar).

**Acceptance criteria (met):** `03-verified-facts.md` exists and every claim in it has a source (path,
command or URL). No phase below depends on an unverified statement about SPT.
