using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Infrastructure.Server;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Server;

public sealed class ServerPidStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    private readonly ServerPidStore _store;

    public ServerPidStoreTests()
    {
        var paths = _temp.CreateAppPaths();
        paths.EnsureCreated();
        _store = new ServerPidStore(paths, NullLogger<ServerPidStore>.Instance);
    }

    public void Dispose()
    {
        _temp.Dispose();
    }

    private static ServerPidRecord Record(Guid? id = null, int pid = 321)
    {
        return new ServerPidRecord(
            id ?? Guid.NewGuid(),
            pid,
            new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero),
            "/srv/SPT.Server.Linux",
            6969,
            99
        );
    }

    [Fact]
    public void Write_ThenReadAll_RoundTripsTheRecord()
    {
        var record = Record();

        _store.Write(record);

        _store.ReadAll().Should().ContainSingle().Which.Should().Be(record);
    }

    [Fact]
    public void Write_ReplacesTheRecordOfTheSameInstance()
    {
        var id = Guid.NewGuid();
        _store.Write(Record(id, pid: 1));
        _store.Write(Record(id, pid: 2));

        _store.ReadAll().Should().ContainSingle().Which.ProcessId.Should().Be(2);
    }

    [Fact]
    public void Delete_RemovesTheRecord_AndIsHarmlessWhenThereIsNone()
    {
        var record = Record();
        _store.Write(record);

        _store.Delete(record.InstanceId);
        _store.Delete(record.InstanceId);

        _store.ReadAll().Should().BeEmpty();
    }

    [Fact]
    public void ReadAll_IsEmpty_WhenNothingWasEverWritten()
    {
        _store.ReadAll().Should().BeEmpty();
    }

    [Fact]
    public void ReadAll_SkipsADamagedFile_AndLeavesItAlone()
    {
        _store.Write(Record());
        var damaged = Path.Combine(_temp.Combine("data"), "run", "server-damaged.json");
        File.WriteAllText(damaged, "{not json");

        _store.ReadAll().Should().HaveCount(1);
        File.Exists(damaged).Should().BeTrue("a damaged record is evidence, not something to delete");
    }

    [Fact]
    public void ReadAll_IgnoresARecordWithoutAProcessId()
    {
        _store.Write(Record(pid: 0));

        _store.ReadAll().Should().BeEmpty();
    }
}
