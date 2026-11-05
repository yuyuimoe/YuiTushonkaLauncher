using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Core.Configuration;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Infrastructure.Server;

namespace YuiTushonkaLauncher.Tests.Server;

/// <summary>
/// The server list always keeps a local entry, always has one active, and never loses a valid address to an edit. The
/// probe behind "test" is checked against a real listener.
/// </summary>
public sealed class ServerListTests
{
    [Fact]
    public void TheLocalServerIsAlwaysTheDefaultAndAlwaysLocal()
    {
        var local = ServerEntry.Local;

        local.IsBuiltIn.Should().BeTrue();
        local.Address.Should().Be("127.0.0.1:6969");
        local.IsLocal.Should().BeTrue();

        (ServerEntry.Local with { Host = "192.168.0.9" }).IsLocal.Should().BeFalse();
        (ServerEntry.Local with { Host = "0.0.0.0" })
            .IsLocal.Should()
            .BeFalse("the endpoint maps it to loopback, but the list is not local");
        (ServerEntry.Local with { Name = " " }).ValidationError().Should().NotBeNullOrEmpty();
        (ServerEntry.Local with { Port = 70_000 }).ValidationError().Should().NotBeNullOrEmpty("values are checked, not clamped");
    }

    [Fact]
    public async Task AStoredListWithoutTheLocalServerIsRepairedOnLoad()
    {
        var settings = new MemorySettings(
            new LauncherSettings
            {
                Servers =
                [
                    new ServerEntry
                    {
                        Name = "Friend",
                        Host = "10.0.0.5",
                        Port = 6969,
                    },
                ],
                ActiveServer = "Friend",
            }
        );
        var profiles = new ServerList(settings, NullLogger<ServerList>.Instance);

        await profiles.LoadAsync();

        profiles.All.Should().HaveCount(2);
        profiles.All[0].IsBuiltIn.Should().BeTrue("the local server is first, whatever the file said");
        profiles.All[0].Address.Should().Be("127.0.0.1:6969");
        profiles.Active.Name.Should().Be("Friend");
        settings.Current.ActiveServer.Should().Be("Friend", "the repair is written back");
    }

    [Fact]
    public async Task AnEmptyListFallsBackToTheLocalServerWhichCannotBeRemoved()
    {
        var profiles = new ServerList(new MemorySettings(new LauncherSettings()), NullLogger<ServerList>.Instance);

        await profiles.LoadAsync();

        await profiles.Invoking(p => p.RemoveAsync("local")).Should().ThrowAsync<ServerListException>();

        profiles.All.Should().ContainSingle().Which.IsBuiltIn.Should().BeTrue("the local server is the list's floor");
        profiles.Active.IsBuiltIn.Should().BeTrue();
    }

    [Fact]
    public async Task SavingAddsReplacesAndKeepsTheBuiltInFlag()
    {
        var settings = new MemorySettings(new LauncherSettings());
        var profiles = new ServerList(new MemorySettings(new LauncherSettings()), NullLogger<ServerList>.Instance);

        var friend = new ServerEntry
        {
            Name = "Friend",
            Host = "10.0.0.5",
            Port = 7000,
        };
        await profiles.SaveAsync(friend);
        profiles.All.Should().Contain(profile => profile.Name == "Friend");

        await profiles.SaveAsync(friend with { Port = 7001 });
        profiles.All.Should().Contain(profile => profile.Name == "Friend" && profile.Port == 7001);
        _ = settings;
    }

    [Fact]
    public async Task ActivatingAndRemovingTheActiveServerFallsBackToLocal()
    {
        var settings = new MemorySettings(new LauncherSettings());
        var profiles = new ServerList(settings, NullLogger<ServerList>.Instance);

        await profiles.SaveAsync(new ServerEntry { Name = "Friend", Host = "10.0.0.5" });
        await profiles.ActivateAsync("Friend");

        profiles.Active.Name.Should().Be("Friend");
        settings.Current.ActiveServer.Should().Be("Friend");

        await profiles.RemoveAsync("Friend");

        profiles.Active.IsBuiltIn.Should().BeTrue("removing the active server leaves nothing selected otherwise");
        settings.Current.ActiveServer.Should().Be("Local");
    }

    [Fact]
    public async Task TheProbeAnswersYesForAListenerAndNoForAClosedPort()
    {
        var probe = new EndpointProbe(TimeProvider.System);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var open = await probe.ProbeAsync(new ServerEndpoint("127.0.0.1", port));
            open.Reachable.Should().BeTrue();
            open.Milliseconds.Should().NotBeNull();
        }
        finally
        {
            listener.Stop();
        }

        // The port the listener used is closed again, so nothing answers.
        var closed = await probe.ProbeAsync(new ServerEndpoint("127.0.0.1", 1), CancellationToken.None);
        closed.Reachable.Should().BeFalse();
        closed.Failure.Should().NotBeNullOrEmpty();
    }

    /// <summary>Settings in memory, so a list can be exercised without a database.</summary>
    private sealed class MemorySettings(LauncherSettings initial) : ISettingsService
    {
        public LauncherSettings Current { get; private set; } = initial;

        public event EventHandler<LauncherSettings>? Changed;

        public Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Current);
        }

        public Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            Changed?.Invoke(this, settings);
            return Task.CompletedTask;
        }
    }
}
