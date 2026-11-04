namespace YuiTushonkaLauncher.Core.Runtime;

/// <summary>A Proton build found on disk.</summary>
public sealed record ProtonBuild
{
    /// <summary>Directory name, e.g. <c>GE-Proton11-1</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The build's directory (the one holding the <c>proton</c> script).</summary>
    public required string Path { get; init; }

    /// <summary>The root it was found in, so the UI can group builds by origin.</summary>
    public required string Root { get; init; }

    /// <summary>The version label from the build's <c>version</c> file, when it has one.</summary>
    public string? Version { get; init; }

    /// <summary>Why the build cannot be used; <c>null</c> when it is valid.</summary>
    public string? Problem { get; init; }

    public bool IsValid
    {
        get { return Problem is null; }
    }
}

/// <summary>Whether Feral GameMode is available and running.</summary>
public sealed record GameModeStatus(bool Installed, bool DaemonRunning)
{
    /// <summary>GameMode was asked for, is installed, and nothing is serving it.</summary>
    public bool InstalledButNotRunning
    {
        get { return Installed && !DaemonRunning; }
    }
}

/// <summary>Everything the launcher found that can run a Windows program on this machine.</summary>
public sealed record RuntimeInventory
{
    /// <summary><c>false</c> on Windows: nothing is searched and the Linux settings are hidden.</summary>
    public required bool IsSupported { get; init; }

    public IReadOnlyList<ProtonBuild> ProtonBuilds { get; init; } = [];

    public string? UmuRun { get; init; }

    public string? Wine { get; init; }

    public string? Winetricks { get; init; }

    public GameModeStatus GameMode { get; init; } = new(false, false);

    /// <summary>Every root that was looked at, existing or not, for the "where did you look" hint.</summary>
    public IReadOnlyList<string> SearchedRoots { get; init; } = [];

    /// <summary>An inventory for a platform that needs no runtime layer.</summary>
    public static RuntimeInventory Unsupported { get; } = new() { IsSupported = false };
}

/// <summary>Where the locator may look beyond the standard places.</summary>
public sealed record RuntimeSearchOptions
{
    /// <summary>Extra directories holding Proton builds, added by the user.</summary>
    public IReadOnlyList<string> ExtraProtonRoots { get; init; } = [];

    /// <summary>A <c>umu-run</c> the user picked; used before looking on <c>PATH</c>.</summary>
    public string? UmuRunPath { get; init; }
}
