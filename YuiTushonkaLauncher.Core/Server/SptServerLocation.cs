using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Core.Server;

/// <summary>A .NET shared framework the server needs, read from its <c>runtimeconfig.json</c>.</summary>
public sealed record FrameworkRequirement(string Name, string Version);

/// <summary>Where an instance's server is and how it is configured. Built from files only; nothing is started.</summary>
public sealed record SptServerLocation
{
    /// <summary>The server executable for this platform, or <c>null</c> when it is missing.</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>The server's working directory: the runtime root (<c>spt/SPT_Runtime</c> or <c>spt/SPT</c>).</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary><c>SPT_Data/configs/http.json</c>.</summary>
    public required string ConfigPath { get; init; }

    /// <summary><c>user/profiles</c>, where the server keeps one JSON file per profile.</summary>
    public required string ProfilesDirectory { get; init; }

    /// <summary>The address and port from <c>http.json</c>.</summary>
    public required ServerEndpoint Endpoint { get; init; }

    public IReadOnlyList<FrameworkRequirement> Requirements { get; init; } = [];

    public IReadOnlyList<ServerProblem> Problems { get; init; } = [];

    public bool IsComplete
    {
        get { return Problems.All(problem => !problem.IsError); }
    }
}

/// <summary>Finds an instance's server. Reads files, never starts a process.</summary>
public interface ISptServerLocator
{
    /// <summary>Locates the server of an instance. Every problem is reported on the result, none is thrown.</summary>
    SptServerLocation Locate(Instance instance);
}
