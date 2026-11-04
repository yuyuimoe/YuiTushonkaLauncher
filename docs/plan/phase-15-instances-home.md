# Phase 15 — Instances as the home, and a real adopt

## Why

Three problems found by running the launcher for the first time:

1. **Adopt asked for a destination.** Adopt is not "create a second copy from a base install"; it is "take the install
   that is already on this machine and bring it into the launcher". The instance is therefore created *in place*, and
   the install's pristine files are lifted into a base install that other instances can share.
2. **No version awareness.** Create and Adopt offered a folder and nothing else. Every SPT release requires one exact
   Tarkov build (`SPT_Data/configs/core.json → compatibleTarkovVersion`), and the PE file version of
   `EscapeFromTarkov.exe` says which build is actually there, so both can be read and shown, never guessed.
3. **The instance check could hang with no trail.** A real adopt left the status bar on "Checking the instance" with
   nothing in the log at Debug level.

## 15.1 Install detection

`YuiTushonkaLauncher.Core/Instances/InstallFingerprint.cs` plus `IInstallDetector` in the same file:

```csharp
public sealed record InstallFingerprint
{
    public required string RootPath { get; init; }
    public required InstallShape Shape { get; init; }          // split (installer output) or merged (runnable)
    public required InstanceLayout Layout { get; init; }
    public required string RuntimeDirectory { get; init; }
    public SemanticVersion? SptVersion { get; init; }           // SPT.Server.deps.json
    public string? RequiredTarkovVersion { get; init; }         // SPT_Data/configs/core.json
    public string? ActualTarkovVersion { get; init; }           // PE FileVersion of EscapeFromTarkov.exe
    public PatchInfo Patch { get; init; } = new();
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public bool IsRunnable { get; }                             // merged + exe + SPT runtime present
    public bool TarkovMatches { get; }                          // both known and equal
}
```

* `Infrastructure/Instances/InstallDetector.cs` implements it over `BaseInstallProbe` (existing) and `PeVersionReader`
  (existing), adding the `core.json` read. Detection never throws for a folder that merely is not an install: it fills
  `Warnings` and leaves versions null.
* The Create/Adopt window shows: **SPT version**, **required Tarkov version**, **detected Tarkov version** as a green
  match or a red mismatch, and the layout. A mismatch blocks creation with a sentence naming both builds.
* `SptRelease.TarkovVersion` is still what the Forge cache stores; detection is what the dialogs use.

## 15.2 Adopt

`YuiTushonkaLauncher.Core/Instances/IAdoptService.cs`:

```csharp
public sealed record AdoptRequest
{
    public required string InstallPath { get; init; }   // the playable install
    public required string BaseRoot { get; init; }      // where a new base install may be created
    public string? ReferenceBasePath { get; init; }     // an existing pristine base of the same Tarkov build
    public string Name { get; init; } = string.Empty;
}

public sealed record AdoptPlan
{
    public required InstallFingerprint Install { get; init; }
    public string? ReferencePath { get; init; }
    public int SharedFiles { get; init; }        // hash equals the reference: game or SPT files
    public int PatchedFiles { get; init; }       // instance copy matches a reference .spt-bak / delta output
    public int ModifiedFiles { get; init; }
    public int ExtraFiles { get; init; }         // not in the reference at all
    public long SharedBytes { get; init; }
    public long ModifiedBytes { get; init; }
    public IReadOnlyList<string> Modifications { get; init; }   // capped sample, relative paths
    public bool CreatesBase { get; init; }
    public string? NewBasePath { get; init; }
    public IReadOnlyList<string> Warnings { get; init; }
}
```

Classification, per file (size first, SHA-256 only when sizes agree):

| case | verdict |
| --- | --- |
| reference has the path, same size and same SHA-256 | **shared** (game or SPT file) |
| reference has the path, different hash, and the reference's `<file>.spt-bak` matches the instance copy | **patched** (the instance is the patched form) |
| reference has the path, different hash otherwise | **modified**, kept in the instance |
| reference does not have the path | **extra**, kept in the instance |

Writes, all through `ILinkAwareFileWriter` (D6) and the per-path link policy (D7):

* **No reference** (fresh machine, nothing to compare): the install becomes the base install as well — every file is
  hard-linked to `<BaseRoot>/base-<sptVersion>/`, and the instance keeps its own copies of anything the policy says
  must be copied (`Assembly-CSharp.dll`, `*.spt-bak`). Recorded as a warning, because nothing could be classified.
* **Reference given or auto-matched** by required Tarkov version: shared files become hard links between the base and
  the instance; the base is registered; modifications stay untouched in the instance. Nothing is ever deleted, and no
  file is rewritten in place.
* Hard links are skipped when the install and the base root are on different devices (the `EXDEV` path already exists
  in `FileLinkService`): the plan then says how much is copied.

The instance row is created with `Path = InstallPath` (in place), `BaseGamePath = base`, versions from the fingerprint
and the patch state from `PatchStateDetector`.

## 15.3 One window for the instance

Prism's shape, which is the reference the user gave:

* `MainWindow` shows **instances** only: a grid of cards, each with thumbnail-ish initial, name, "SPT x · EFT y",
  patched badge and last launch; a details pane on the right with Launch, Kill, Manage, Folder, Export, Copy, Delete.
* **Manage instance** opens `InstanceWindow` (`IDialogService.ManageInstanceAsync`), a second window whose rail is
  Mods / Addons / Server / Logs / Patch / Settings, with the launch panel and status bar. It is a child of the main
  window for modality purposes but not modal: the main window stays visible.
* `ShellViewModel` keeps the rail for the manager window only; the main window gets `HomeViewModel`. Navigation
  (`INavigationService`/`ShellNavigation`) keeps working inside the manager window, so the existing pages and their
  tests are reused unchanged.
* Actions that need an instance (Forge, Mods) are enabled only when one is active; the manager window is titled with
  the instance name and version.

## 15.4 Debug logging

`--verbose` (already parsed) raises the file sink to Debug. Add, at Debug level, one line per step that can block:
detection start/done with elapsed milliseconds and file counts, adopt plan, each classification roll-up, each write
batch, and every `IInstanceContext.RefreshAsync` with its source. `StatusBarViewModel` logs the transition it paints
(idle → checking → ok/failed with a reason) so a stuck "Checking the instance" always has a last line.

## Acceptance

* Adopt of a real merged install on this machine classifies its files against `base_tarkov41`, creates no new instance
  folder, links the shared files, keeps the mods, and registers the base.
* Create/Adopt show SPT + required Tarkov + detected Tarkov, and refuse a mismatched pair with both builds named.
* Main window lists instances; the manager window opens on Mods and the pages work as before.
* Every instance check logs a start and an end at Debug.
* Release build warning-free, CSharpier clean, tests green, coverage floor kept.

## What was built

* `InstallFingerprint` / `IInstallDetector` (`InstallDetector`), reading `SPT.Server.deps.json`,
  `SPT_Data/configs/core.json` and the executable's PE version. Detection never throws; a folder that is not an install
  comes back with warnings.
* `IAdoptService` / `AdoptService`: plan and adopt, in place, with SHA-256 classification against a pristine reference,
  patched forms recognised through `.spt-bak`, hard links into the new base install, and a no-reference fallback that
  treats the install as its own base and says so.
* Create/adopt window: detection row (SPT, required build, detected build, mismatch in red), adopt without a
  destination, reference and base-root pickers, plan summaries for both modes, and a refusal that names both builds.
* `HomeViewModel` + new `MainWindow` (instances as tiles, details pane, launch, manage) and `InstanceManagerWindow`
  (the old shell: Forge, Mods, Server, Logs, Settings), opened from the details pane. The manager is created on first
  use; its rail no longer carries an instances entry, because instances belong to the home window.
* `--verbose` copies debug lines to the console, and the file sink always keeps Debug: it previously applied a global
  `Information` minimum, so Debug never reached the log at all. Pre-flight now logs each step with elapsed milliseconds.
* Tests: `InstallDetectorTests` (7), `AdoptServiceTests` (7), `HomeTests` (5); the fake base install now writes a real
  PE version and a `core.json`, which is why one existing expectation moved from "unknown" to `0.16.9.40743`.

## Still open from this phase

* The home details pane does not show a thumbnail: an instance has no artwork of its own yet, so the card shows its
  initial. Deciding where instance art comes from belongs with the Forge thumbnails (phase 16).
* `--verbose` is read but the CLI has no other flag for the log level; a config knob can come with the settings pass.
