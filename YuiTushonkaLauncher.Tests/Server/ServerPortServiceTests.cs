using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Infrastructure.FileSystem;
using YuiTushonkaLauncher.Infrastructure.Server;
using YuiTushonkaLauncher.Tests.Infrastructure;
using YuiTushonkaLauncher.Tests.Runtime;

namespace YuiTushonkaLauncher.Tests.Server;

public sealed class ServerPortServiceTests : IAsyncLifetime
{
    private readonly SupervisorHarness _h = new();

    private readonly TestDatabase _database = new();

    private readonly FileLinkService _links = new(NullLogger<FileLinkService>.Instance);

    private Instance _one = null!;

    private ServerPortService _service = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _one = _h.AddInstance("one", port: 6969, ip: "0.0.0.0");
        await Persist(_one);

        _service = new ServerPortService(
            _h.Instances,
            new SptServerLocator(new FakePlatform(linux: true)),
            _h.Ports,
            _h.Supervisor,
            new LinkAwareFileWriter(_links),
            _database.UnitOfWork,
            _h.Time
        );
    }

    public async Task DisposeAsync()
    {
        _h.Dispose();
        await _database.DisposeAsync();
    }

    private async Task Persist(Instance instance)
    {
        await _database.UnitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                context.Instances.Add(
                    new Instance
                    {
                        Id = instance.Id,
                        Name = instance.Name,
                        Path = instance.Path,
                        BaseGamePath = "/base",
                        SptVersion = SemanticVersion.Parse("4.1.6"),
                        TarkovVersion = "1.0.0.0",
                        Layout = instance.Layout,
                        ServerPort = instance.ServerPort,
                        CreatedAt = DateTimeOffset.UtcNow,
                        ServerProfile = new ServerProfile
                        {
                            Id = Guid.NewGuid(),
                            Port = instance.ServerPort,
                            BackendUrl = $"https://127.0.0.1:{instance.ServerPort}",
                            UpdatedAt = DateTimeOffset.UtcNow,
                        },
                    }
                );
                await Task.CompletedTask;
            }
        );
    }

    // ---- choosing a port ----

    [Fact]
    public async Task Suggest_SkipsPortsOtherInstancesAreConfiguredFor_AndPortsThatAreBusy()
    {
        _h.AddInstance("two", port: 6970);
        _h.Ports.Busy.Add(6971);

        (await _service.SuggestFreePortAsync(6969)).Should().Be(6972);
    }

    [Fact]
    public void FindFree_ThrowsWhenNothingIsFreeInReach()
    {
        var everythingBusy = new AlwaysBusy();

        var act = () => ServerPortService.FindFree(6969, everythingBusy, new HashSet<int>());

        act.Should().Throw<ServerPortException>().WithMessage("*No free port*");
    }

    [Fact]
    public void FindFree_NeverReturnsAPrivilegedPort()
    {
        ServerPortService.FindFree(80, _h.Ports, new HashSet<int>()).Should().Be(1024);
    }

    // ---- changing it ----

    [Fact]
    public async Task SetPort_WritesTheServersConfig_KeepingTheRest()
    {
        await _service.SetPortAsync(_one.Id, 7005);

        var config = JsonNode.Parse(File.ReadAllText(ServerWorld.HttpConfigOf(_one)))!;
        config["port"]!.GetValue<int>().Should().Be(7005);
        config["backendPort"]!.GetValue<int>().Should().Be(7005);
        config["ip"]!.GetValue<string>().Should().Be("0.0.0.0");
        config["logRequests"]!.GetValue<bool>().Should().BeTrue();
        new SptServerLocator(new FakePlatform(linux: true)).Locate(_one).Endpoint.Port.Should().Be(7005);
    }

    [Fact]
    public async Task SetPort_MirrorsTheNewPortInTheDatabase()
    {
        await _service.SetPortAsync(_one.Id, 7005);

        await using var context = _database.CreateContext();
        var row = await context.Instances.AsNoTracking().Include(item => item.ServerProfile).SingleAsync();
        row.ServerPort.Should().Be(7005);
        row.ServerProfile!.Port.Should().Be(7005);
        row.ServerProfile.BackendUrl.Should().Be("https://127.0.0.1:7005");
    }

    [Fact]
    public async Task SetPort_NeverWritesThroughAHardLink_TheBaseInstallStaysAsItWas()
    {
        var shared = _h.World.Temp.Combine("base-http.json");
        File.Copy(ServerWorld.HttpConfigOf(_one), shared);
        File.Delete(ServerWorld.HttpConfigOf(_one));
        _links.CreateHardLink(ServerWorld.HttpConfigOf(_one), shared);
        var before = File.ReadAllText(shared);

        await _service.SetPortAsync(_one.Id, 7005);

        File.ReadAllText(shared).Should().Be(before, "the file is shared with the base install and must not change");
        JsonNode.Parse(File.ReadAllText(ServerWorld.HttpConfigOf(_one)))!["port"]!.GetValue<int>().Should().Be(7005);
    }

    [Theory]
    [InlineData(80)]
    [InlineData(1023)]
    [InlineData(0)]
    [InlineData(65536)]
    [InlineData(-1)]
    public async Task SetPort_RefusesAPortOutsideTheUsableRange(int port)
    {
        var act = () => _service.SetPortAsync(_one.Id, port);

        await act.Should().ThrowAsync<ServerPortException>().WithMessage("*not usable*");
    }

    [Fact]
    public async Task SetPort_RefusesAnInstanceThatDoesNotExist()
    {
        var act = () => _service.SetPortAsync(Guid.NewGuid(), 7005);

        await act.Should().ThrowAsync<ServerPortException>().WithMessage("*does not exist*");
    }

    [Fact]
    public async Task SetPort_RefusesWhileTheServerIsRunning()
    {
        await _h.Supervisor.StartAsync(_one);

        var act = () => _service.SetPortAsync(_one.Id, 7005);

        await act.Should().ThrowAsync<ServerPortException>().WithMessage("*is running; stop it*");
        new SptServerLocator(new FakePlatform(linux: true)).Locate(_one).Endpoint.Port.Should().Be(6969);
    }

    [Fact]
    public async Task SetPort_RefusesAPortAnotherInstanceIsConfiguredFor_AndNamesIt()
    {
        _h.AddInstance("two", port: 7005);

        var act = () => _service.SetPortAsync(_one.Id, 7005);

        await act.Should().ThrowAsync<ServerPortException>().WithMessage("*'two'*");
    }

    [Fact]
    public async Task SetPort_RefusesAPortSomethingIsListeningOn()
    {
        _h.Ports.Busy.Add(7005);

        var act = () => _service.SetPortAsync(_one.Id, 7005);

        await act.Should().ThrowAsync<ServerPortException>().WithMessage("*in use by another program*");
    }

    [Fact]
    public async Task SetPort_AcceptsTheCurrentPort_EvenIfItsOwnServerHoldsIt()
    {
        _h.Ports.Busy.Add(6969);

        await _service.SetPortAsync(_one.Id, 6969);
    }

    [Fact]
    public async Task SetPort_RefusesAnInstanceWithoutAServerConfig()
    {
        File.Delete(ServerWorld.HttpConfigOf(_one));

        var act = () => _service.SetPortAsync(_one.Id, 7005);

        await act.Should().ThrowAsync<ServerPortException>().WithMessage("*does not exist*");
    }

    private sealed class AlwaysBusy : IPortProbe
    {
        public bool IsFree(int port)
        {
            return false;
        }
    }
}
