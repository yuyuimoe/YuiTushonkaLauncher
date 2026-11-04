# Domain model

EF Core entities live in `YuiTushonkaLauncher.Core` (POCOs, no provider attributes); their
configuration lives in Infrastructure. Value objects are `record`s and compare by value.

## Entities

### `Instance`

| Member | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `Name` | `string` | unique, `[A-Za-z0-9_-]+`, used as folder name |
| `Path` | `string` | absolute instance root |
| `BaseGamePath` | `string` | the user-supplied Tarkov/SPT installer output this instance links from |
| `SptVersion` | `SptVersion` | owned value object |
| `TarkovVersion` | `string` | as reported by the installer/`EscapeFromTarkov.exe` |
| `Layout` | `InstanceLayout` | `Spt40` (`spt/SPT/...`) or `Spt41Plus` (`spt/SPT_Runtime/...`) |
| `RuntimeProfileId` | `Guid?` | Windows native, Proton, Wine runner config |
| `PatchState` | `PatchInfo` | owned |
| `ServerPort` | `int` | default 6969, per-instance |
| `CreatedAt` / `LastLaunchedAt` | `DateTimeOffset` | |

Invariants: `Path` must exist and be on the same volume as `BaseGamePath` for linking; an instance is
`Ready` only when `PatchState.IsPatched || PatchNotRequired` and the server binary is present.

### `Mod`

The Forge-side identity of an installed mod.

| Member | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `ForgeId` | `int?` | null = not (yet) matched to the Forge API |
| `Guid` | `string` | canonical key: BepInEx plugin GUID or `IModMetadata.ModGuid` |
| `Name`, `Author` | `string` | |
| `Kind` | `ModKind` | `Client` (BepInEx plugin) / `Server` (`IModMetadata`) / `Both` |
| `IsPinned` | `bool` | blocks automatic updates |
| `Recognition` | `ModRecognition` | `Recognized` / `Unrecognized` / `GuidMissing` |

### `ModVersion`

| Member | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `ModId` | `Guid` | FK |
| `Version` | `Version` | semver, from `IModMetadata.Version` or the DLL's `BepInPlugin` |
| `SptConstraint` | `Range` | semver range from `IModMetadata.SptVersion` or Forge |
| `DownloadUrl`, `ContentLength` | `string`, `long?` | Forge `link` + `content_length` |
| `Sha256` | `string?` | **we** compute it after download (the API has no hash field) |
| `IsInstalled`, `InstalledAt` | `bool`, `DateTimeOffset?` | |

### `Addon` / `AddonVersion`

Addons are attachments to a mod (translations, extra content) and the Forge API models them
separately: `GET /addons` with `mod_id`, and their versions carry `mod_version_constraint` instead of
`spt_version_constraint` — i.e. **an addon is pinned to a mod version, not to an SPT version**
(see [03-verified-facts](03-verified-facts.md#addons)). Therefore: separate tables, no shared
inheritance; both expose a common `IPackage` read model for the UI only.

### `SptVersion`

`Version`, `TarkovVersion`, `IsCurrent`, `ModCount`, `ReleaseUrl` (from `/spt/versions`). Also the
compatibility source for [the SPT↔Tarkov matrix](04-decisions.md#d12-spt-and-tarkov-compatibility-matrix).

### `Dependency`

`PackageId`, `DependencyGuid`, `Kind` (`Required` / `Optional` / `Incompatible`), `VersionRange`.
Modelled after `IModMetadata.ModDependencies` (guid → range) and `Incompatibilities`, plus the Forge
`/mods/dependencies` answer.

### `ModList` (shareable manifest)

`Id`, `Name`, `SptVersion`, `TarkovVersion`, `Entries` (`PackageEntry`: guid, name, exact version,
kind, pinned), `ManifestSha256` (the share code). This is both the export manifest and the local
snapshot used to detect drift.

### `PatchInfo`

`IsPatched`, `PatchSetVersion` (SPT version the deltas belong to), `OriginalSha256` (of the backed-up
`Assembly-CSharp.dll`), `PatchedSha256`, `BackupPath`, `AppliedAt`.

### `RuntimeProfile`

`Id`, `Name`, `Kind` (`Native`, `Proton`, `Wine`), `Executable` (umu, proton path, wine binary),
`PrefixPath`, `Environment` (dictionary), `ExtraArguments`, `IsDefault`.

### `ServerProfile`

`Id`, `InstanceId`, `Port`, `BackendUrl`, `SavedProfiles` (mirror of the SPT server's profile list),
`AutoSyncMods`.

### `Settings`

Single row (or key/value): instance root, download cache path, log retention, language, theme,
concurrent downloads, telemetry off.

## Value objects

| Type | Rules |
|---|---|
| `SptVersion` | semver `X.Y.Z`; ordering matters (4.1.10 > 4.1.9) |
| `Range` | semver constraint parser (`^4.1.0`, `>=4.0.0 <5.0.0`, SPT's own strings sometimes contain stray whitespace — trim) |
| `PackageId` | `(ModKind kind, string guid)` — the identity used everywhere |
| `FileHash` | `(string Sha256, long Length)` |
| `RelativePath` | validated: no `..`, no rooted paths, no reserved names — every extracted path goes through it |

## Invariants worth a test each

1. Linking never modifies the base install's inode, mtime or content.
2. Writing to a linked file detaches it first (inode changes, base file untouched).
3. `Assembly-CSharp.dll` is never shared, in either layout, before or after patching.
4. A patch is applied only when the target's pre-patch hash matches the delta set's expectation.
5. Every path extracted from a downloaded archive stays inside the destination directory.
6. Removing a mod that others depend on is refused unless it is explicitly forced.
7. A mod marked `Unrecognized` is never auto-updated and never blocks a launch.
