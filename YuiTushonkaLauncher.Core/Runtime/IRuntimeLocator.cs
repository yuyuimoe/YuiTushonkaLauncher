namespace YuiTushonkaLauncher.Core.Runtime;

/// <summary>Finds Proton builds, <c>umu-run</c>, Wine and helper tools on this machine.</summary>
public interface IRuntimeLocator
{
    /// <summary>Searches the standard locations plus <paramref name="options" />. Never throws for a missing path.</summary>
    RuntimeInventory Locate(RuntimeSearchOptions? options = null);

    /// <summary>The <c>umu-run</c> to use: the user's pick, else <c>PATH</c>, else the usual install folders.</summary>
    string? FindUmuRun(RuntimeSearchOptions? options = null);
}

/// <summary>Resolves a program name against <c>PATH</c>; a seam so tests do not depend on the host.</summary>
public interface IExecutableFinder
{
    /// <summary>The full path of an executable file called <paramref name="name" />, or <c>null</c>.</summary>
    string? Find(string name);
}

/// <summary>Reports the names of running processes; a seam so GameMode detection is testable.</summary>
public interface IProcessNames
{
    IReadOnlyCollection<string> Running();
}

/// <summary>Tells which operating system rules apply; a seam so both platforms are testable.</summary>
public interface IPlatformInfo
{
    bool IsLinux { get; }

    bool IsWindows { get; }
}
