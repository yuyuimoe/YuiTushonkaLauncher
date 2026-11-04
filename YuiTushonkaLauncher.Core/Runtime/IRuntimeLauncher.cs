using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Core.Runtime;

/// <summary>What kind of program is being started, which decides whether a Windows runtime is needed.</summary>
public enum ProgramTarget
{
    /// <summary>A Windows executable (the game): on Linux it runs through Proton or Wine.</summary>
    WindowsProgram,

    /// <summary>A program that runs as-is on this platform (the Linux build of the SPT server).</summary>
    NativeProgram,
}

/// <summary>A helper program run inside the prefix.</summary>
public enum PrefixTool
{
    /// <summary><c>wineboot -u</c>: creates or updates the prefix.</summary>
    Initialize,

    WineConfig,

    RegistryEditor,

    /// <summary><c>winetricks</c>; the verbs go in <see cref="RuntimeToolRequest.Arguments" />.</summary>
    Winetricks,
}

/// <summary>A request to start one program through the instance's runtime.</summary>
public sealed record RuntimeLaunchRequest
{
    /// <summary>The program: an absolute path to the executable.</summary>
    public required string Program { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public required string WorkingDirectory { get; init; }

    public ProgramTarget Target { get; init; } = ProgramTarget.WindowsProgram;

    /// <summary>
    /// The program loads BepInEx through Unity Doorstop's <c>winhttp.dll</c>. Under Wine that only works
    /// with a <c>winhttp=n,b</c> DLL override, so pre-flight checks for it.
    /// </summary>
    public bool LoadsDoorstop { get; init; }
}

/// <summary>A request to run a prefix helper.</summary>
public sealed record RuntimeToolRequest
{
    public required PrefixTool Tool { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];
}

/// <summary>Turns "run this program for this instance" into a concrete, checked command.</summary>
public interface IRuntimeLauncher
{
    /// <summary>
    /// Plans the launch of a program. Nothing is started and nothing is written: the result carries every
    /// problem found, so a misconfiguration is reported before any process exists.
    /// </summary>
    /// <param name="instance">The instance whose folder is the default home of the prefix.</param>
    /// <param name="profile">The runtime to use; <c>null</c> is only valid where no runtime is needed.</param>
    /// <param name="request">The program to start.</param>
    /// <param name="search">Where to look beyond the standard places.</param>
    LaunchPlan Plan(Instance instance, RuntimeProfile? profile, RuntimeLaunchRequest request, RuntimeSearchOptions? search = null);

    /// <summary>Plans a prefix helper (<c>winecfg</c>, <c>winetricks</c>, …). Same guarantees as <see cref="Plan" />.</summary>
    LaunchPlan PlanTool(Instance instance, RuntimeProfile? profile, RuntimeToolRequest request, RuntimeSearchOptions? search = null);

    /// <summary>The prefix an instance uses with a profile: the profile's own, or one inside the instance.</summary>
    string ResolvePrefix(Instance instance, RuntimeProfile? profile);
}
