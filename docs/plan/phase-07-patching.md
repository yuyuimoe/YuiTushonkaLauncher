# Phase 7 — Binary patching

**Branch:** `feature/phase-07-patching` · **Depends on:** Phase 4.

## Goal

Apply the SPT binary delta to an instance, detect an already-patched client, and roll back safely —
reproducing the official launcher's behaviour with verification it does not have.

## Background (verified)

See [03-verified-facts](../plan/03-verified-facts.md#patching). In short: `*.delta` files under
`<runtime>/SPT_Data/Launcher/Patches`, applied to the game root with `SharpHDiffPatch.Core`, original
kept as `<file>.spt-bak`, patch applied `backup → target`, restore walks for `*.spt-bak`.
`SPT.ByteBanger` no longer exists. Today's delta set touches
`EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll` (and the current layout also ships
`EscapeFromTarkov_Data/Plugins/x86_64/hwecho.dll`).

## Tasks

- [ ] `YuiTushonkaLauncher.Patching`:
      - `DeltaSet` (discover `*.delta`, resolve target paths relative to game root, `.delta` stripped);
      - `IPatchEngine` + `SharpHDiffPatchEngine` (`Initialize` + `Patch`, `Verbosity.Quiet`,
        progress callbacks for the UI);
      - `PatchPlanner` (state machine: `NoBackup` → `Backup` → `Patch` → `Verify`, idempotent re-runs);
      - `Restore` (walk `*.spt-bak`, skip reparse points on Linux, delete + copy back).
- [ ] `PatchService` (Infrastructure) orchestrating: `PatchStateDetector` → backup → apply →
      verify (`PatchInfo.OriginalSha256`/`PatchedSha256`) → persist.
- [ ] Pre-patch guard: compute the target's current hash; accept only a known pre-patch variant
      (recorded in the delta descriptor the app ships or derives) — refuse with a clear message when
      the game build is unsupported, instead of producing a corrupted DLL.
- [ ] Post-patch verify: size + SHA-256 recorded; a mismatch rolls back automatically from `.spt-bak`
      and reports which file failed.
- [ ] `IsPatched` detection for existing installs (adoption path): `.spt-bak` present **and** target
      hash equals the expected patched hash → `Patched`; `.spt-bak` absent and target hash matches a
      known vanilla variant → `NotPatched`; otherwise `Unknown` with a warning.
- [ ] UI: patch tab showing state per file, "Patch now", "Restore original", progress with byte-level
      reporting, and the reason when a patch is refused.

## Acceptance criteria

- On a copy of `base_tarkov41/gamefiles` with the patch state reset (delete `.spt-bak`, restore the
  vanilla DLL), patching produces a byte-identical `Assembly-CSharp.dll` to the one the official
  installer/launcher produced (hash equality, asserted by a local manual test recorded in the PR).
- Running patch twice changes nothing the second time (idempotent).
- A deliberately corrupted target is refused *before* any backup is overwritten, and the instance
  still launches.
- Restore returns every patched file to its original hash.
- No write ever lands on a hard-linked file without detaching first
  ([D6](../plan/04-decisions.md#d6-copy-on-write-before-every-write)).

## Notes

- Backups are per instance and never shared — two instances must be able to hold different patch
  states ([D5](../plan/04-decisions.md#d5-assembly-csharpdll-is-never-linked)).
- The official launcher patches on every launch (`Restore` then `Run`); we prefer patch-once + verify,
  but must stay compatible with a base install that arrives already patched.
- Pin `SharpHDiffPatch.Core` and re-verify its API (`LoggerEvent`/`PatchEvent` differ across majors).
