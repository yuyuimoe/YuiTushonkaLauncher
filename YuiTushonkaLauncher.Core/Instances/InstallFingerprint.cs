using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Core.Instances;

/// <summary>
/// What a folder turned out to be: which SPT release it holds, which game build that release demands, which build is
/// actually there, and whether it is a shape the launcher can run.
/// </summary>
/// <remarks>
/// Detection never throws. A folder that is not an install at all produces an empty fingerprint whose
/// <see cref="Warnings" /> say why, because the dialog that shows this has to describe what it found, not fail.
/// </remarks>
public sealed record InstallFingerprint
{
    /// <summary>Absolute path that was inspected.</summary>
    public required string RootPath { get; init; }

    /// <summary>Split (the SPT installer's output) or merged (what SPT runs).</summary>
    public InstallShape? Shape { get; init; }

    /// <summary>Runtime layout, when the runtime directory was found.</summary>
    public InstanceLayout? Layout { get; init; }

    /// <summary>Runtime directory relative to <see cref="RootPath" /> (<c>spt/SPT_Runtime</c>, <c>SPT_Runtime</c>, …).</summary>
    public string? RuntimeDirectory { get; init; }

    /// <summary>SPT version read from <c>SPT.Server.deps.json</c>.</summary>
    public SemanticVersion? SptVersion { get; init; }

    /// <summary>
    /// Game build this SPT release requires, read from <c>SPT_Data/configs/core.json</c>
    /// (<c>compatibleTarkovVersion</c>). This is the SPT-to-Tarkov link, and it comes from the install itself.
    /// </summary>
    public string? RequiredTarkovVersion { get; init; }

    /// <summary>Game build found in <c>EscapeFromTarkov.exe</c>'s PE file version.</summary>
    public string? ActualTarkovVersion { get; init; }

    /// <summary>Patch state of the game binaries.</summary>
    public PatchStateInfo? Patch { get; init; }

    /// <summary>Non-fatal findings: what is missing, or what could not be read.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>True when the folder holds the game executable and the SPT runtime.</summary>
    public bool IsInstall
    {
        get { return Shape is not null && Layout is not null && ActualTarkovVersion is not null; }
    }

    /// <summary>True when the folder is merged and therefore runnable as it is.</summary>
    public bool IsRunnable
    {
        get { return Shape == InstallShape.Merged && IsInstall; }
    }

    /// <summary>
    /// True when the required and the detected game builds are both known and equal. A build may carry a suffix
    /// (the installer's <c>0.16.9.40743</c> against a PE version of <c>0.16.9.40743</c>), so the comparison is textual
    /// after trimming.
    /// </summary>
    public bool TarkovMatches
    {
        get
        {
            return !string.IsNullOrWhiteSpace(RequiredTarkovVersion)
                && !string.IsNullOrWhiteSpace(ActualTarkovVersion)
                && string.Equals(RequiredTarkovVersion.Trim(), ActualTarkovVersion.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>One line for a dialog or a log: versions, shape, patch state.</summary>
    public string Describe()
    {
        if (!IsInstall)
        {
            return $"'{RootPath}' is not an SPT install ({string.Join(" ", Warnings)})";
        }

        var patch = Patch?.State switch
        {
            PatchState.Patched => "patched",
            PatchState.NotPatched => "not patched",
            _ => "patch state unknown",
        };

        return $"SPT {SptVersion?.ToString() ?? "unknown"} (requires Tarkov {RequiredTarkovVersion ?? "unknown"}), "
            + $"found Tarkov {ActualTarkovVersion}, {Shape}, {patch}";
    }
}

/// <summary>Reads what is on disk into an <see cref="InstallFingerprint" />.</summary>
public interface IInstallDetector
{
    /// <summary>Inspects a folder. Never throws for a folder that is not an install; the warnings explain it.</summary>
    InstallFingerprint Detect(string path);

    /// <summary>Every folder under the given roots that looks like an install, matched by game build.</summary>
    IReadOnlyList<InstallFingerprint> DetectMany(IEnumerable<string> roots);
}
