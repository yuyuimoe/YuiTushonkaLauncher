# Before 1.0

Everything that is **allowed to stay open while phases are built, but not to stay open at release**. It
is the short list; the reasoning for each item lives in [90-open-questions.md](90-open-questions.md) (item
numbers in brackets). Add to it whenever a phase leaves a deliberate gap, and tick an item only with
evidence.

| # | Point | Why it cannot ship open | Where |
|---|---|---|---|
| 1 | **Pack authenticity** — decide whether `.sptpack` is signed and/or embeds a hash of the producing install. | The share code proves integrity only, never who made a package. Changing it later changes the format (`formatVersion` 2), so decide before packages exist in the wild. Hold a grilling session first. | Q1, [D19](04-decisions.md#d19-a-sptpack-is-a-list-of-names-never-a-payload) |
| 2 | **Patch guard is size-only** — a same-size tampered `Assembly-CSharp.dll` passes. | Close it with an expected-output hash per SPT/Tarkov build (own output hash recorded in `PatchInfo` is already there), or ship per-release hashes. | Q11 |
| 3 | **Byte identity with the official launcher** is unverified. | Needs one fresh 4.1.6 install patched by the *current* official launcher to compare against. | Q12 |
| 4 | **Review screen shows untrusted names** — always show the GUID and the Forge-confirmed name beside any name from a package. | A package's names are unauthenticated. | Q13 (phase 12) |
| 5 | **`IsConfig` misses `.txt`** — integrity does not list an edited `MOTD.txt`. | Export no longer depends on the flag; integrity should use the same extension rule. | Q14 (phase 12) |
| 6 | **Windows is untested on real hardware.** The native path (no runtime layer, nothing Linux-specific invoked) is proven only with a fake platform; no Windows machine has run it. Hard links, `CreateHardLinkW`, job objects and the single-instance guard share this gap. | A Windows release needs one real run of each headless command on Windows. | phase 13 |
| 7 | **Stale build after `wt merge`** — the suite ran the previous phase's test count twice. Cause unproven (Rider open on the main worktree is the suspect). | A release pipeline must not trust a count it did not expect; CI should assert the test count floor. | process |
| 8 | **GameMode path is only tested with fakes** — GameMode is not installed on the development machine. | Run `--runtime-plan` with a profile that has GameMode on, on a machine that has it. | Q17 |
| 9 | **umu `GAMEID`** — profiles use `umu-default`, so umu applies no per-game fixes. | Check whether the umu database has an entry for Tarkov and use it if so. | Q18 |
| 10 | **Prefix size** — a per-instance prefix is 0.4–0.7 GB. | Decide whether the default stays per-instance, becomes shared, or is cloned copy-on-write. | Q19 |
| 11 | **Server mods that are not on the Forge** cannot be installed by `--server-sync --apply`; only named. | A user syncing to a server that runs a private mod needs a way to get it, and a release cannot answer "where is it?". | Q16 |
| 12 | **The `ip` in `http.json`** can be `0.0.0.0` (LAN-reachable). We warn but never change it. | Deciding the default for new instances is a release-time UX decision, not a bug. | Q18 |
