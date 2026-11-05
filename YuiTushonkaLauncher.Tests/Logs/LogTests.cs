using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Logs;
using YuiTushonkaLauncher.Infrastructure.Logs;
using YuiTushonkaLauncher.Tests.Infrastructure;
using YuiTushonkaLauncher.Tests.Server;

namespace YuiTushonkaLauncher.Tests.Logs;

public sealed class LogTests : IAsyncLifetime
{
    private readonly ServerWorld _world = new();

    private readonly TestDatabase _database = new();

    private readonly TempDirectory _appData = new();

    private readonly FastTimeProvider _time = new();

    private readonly LogReader _reader = new();

    private Instance _instance = null!;

    private LogCatalog _catalog = null!;

    private LogService _service = null!;

    private string Root
    {
        get { return _instance.Path; }
    }

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _instance = _world.CreateInstance("one");
        var paths = _appData.CreateAppPaths();
        paths.EnsureCreated();
        await _database.UnitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                context.Instances.Add(_instance);
                await Task.CompletedTask;
            }
        );

        _catalog = new LogCatalog(_database.UnitOfWork, paths);
        _service = new LogService(_database.UnitOfWork, _catalog, _reader, new InstanceList(_instance), _time);
    }

    public Task DisposeAsync()
    {
        _world.Dispose();
        _appData.Dispose();
        return _database.DisposeAsync().AsTask();
    }

    private string Write(string relative, string content)
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private string WriteBytes(string relative, long bytes)
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        stream.SetLength(bytes);
        return path;
    }

    // ---- the catalog finds the real layout ----

    [Fact]
    public async Task List_FindsTheFourSourcesOfARealInstall()
    {
        Write("Logs/log_2026.11.03_23-48-46_0.16.9.5.40743/2026.11.03_23-48-46_0.16.9.5.40743 application.log", "app\n");
        Write("Logs/log_2026.11.03_23-48-46_0.16.9.5.40743/2026.11.03_23-48-46_0.16.9.5.40743 traces.log", "trace\n");
        Write("BepInEx/LogOutput.log", "bepinex\n");
        Write("SPT_Runtime/user/logs/spt/spt20261104.log", "server\n");

        var files = await _catalog.ListAsync(_instance.Id);

        files
            .Select(file => file.Kind)
            .Distinct()
            .Should()
            .BeEquivalentTo([LogSourceKind.Game, LogSourceKind.BepInEx, LogSourceKind.Server]);
        files
            .Where(file => file.Kind == LogSourceKind.Game && file.Label.Contains("traces"))
            .Should()
            .ContainSingle()
            .Which.IsDefault.Should()
            .BeFalse();
        files.Where(file => file.IsDefault).Should().Contain(file => file.Kind == LogSourceKind.Game && file.Label.Contains("application"));
    }

    [Fact]
    public async Task List_IsEmpty_ForAnInstanceWithoutLogs_AndForOneThatDoesNotExist()
    {
        (await _catalog.ListAsync(_instance.Id)).Should().BeEmpty();
        (await _catalog.ListAsync(Guid.NewGuid())).Should().BeEmpty();
    }

    [Fact]
    public async Task List_PutsTheNewestRunsFirst()
    {
        Write("Logs/log_2026.11.01/2026.11.01 application.log", "old\n");
        File.SetLastWriteTimeUtc(Path.Combine(Root, "Logs/log_2026.11.01"), _time.GetUtcNow().AddDays(-3).UtcDateTime);
        Write("Logs/log_2026.11.04/2026.11.04 application.log", "new\n");

        var game = await _catalog.ListAsync(_instance.Id, LogSourceKind.Game);

        game.First().Label.Should().Contain("2026.11.04", "the newest run comes first; older runs stay for a report of yesterday's crash");
        game.Select(file => file.Label).Should().Contain(label => label.Contains("2026.11.01"));
    }

    // ---- reading a tail ----

    [Fact]
    public async Task ReadTail_ReturnsTheNewestLines_AndSaysWhichTheyAre()
    {
        var path = Write("BepInEx/LogOutput.log", "one\ntwo\nthree\n");
        var file = (await _catalog.ListAsync(_instance.Id, LogSourceKind.BepInEx)).Single();

        var tail = await _reader.ReadTailAsync(file, 2);

        tail.Lines.Select(line => line.Text).Should().Equal("two", "three");
        tail.Lines.Select(line => line.Number).Should().Equal([1, 2], "the number is the position in the tail, not in the file");
        tail.TooLarge.Should().BeFalse();
        tail.File.Path.AsSlashes().Should().Be(path.AsSlashes());
    }

    [Fact]
    public async Task ReadTail_HandlesACrLfFileAndATrailingBlankLine()
    {
        Write("BepInEx/LogOutput.log", "one\r\ntwo\r\n\r\n");
        var file = (await _catalog.ListAsync(_instance.Id, LogSourceKind.BepInEx)).Single();

        var tail = await _reader.ReadTailAsync(file, 10);

        tail.Lines.Select(line => line.Text).Should().Equal("one", "two");
    }

    [Fact]
    public async Task ReadTail_NeverLoadsAFileAboveTheLimit_AndSaysWhy()
    {
        WriteBytes("BepInEx/LogOutput.log", LogLimits.MaxFollowedBytes + 1);
        var file = (await _catalog.ListAsync(_instance.Id, LogSourceKind.BepInEx)).Single();

        var tail = await _reader.ReadTailAsync(file, 10);

        tail.TooLarge.Should().BeTrue();
        tail.Lines.Should().BeEmpty();
        tail.Reason.Should().Contain("above the 64 MiB limit").And.Contain("text editor");
    }

    [Fact]
    public async Task ReadTail_ReadsAWindowFromTheEnd_AndDropsTheFragmentItStartsWith()
    {
        // A file whose bytes at the window boundary fall inside a line: a full read would return that half line, a
        // windowed read must drop it instead of showing a cut word as if it were a log entry.
        var filler = new string('x', 1024 * 1024);
        Write("BepInEx/LogOutput.log", filler + "\nFULL-LINE-AFTER-THE-WINDOW\ntail-one\ntail-two\n");
        var file = (await _catalog.ListAsync(_instance.Id, LogSourceKind.BepInEx)).Single();

        var tail = await _reader.ReadTailAsync(file, 10);

        tail.Lines.Select(line => line.Text).Should().NotContain(text => text.StartsWith("xxxx", StringComparison.Ordinal));
        tail.Lines[^1].Text.Should().Be("tail-two");
        tail.Lines.Should().NotContain(line => line.Text.Length > 200, "the window starts mid-line and that fragment is dropped");
    }

    [Fact]
    public async Task ReadTail_ReadsOnlyTheEndOfALargeButAllowedFile()
    {
        // Just under the limit: the reader must still take the tail, not the beginning.
        var lines = new StringBuilder();

        for (var number = 0; number < 60_000; number++)
        {
            lines.Append("line ").Append(number).Append('\n');
        }

        Write("BepInEx/LogOutput.log", lines.ToString());
        var file = (await _catalog.ListAsync(_instance.Id, LogSourceKind.BepInEx)).Single();

        var tail = await _reader.ReadTailAsync(file, 3);

        tail.Lines.Should().HaveCount(3);
        tail.Lines[^1].Text.Should().Be("line 59999");
    }

    [Fact]
    public async Task ReadTail_SaysWhenTheFileIsGone()
    {
        var file = new LogFile(LogSourceKind.Server, Path.Combine(Root, "gone.log"), "gone.log", 10, null, true);

        var tail = await _reader.ReadTailAsync(file);

        tail.Missing.Should().BeTrue();
        tail.Reason.Should().NotBeNullOrEmpty();
    }

    // ---- exporting ----

    [Fact]
    public async Task ExportLines_WritesExactlyWhatWasSelected()
    {
        Write("BepInEx/LogOutput.log", "alpha\nbeta\ngamma\n");
        var file = (await _catalog.ListAsync(_instance.Id, LogSourceKind.BepInEx)).Single();
        var tail = await _reader.ReadTailAsync(file, 2);
        var destination = Path.Combine(_appData.Path, "export.txt");

        await _service.ExportLinesAsync(tail.Lines, destination);

        var text = await File.ReadAllTextAsync(destination);
        var lines = tail.Lines.Select(line => line.Text).ToList();
        lines.Should().Equal("beta", "gamma");
        text.Should().Contain("beta").And.Contain("gamma").And.NotContain("alpha");
        Encoding.UTF8.GetPreamble().Should().NotBeNull();
        (await File.ReadAllBytesAsync(destination))
            .Take(3)
            .Should()
            .NotEqual([0xEF, 0xBB, 0xBF], "no BOM: it breaks a paste into a bug report");
    }

    [Fact]
    public async Task ExportDiagnostics_ContainsVersionsStateModsAndTheLastLinesOfEachSource()
    {
        Write("BepInEx/LogOutput.log", "bepinex says hello\n");
        Write("SPT_Runtime/user/logs/spt/spt20261104.log", "server says hello\n");
        Write("Logs/log_2026.11.04/2026.11.04 application.log", "game says hello\n");
        await _database.UnitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                context.InstalledMods.Add(
                    new InstalledMod
                    {
                        Id = Guid.NewGuid(),
                        InstanceId = _instance.Id,
                        Guid = "com.fixture.mod",
                        Name = "Fixture",
                        Kind = ModKind.Server,
                        Version = SemanticVersion.Parse("1.2.3"),
                        InstalledAt = _time.GetUtcNow(),
                    }
                );
                await Task.CompletedTask;
            }
        );

        var destination = Path.Combine(_appData.Combine("diag"), "report.txt");
        await _service.ExportDiagnosticsAsync(_instance.Id, destination);

        var text = await File.ReadAllTextAsync(destination);
        text.Should().Contain("Instance: one").And.Contain("SPT: 4.1.6").And.Contain("layout: Spt41Plus (Merged)");
        text.Should()
            .Contain("EscapeFromTarkov.exe: ")
            .And.Contain("Assembly-CSharp.dll: MISSING", "the fixture has no patched binary, and a bundle must say so");
        text.Should().Contain("com.fixture.mod 1.2.3 [Server] Fixture");
        text.Should().Contain("bepinex says hello").And.Contain("server says hello").And.Contain("game says hello");
    }

    [Fact]
    public async Task ExportDiagnostics_RefusesAnInstanceThatDoesNotExist()
    {
        var act = () => _service.ExportDiagnosticsAsync(Guid.NewGuid(), Path.Combine(_appData.Path, "x.txt"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not exist*");
    }

    /// <summary>The instance service as the log service uses it: one row.</summary>
    private sealed class InstanceList(Instance instance) : Core.Instances.IInstanceService
    {
        public Task<IReadOnlyList<Instance>> ListAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Instance>>([instance]);
        }

        public Task<Instance?> GetAsync(Guid instanceId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Instance?>(instance.Id == instanceId ? instance : null);
        }

        public Task<Core.Instances.InstancePlan> PlanAsync(
            Core.Instances.InstanceCreationRequest request,
            CancellationToken cancellationToken = default
        )
        {
            throw new NotSupportedException();
        }

        public Task<Instance> CreateAsync(
            Core.Instances.InstanceCreationRequest request,
            IProgress<Core.Instances.InstanceCreationProgress>? progress = null,
            CancellationToken cancellationToken = default
        )
        {
            throw new NotSupportedException();
        }

        public Task DeleteAsync(Guid instanceId, bool deleteFiles, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task RenameAsync(Guid instanceId, string newName, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Core.Instances.PatchStateInfo> DetectPatchStateAsync(Guid instanceId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
