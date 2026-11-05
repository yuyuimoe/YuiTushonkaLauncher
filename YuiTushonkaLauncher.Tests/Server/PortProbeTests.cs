using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Infrastructure.Server;

namespace YuiTushonkaLauncher.Tests.Server;

public sealed class PortProbeTests
{
    [Fact]
    public void IsFree_IsFalseWhileSomethingListens_AndTrueOnceItStops()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            new PortProbe().IsFree(port).Should().BeFalse();
        }
        finally
        {
            listener.Stop();
        }

        new PortProbe().IsFree(port).Should().BeTrue();
    }

    [Fact]
    public void IsFree_SeesAListenerOnAnyAddress()
    {
        var listener = new TcpListener(IPAddress.Any, 0);
        listener.Start();

        try
        {
            new PortProbe().IsFree(((IPEndPoint)listener.LocalEndpoint).Port).Should().BeFalse();
        }
        finally
        {
            listener.Stop();
        }
    }
}
