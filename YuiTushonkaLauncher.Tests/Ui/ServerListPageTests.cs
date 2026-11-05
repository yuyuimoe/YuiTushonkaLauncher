using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Tests.Server;
using YuiTushonkaLauncher.ViewModels;

namespace YuiTushonkaLauncher.Tests.Ui;

/// <summary>
/// The server page keeps a list of servers, always with a local entry first, and only offers to start the one that runs
/// on this machine. Editing the local entry's port writes the instance's own configuration.
/// </summary>
[Collection(AvaloniaCollection.Name)]
public sealed class ServerListPageTests
{
    [Fact]
    public async Task ThePageOpensOnTheLocalServerAndCanStartIt()
    {
        var (world, _, _, _, page) = await WorldAsync();
        await using var _ = world;

        page.Servers.Should().ContainSingle();
        page.Servers[0].Address.Should().Be("127.0.0.1:6969");
        page.IsLocalActive.Should().BeTrue();
        page.CanStart.Should().BeTrue("the local server is the one the launcher runs");
        page.SelectedServer.Should().NotBeNull();
    }

    [Fact]
    public async Task ActivatingAServerElsewhereTakesTheStartButtonAway()
    {
        var (world, list, _, _, page) = await WorldAsync();
        await using var _ = world;

        page.AddServerCommand.Execute(null);
        page.EditName = "Friend";
        page.EditHost = "10.0.0.5";
        page.EditPort = "7000";
        await page.SaveServerCommand.ExecuteAsync(null);

        page.Servers.Should().HaveCount(2);
        await page.ActivateServerCommand.ExecuteAsync(null);

        list.Activations.Should().Contain("Friend");
        page.IsLocalActive.Should().BeFalse();
        page.CanStart.Should().BeFalse("nothing here can start a server on another machine");
        page.ActiveServerAddress.Should().Be("10.0.0.5:7000");
    }

    [Fact]
    public async Task EditingTheLocalPortWritesTheInstancesOwnConfiguration()
    {
        var (world, _, ports, _, page) = await WorldAsync();
        await using var _ = world;

        page.SelectedServer!.IsActive.Should().BeTrue();
        page.EditPort = "7070";
        await page.SaveServerCommand.ExecuteAsync(null);

        ports.Sets.Should().ContainSingle().Which.Port.Should().Be(7070);
        page.ServerStatusText.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task APortThatIsNotANumberIsRefusedWithoutTouchingAnything()
    {
        var (world, list, ports, _, page) = await WorldAsync();
        await using var _ = world;

        page.EditPort = "not a port";
        await page.SaveServerCommand.ExecuteAsync(null);

        list.Saved.Should().BeEmpty();
        ports.Sets.Should().BeEmpty();
        page.ServerStatusText.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task TheTestButtonReportsWhatTheProbeSaid()
    {
        var (world, _, _, probe, page) = await WorldAsync();
        await using var _ = world;

        await page.TestServerCommand.ExecuteAsync(null);

        probe.Asked.Should().ContainSingle().Which.Port.Should().Be(6969);
        page.ServerStatusText.Should().Contain("12");
    }

    /// <summary>A page over a server list, a probe and a port service that only record what happens.</summary>
    private static async Task<(
        UiWorld World,
        FakeServerList List,
        FakeServerPorts Ports,
        FakeEndpointProbe Probe,
        ServerPageViewModel Page
    )> WorldAsync()
    {
        var list = new FakeServerList();
        var ports = new FakeServerPorts();
        var probe = new FakeEndpointProbe(new EndpointProbeResult(true, 12, null));

        var world = await UiWorld.CreateAsync(configure: services =>
        {
            services.AddSingleton<IServerList>(list);
            services.AddSingleton<IServerPortService>(ports);
            services.AddSingleton<IEndpointProbe>(probe);
        });

        await world.SeedInstanceAsync("server-list");
        var page = world.Get<ServerPageViewModel>();
        await page.RefreshAsync(CancellationToken.None);
        return (world, list, ports, probe, page);
    }
}
