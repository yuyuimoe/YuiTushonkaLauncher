namespace YuiTushonkaLauncher.Core.Patching;

/// <summary>
/// One <c>*.delta</c> from the SPT runtime, resolved against an instance.
/// </summary>
/// <remarks>
/// A delta is rooted at the <em>game</em> root and carries no paths of its own: its location decides
/// the target. The two sizes come from the delta header, which is what makes the pre-patch guard
/// possible without shipping a table of known game builds.
/// </remarks>
public sealed record DeltaDescriptor
{
    /// <summary>Instance-relative path of the file the delta rewrites (<c>EscapeFromTarkov_Data/…</c>).</summary>
    public required string RelativePath { get; init; }

    /// <summary>Absolute path of the <c>.delta</c> file.</summary>
    public required string DeltaPath { get; init; }

    /// <summary>Instance-relative path of the backup the launcher keeps next to the target.</summary>
    public required string BackupRelativePath { get; init; }

    /// <summary>Size of the file the delta expects to read, as declared in the delta header.</summary>
    public required long ExpectedInputSize { get; init; }

    /// <summary>Size of the file the delta produces, as declared in the delta header.</summary>
    public required long ExpectedOutputSize { get; init; }
}

/// <summary>Where an instance keeps the deltas that belong to its SPT version.</summary>
public static class DeltaSetLocation
{
    /// <summary>Suffix every delta file ends with.</summary>
    public const string DeltaExtension = ".delta";

    /// <summary>Suffix of the backup the launcher and the official launcher both use.</summary>
    public const string BackupSuffix = ".spt-bak";

    /// <summary>Instance-relative directory holding the deltas.</summary>
    public static string RelativePatchesDirectory(Domain.InstanceLayout layout)
    {
        return Instances.InstancePaths.PatchDirectory(layout);
    }

    /// <summary>Absolute directory holding the deltas of <paramref name="instanceRoot" />.</summary>
    public static string Resolve(string instanceRoot, Domain.InstanceLayout layout)
    {
        return Path.Combine(instanceRoot, RelativePatchesDirectory(layout));
    }
}
