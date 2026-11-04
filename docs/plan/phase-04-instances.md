# Phase 4 — Instances, hard links and file safety

**Branch:** `feature/phase-04-instances` · **Depends on:** Phase 2.

## Goal

Create/clone/delete SPT instances that share one base install through hard links, with copy-on-write
guaranteed before anything is written.

## Tasks

- [ ] `IFileLinkService` + `FileLinkService`:
      `CreateHardLink` (`link()` / `CreateHardLinkW` P/Invoke), `CreateSymbolicLink`,
      `TryGetLinkCount`, `Detach`/`CopyOnWrite`, `CopyTree`, `IsSameVolume`.
      Map `EXDEV`/`EPERM`/`EMLINK`/`ENOSPC`/`ERROR_NOT_SAME_DEVICE` to domain errors.
- [ ] `ILinkAwareFileWriter`: the only writer allowed to touch instance files; detaches first when the
      file is shared. Everything else in the codebase must go through it.
- [ ] `InstanceLayoutDetector`: `spt/SPT` (4.0) vs `spt/SPT_Runtime` (4.1+); reject folders that are
      neither.
- [ ] `BaseInstallValidator`: base folder must contain `gamefiles/` + the SPT runtime, plus
      `EscapeFromTarkov.exe` and `BepInEx/core`. Report *which* piece is missing and what installer
      output is expected ([D4](../plan/04-decisions.md#d4-tarkov-files-are-user-supplied-spt-files-are-downloaded)).
- [ ] `InstanceService`:
      `Create(name, location, baseInstall, sptVersion)`, `Clone(source, name, location)`,
      `Delete(id, deleteFiles)`, `Rename`, `List`.
- [ ] `LinkPlanner`: implements the
      [link policy table](../plan/01-architecture.md#link-policy) — explicit allowlist of linked
      paths, everything else copied; `Assembly-CSharp.dll` always copied
      ([D5](../plan/04-decisions.md#d5-assembly-csharpdll-is-never-linked)).
- [ ] Pre-flight: same-volume check, free-space estimate, filesystem link probe, name validation
      (`^[A-Za-z0-9_-]+$`), destination-exists check, partial-instance cleanup on any failure
      (`try/finally` + rollback list, like the script's `trap`).
- [ ] Per-instance directories created empty: `BepInEx/plugins`, `BepInEx/config`,
      `SPT_Runtime/user/{logs,profiles,mods,cache,sptsettings,sptappdata,certs}`.
- [ ] `PatchStateDetector`: `.spt-bak` presence + hash comparison decides `Patched` /
      `NotPatched` / `Unknown`, written into `PatchInfo`.
- [ ] UI: instance list with state badges; create dialog with base-install picker and pre-flight
      report.

## Acceptance criteria

- Two instances created from `base_tarkov41` share inodes for game files (verify with `stat -c %i` /
  `fsutil hardlink list`) and **do not** share `Assembly-CSharp.dll`.
- Writing one byte through `ILinkAwareFileWriter` into a linked file leaves the base install's hash
  and inode untouched, and changes the instance file's inode.
- Cross-volume creation is refused with an actionable message, not an exception.
- A failure halfway through creation leaves no partial directory behind.
- Same behaviour on a Wine/Proton path and on a native Linux path.

## Notes

- The user's `create-spt-instance.sh` is the behavioural baseline: `cp -al` semantics, `stat -c %d`
  check, `Assembly-CSharp.dll` unshare via temp + `mv`, `trap` cleanup.
- Windows links on a network share fail; detect and refuse early instead of mid-copy.
- Free-space estimate must account for the copied set (`Assembly-CSharp.dll` is ~16 MiB, configs are
  small) *plus* the mod download cache the user may not have yet.
