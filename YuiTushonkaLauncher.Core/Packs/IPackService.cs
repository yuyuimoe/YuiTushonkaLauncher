namespace YuiTushonkaLauncher.Core.Packs;

/// <summary>Choices for an export.</summary>
public sealed record ExportOptions
{
    /// <summary>Include the config files the user edited. Turn off to share the mod list only.</summary>
    public bool IncludeConfigs { get; init; } = true;

    /// <summary>Instance-relative paths to leave out even though they were edited (the export dialog's toggles).</summary>
    public IReadOnlyCollection<string> ExcludedConfigs { get; init; } = [];
}

/// <summary>A file the export left out, with the reason shown to the user.</summary>
public sealed record SkippedFile(string Path, string Reason);

/// <summary>What an export produced.</summary>
public sealed record ExportResult
{
    public required string PackagePath { get; init; }

    public required string ShareCode { get; init; }

    public required PackManifest Manifest { get; init; }

    public IReadOnlyList<SkippedFile> Skipped { get; init; } = [];

    /// <summary>Mods the receiver will not be able to download (they never came from the Forge).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>What an import would do with one package entry.</summary>
public enum ImportAction
{
    /// <summary>Download the exact version and install it.</summary>
    Install,

    /// <summary>A different version is installed; it is replaced only when the options allow it.</summary>
    Replace,

    /// <summary>The same version is already installed: nothing to do.</summary>
    AlreadyInstalled,

    /// <summary>Write the shared config file.</summary>
    ApplyConfig,

    /// <summary>The config already has the shared content.</summary>
    ConfigUpToDate,

    /// <summary>The config was edited locally; it is kept unless the options say otherwise.</summary>
    KeepLocalConfig,

    /// <summary>The entry cannot be fulfilled (not on the Forge, version gone, file not owned by the mod, …).</summary>
    Unavailable,
}

/// <summary>What kind of thing a plan line describes.</summary>
public enum ImportItemKind
{
    Mod,

    Addon,

    Config,
}

/// <summary>One line of the review screen.</summary>
public sealed record ImportPlanItem
{
    public required ImportItemKind Kind { get; init; }

    /// <summary>GUID for mods and addons, instance-relative path for configs.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? Version { get; init; }

    public required ImportAction Action { get; init; }

    /// <summary>Why this action was chosen, shown to the user as-is.</summary>
    public required string Reason { get; init; }

    /// <summary>Where the files would be downloaded from. Always taken from the Forge, never from the package.</summary>
    public string? DownloadUrl { get; init; }

    public int? ForgeId { get; init; }

    public string? ParentGuid { get; init; }

    /// <summary>For addons: the mod versions the Forge says the addon supports.</summary>
    public string? Constraint { get; init; }
}

/// <summary>Everything the user sees before any byte is written.</summary>
public sealed record ImportPlan
{
    public required PackManifest Manifest { get; init; }

    public IReadOnlyList<ImportPlanItem> Items { get; init; } = [];

    /// <summary>Problems that do not stop the import but deserve a look (other SPT version, other layout, …).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>True when at least one item would change the instance.</summary>
    public bool HasChanges
    {
        get { return Items.Any(item => item.Action is ImportAction.Install or ImportAction.Replace or ImportAction.ApplyConfig); }
    }

    public int UnavailableCount
    {
        get { return Items.Count(item => item.Action == ImportAction.Unavailable); }
    }
}

/// <summary>Choices for an import.</summary>
public sealed record ImportOptions
{
    /// <summary>Write the shared config files.</summary>
    public bool ApplyConfigs { get; init; } = true;

    /// <summary>Replace an installed mod whose version differs from the package's.</summary>
    public bool ReplaceDifferentVersions { get; init; }

    /// <summary>Overwrite a config the user edited locally. Off by default: local edits win.</summary>
    public bool OverwriteEditedConfigs { get; init; }

    /// <summary>
    /// Keep a download whose content differs from what the package was made from. Off by default: that is
    /// what a host swapping a file under the same version looks like.
    /// </summary>
    public bool AcceptChangedContent { get; init; }
}

/// <summary>How one item ended.</summary>
public enum ImportOutcome
{
    Installed,

    Replaced,

    ConfigApplied,

    Skipped,

    Failed,
}

/// <summary>What happened to one plan item.</summary>
public sealed record ImportItemResult
{
    public required ImportItemKind Kind { get; init; }

    public required string Id { get; init; }

    public required ImportOutcome Outcome { get; init; }

    public required string Message { get; init; }

    /// <summary>True when the result was checked against the hash the package declared.</summary>
    public bool Verified { get; init; }
}

/// <summary>Result of an import.</summary>
public sealed record ImportResult
{
    public IReadOnlyList<ImportItemResult> Items { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>True when nothing failed.</summary>
    public bool Succeeded
    {
        get { return Items.All(item => item.Outcome != ImportOutcome.Failed); }
    }

    public string Summary
    {
        get
        {
            var installed = Items.Count(item => item.Outcome is ImportOutcome.Installed or ImportOutcome.Replaced);
            var configs = Items.Count(item => item.Outcome == ImportOutcome.ConfigApplied);
            var skipped = Items.Count(item => item.Outcome == ImportOutcome.Skipped);
            var failed = Items.Count(item => item.Outcome == ImportOutcome.Failed);

            return $"{installed} installed, {configs} config(s) applied, {skipped} skipped, {failed} failed.";
        }
    }
}

/// <summary>Exports an instance's composition and rebuilds it from a package.</summary>
public interface IPackService
{
    /// <summary>Writes <paramref name="packagePath" /> from the instance's current state.</summary>
    Task<ExportResult> ExportAsync(
        Guid instanceId,
        string packagePath,
        ExportOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reads and validates a package and works out what importing it would do, without writing anything
    /// to the instance.
    /// </summary>
    /// <exception cref="PackException">The package is malformed or unsafe.</exception>
    Task<ImportPlan> PlanImportAsync(Guid instanceId, string packagePath, CancellationToken cancellationToken = default);

    /// <summary>Applies the package: installs the exact versions, then writes the configs, then verifies.</summary>
    /// <exception cref="PackException">The package is malformed or unsafe.</exception>
    Task<ImportResult> ImportAsync(
        Guid instanceId,
        string packagePath,
        ImportOptions? options = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default
    );
}
