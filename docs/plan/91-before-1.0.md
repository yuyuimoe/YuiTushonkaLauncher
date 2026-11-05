# Before 1.0

The gate is deliberately short ([D23](04-decisions.md#d23-the-10-gate-is-three-items-and-the-releases-are-sequenced)).
This list was 13 items with no release date in sight; the session of 2026-11-05 cut it to the three things a
1.0 must honestly be able to claim. Everything else either has a decision now (and moved to
[04-decisions.md](04-decisions.md)) or waits for 1.1.

## The gate

| # | Point | What closes it |
|---|---|---|
| 1 | **Real Windows** — installer, create instance, start/stop server, install a mod, launch, logs, job object and `CREATE_BREAKAWAY_FROM_JOB`. | **Partially done 2026-11-05** on a Windows 10 VM with SPT 4.0.9: base inspect, instance creation (hard links proven with `fsutil`), patch plan/restore/patch, a Forge mod install, and server start/stop with its own log (`Server has started, happy playing`). Still open: the Inno Setup installer, a full `--launch` with the real game, and the breakaway-job survival test. Results in `docs/qa/manual-matrix.md`. |
| 2 | ~~**`.7z` and `.rar` mods install**~~ — **done 2026-11-05** ([D24](04-decisions.md#d24-archives-7z-and-rar-are-read-the-format-comes-from-the-bytes-packs-stay-zip)). | The installer reads the three formats by content; the real `DrakiaXYZ-BigBrain-1.5.0.7z` and `WTT-VoicePatcher-1.0.1.rar` were installed into an instance by `--install-mod`, inspected, extracted and identified. |
| 3 | ~~**Visual smoke**~~ — **done 2026-11-05** ([D29](04-decisions.md#d29-two-documents-that-make-gaps-visible-instead-of-remembered)). | `docs/qa/known-ugly.md` written from ten rendered screens plus a dark pass; one real defect found (a Settings label painted under its checkbox) and fixed. The 1000-row scale case lives in `ModListScaleTests`. |

## Evidence that is welcome but does not gate

- **Byte identity with the official launcher** (Q12): run the official launcher once on a clean 4.1.6 install
  and compare the produced `Assembly-CSharp.dll` with ours. Worth having; not a blocker.
- **GameMode with a real daemon** (Q17): run `--runtime-plan` on a machine that has GameMode.
- **`systemd-run --scope --no-block`** (Q22): measure on a slow machine before changing anything.

## Sequence

`v0.1.0` (the state that exists, which also exercises the release pipeline for the first time) → 7z +
remote servers ([D24](04-decisions.md#d24-archives-7z-and-rar-are-read-the-format-comes-from-the-bytes-packs-stay-zip),
[D25](04-decisions.md#d25-a-server-that-is-not-this-machine)) → `v0.2.0` → the gate above → `1.0.0`.

## Deliberately left for 1.1

- Signed `.sptpack` and shell integration/MIME ([D26](04-decisions.md#d26-sptpack-stays-integrity-only-for-10)).
- Built-in app updater; the app keeps GitHub Releases plus a manual check (D27).
- Designed light/dark palettes: the layout is temporary and will be redesigned (D28).
- Patch guard above size-only: the output hash of our own patching is recorded in `PatchInfo`, and shipping
  per-release hashes is a follow-up (Q11).
- Windows support for `--stale --scan` (D28).
