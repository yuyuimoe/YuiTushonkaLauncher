using System.Net.NetworkInformation;
using YuiTushonkaLauncher.Core.Server;

namespace YuiTushonkaLauncher.Infrastructure.Server;

/// <summary>
/// Reads the operating system's table of listening sockets. It never binds, so asking cannot disturb a
/// server that is starting at the same moment.
/// </summary>
public sealed class PortProbe : IPortProbe
{
    /// <inheritdoc />
    public bool IsFree(int port)
    {
        return !IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(listener => listener.Port == port);
    }
}
