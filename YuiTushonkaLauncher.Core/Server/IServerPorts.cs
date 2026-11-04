namespace YuiTushonkaLauncher.Core.Server;

/// <summary>Asks the operating system which ports something is already listening on.</summary>
public interface IPortProbe
{
    /// <summary>True when nothing listens on <paramref name="port" /> on any address that would collide with a bind.</summary>
    bool IsFree(int port);
}

/// <summary>Reads and changes the port in an instance's <c>http.json</c>.</summary>
public interface IServerPortService
{
    /// <summary>The first free port at or after <paramref name="start" /> that no instance is configured to use.</summary>
    Task<int> SuggestFreePortAsync(int start = ServerEndpoint.DefaultPort, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the port the server of an instance listens on. Refuses a running server, a port outside 1024–65535
    /// and a port another instance is configured to use. Writes through the copy-on-write writer.
    /// </summary>
    Task SetPortAsync(Guid instanceId, int port, CancellationToken cancellationToken = default);
}

/// <summary>Which .NET shared frameworks are installed on this machine.</summary>
public interface IDotNetRuntimeProbe
{
    /// <summary>
    /// The installed frameworks (<c>dotnet --list-runtimes</c>), or <c>null</c> when <c>dotnet</c> cannot be asked,
    /// which does not mean the server cannot run: its launcher finds the runtime on its own.
    /// </summary>
    Task<IReadOnlyList<FrameworkRequirement>?> ListInstalledAsync(CancellationToken cancellationToken = default);
}

/// <summary>A port could not be changed, for a reason a person can act on.</summary>
public sealed class ServerPortException(string message) : Exception(message);
