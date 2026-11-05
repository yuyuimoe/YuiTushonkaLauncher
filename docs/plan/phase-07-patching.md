# Phase 7 — Binary patching

**Branch:** `feature/phase-07-patching` · **Depends on:** Phase 4.

**Status:** ☑ done (UI tab moved to Phase 12). Delivered: `DeltaSetReader` (nested `SPT-core/…` trees,
4.0 and 4.1+ layouts, unreadable deltas skipped with a warning), `IPatchEngine` +
`SharpHDiffPatchEngine` (serialised, per-call event subscription, progress, cancellation),
`PatchPlanner` (one explicit decision per file), `BackupScanner` (skips reparse points),
`AtomicWrite`/`FileHash`, `PatchingService` (`Plan`/`Patch`/`Restore`, persists `PatchInfo`) and
`--patch-plan`/`--patch`/`--restore`. 375 tests; Core 95.7 %, Infrastructure 92.7 %, Patching 97.2 %.
Verified on the real 4.1.6 install: patch in 0.3 s, idempotent, deterministic, restorable, a truncated
target refused with the base untouched, a corrupted target with a backup repaired.

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

- [x] `YuiTushonkaLauncher.Patching`:
      - `DeltaSet` (discover `*.delta`, resolve target paths relative to game root, `.delta` stripped);
      - `IPatchEngine` + `SharpHDiffPatchEngine` (`Initialize` + `Patch`, `Verbosity.Quiet`,
        progress callbacks for the UI);
      - `PatchPlanner` (state machine: `NoBackup` → `Backup` → `Patch` → `Verify`, idempotent re-runs);
      - `Restore` (walk `*.spt-bak`, skip reparse points on Linux, delete + copy back).
- [x] `PatchingService` (Infrastructure) orchestrating: `PatchStateDetector` → backup → apply →
      verify (`PatchInfo.OriginalSha256`/`PatchedSha256`) → persist.
- [x] Pre-patch guard: compute the target's current hash; accept only a known pre-patch variant
      (recorded in the delta descriptor the app ships or derives) — refuse with a clear message when
      the game build is unsupported, instead of producing a corrupted DLL.
- [x] Post-patch verify: size + SHA-256 recorded; a mismatch rolls back automatically from `.spt-bak`
      and reports which file failed.
- [x] `IsPatched` detection for existing installs (adoption path): `.spt-bak` present **and** target
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

## Deviations

- **The guard compares sizes from the delta header, not hashes.** The plan asked for "a known pre-patch
  variant recorded in a descriptor"; the header already states the input and output size, so there is
  nothing to ship. The price: a same-size modified file passes the guard (see open question 11).
- **The result is verified by size, not hash.** There is no reference hash for the output (the delta
  carries none), so the recorded SHA-256 is evidence, not proof. A mismatch against the declared size
  discards the temporary file before anything is replaced; the original is never touched, so there is
  nothing to "roll back".
- **Byte identity with the official launcher's output is not asserted.** The only official patched DLL
  available was produced by an older delta set (8 KiB smaller). The test asserts what can hold:
  declared size, a loadable `Assembly-CSharp` with a different type table, determinism, and an exact
  round trip on a real HDiffPatch fixture.
- **"Backup first" is "patch from the pre-patch file".** When the target is neither build but the backup
  is the pre-patch build, the instance is repaired from the backup instead of being refused.
- UI tab deferred to phase 12, as in phases 4–6.

## Notes

- Backups are per instance and never shared — two instances must be able to hold different patch
  states ([D5](../plan/04-decisions.md#d5-assembly-csharpdll-is-never-linked)).
- The official launcher patches on every launch (`Restore` then `Run`); we prefer patch-once + verify,
  but must stay compatible with a base install that arrives already patched.
- Pin `SharpHDiffPatch.Core` and re-verify its API (`LoggerEvent`/`PatchEvent` differ across majors).
