using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Server;

namespace YuiTushonkaLauncher.Tests.Server;

public sealed class ServerEndpointTests
{
    [Theory]
    [InlineData("0.0.0.0", "127.0.0.1")]
    [InlineData("::", "127.0.0.1")]
    [InlineData("[::]", "127.0.0.1")]
    [InlineData("", "127.0.0.1")]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("192.168.1.20", "192.168.1.20")]
    [InlineData("::1", "[::1]")]
    [InlineData("tarkov.example.com", "tarkov.example.com")]
    public void ConnectHost_TurnsAWildcardBindAddressIntoSomethingYouCanConnectTo(string bind, string expected)
    {
        new ServerEndpoint(bind, 6969).ConnectHost.Should().Be(expected);
    }

    [Fact]
    public void BaseUri_IsAlwaysHttps()
    {
        new ServerEndpoint("0.0.0.0", 7001).BaseUri.Should().Be(new Uri("https://127.0.0.1:7001"));
    }

    [Theory]
    [InlineData("0.0.0.0", true)]
    [InlineData("::", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("192.168.1.20", false)]
    public void ListensOnAllInterfaces_IsTrueOnlyForWildcards(string bind, bool expected)
    {
        new ServerEndpoint(bind, 6969).ListensOnAllInterfaces.Should().Be(expected);
    }

    [Theory]
    [InlineData("host.example:7000", "host.example", 7000)]
    [InlineData("https://host.example:7000", "host.example", 7000)]
    [InlineData("https://host.example:7000/", "host.example", 7000)]
    [InlineData("host.example", "host.example", 6969)]
    [InlineData("https://host.example", "host.example", 6969)]
    [InlineData("127.0.0.1:6970", "127.0.0.1", 6970)]
    [InlineData("[::1]:7000", "::1", 7000)]
    [InlineData("  host.example:7000  ", "host.example", 7000)]
    public void TryParse_ReadsHostAndPort_AndDefaultsToTheSptPort(string text, string host, int port)
    {
        ServerEndpoint.TryParse(text, out var endpoint).Should().BeTrue();

        endpoint.Host.Should().Be(host);
        endpoint.Port.Should().Be(port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://host.example:7000")]
    [InlineData("host.example:0")]
    [InlineData("host.example:70000")]
    [InlineData("host.example:abc")]
    public void TryParse_RefusesWhatIsNotAnHttpsServerAddress(string text)
    {
        ServerEndpoint.TryParse(text, out _).Should().BeFalse();
    }
}
