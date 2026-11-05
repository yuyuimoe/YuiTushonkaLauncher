using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Infrastructure.Server;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Server;

public sealed class InstanceRunStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    private readonly InstanceRunStore _store;

    private readonly string _runDirectory;

    public InstanceRunStoreTests()
    {
        var paths = _temp.CreateAppPaths();
        paths.EnsureCreated();
        _runDirectory = Path.Combine(paths.DataRoot, "run");
        _store = new InstanceRunStore(paths, NullLogger<InstanceRunStore>.Instance);
    }

    public void Dispose()
    {
        _temp.Dispose();
    }

    private static RunProcess Server(int pid = 321, RunMechanism mechanism = RunMechanism.Setsid)
    {
        return new RunProcess(pid, new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero), "/srv/SPT.Server.Linux", mechanism, 6969);
    }

    private static RunProcess Game(int pid = 654)
    {
        return new RunProcess(
            pid,
            new DateTimeOffset(2026, 11, 4, 12, 0, 5, TimeSpan.Zero),
            "/srv/EscapeFromTarkov.exe",
            RunMechanism.SystemdScope
        );
    }

    private static InstanceRun Run(Guid? id = null, string name = "one")
    {
        return new InstanceRun
        {
            InstanceId = id ?? Guid.NewGuid(),
            InstanceName = name,
            LauncherProcessId = 99,
        };
    }

    [Fact]
    public void Save_ThenReadAll_RoundTripsThePair()
    {
        var run = Run().WithServer(Server()).WithGame(Game());

        _store.Save(run);

        var read = _store.ReadAll().Should().ContainSingle().Subject;
        read.InstanceId.Should().Be(run.InstanceId);
        read.InstanceName.Should().Be("one");
        read.Server.Should().Be(Server());
        read.Game.Should().Be(Game());
        read.LauncherProcessId.Should().Be(99);
    }

    [Fact]
    public void OneRecordPerInstance_HoldsBothProcesses()
    {
        var id = Guid.NewGuid();

        _store.Save(Run(id).WithServer(Server()));
        _store.Save(_store.Find(id)!.WithGame(Game()));
        _store.Save(_store.Find(id)!.WithServer(Server(pid: 322)));

        _store.ReadAll().Should().ContainSingle();
        _store.ReadAll().Single().Entries().Should().Be(2);
    }

    [Fact]
    public void Find_IsNullForAnInstanceWithoutARecord_AndForOneThatWasForgotten()
    {
        var id = Guid.NewGuid();

        _store.Find(id).Should().BeNull();

        _store.Save(Run(id).WithServer(Server()));
        _store.Find(id).Should().NotBeNull();

        _store.Forget(id);
        _store.Forget(id);

        _store.Find(id).Should().BeNull();
        _store.ReadAll().Should().BeEmpty();
    }

    [Fact]
    public void ReadAll_IsEmptyBeforeAnythingWasWritten()
    {
        _store.ReadAll().Should().BeEmpty();
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        _store.Save(Run().WithServer(Server()));

        Directory.EnumerateFiles(_runDirectory, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void ReadAll_SkipsADamagedRecord_AndKeepsTheFileAsEvidence()
    {
        _store.Save(Run().WithServer(Server()));
        var damaged = Path.Combine(_runDirectory, "run-damaged.json");
        File.WriteAllText(damaged, "{not json");

        _store.ReadAll().Should().HaveCount(1);
        File.Exists(damaged).Should().BeTrue("a damaged record is what a crash leaves behind");
    }

    [Fact]
    public void ReadAll_IgnoresARecordWithoutAnInstance()
    {
        Directory.CreateDirectory(_runDirectory);
        File.WriteAllText(
            Path.Combine(_runDirectory, "run-empty.json"),
            """{"instanceId":"00000000-0000-0000-0000-000000000000","instanceName":"x"}"""
        );

        _store.ReadAll().Should().BeEmpty();
    }
}

internal static class InstanceRunExtensions
{
    public static int Entries(this InstanceRun run)
    {
        return (run.Server is null ? 0 : 1) + (run.Game is null ? 0 : 1);
    }
}
