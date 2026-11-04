using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Logs;
using YuiTushonkaLauncher.Infrastructure.Logs;
using YuiTushonkaLauncher.Tests.Server;

namespace YuiTushonkaLauncher.Tests.Logs;

public sealed class LogRetentionTests : IDisposable
{
    private readonly Infrastructure.TempDirectory _temp = new();

    private readonly FastTimeProvider _time = new();

    private readonly LogRetention _retention;

    public LogRetentionTests()
    {
        _retention = new LogRetention(_time);
    }

    public void Dispose()
    {
        _temp.Dispose();
    }

    private string Write(string name, int daysAgo, int bytes = 10)
    {
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllText(path, new string('x', bytes));
        File.SetLastWriteTimeUtc(path, _time.GetUtcNow().AddDays(-daysAgo).UtcDateTime);
        return path;
    }

    [Fact]
    public void Plan_CompressesRotatedFiles_AndDeletesWhatIsBeyondTheCount()
    {
        for (var day = 1; day <= 5; day++)
        {
            Write($"launcher-2026110{day}.log", day);
        }

        var plan = _retention.Plan(_temp.Path, keepFiles: 2, keepDays: 0);

        plan.ToCompress.Should().HaveCount(2, "only what survives the count is worth compressing");
        plan.ToDelete.Should().HaveCount(3);
        plan.ToDelete.Should().Contain(path => path.EndsWith("launcher-20261104.log", StringComparison.Ordinal));
    }

    [Fact]
    public void Plan_DeletesByAge_BeforeItAppliesTheCount()
    {
        Write("ancient.log", 40);
        Write("recent.log", 1);

        var plan = _retention.Plan(_temp.Path, keepFiles: 10, keepDays: 14);

        plan.ToDelete.Should().Contain(path => path.EndsWith("ancient.log", StringComparison.Ordinal));
        plan.ToDelete.Should().NotContain(path => path.EndsWith("recent.log", StringComparison.Ordinal));
    }

    [Fact]
    public void Plan_LeavesTheFileTheLauncherIsWritingRightNow()
    {
        var active = Write("launcher-20261104.log", 0);

        var plan = _retention.Plan(_temp.Path, keepFiles: 1, keepDays: 0);

        plan.ToCompress.Should().NotContain(active);
        plan.ToDelete.Should().NotContain(active);
    }

    [Fact]
    public void Plan_DoesNotCompressWhatItIsAboutToDelete()
    {
        Write("old.log", 30);

        var plan = _retention.Plan(_temp.Path, keepFiles: 10, keepDays: 7);

        plan.ToCompress.Should().NotContain(path => path.EndsWith("old.log", StringComparison.Ordinal));
        plan.ToDelete.Should().Contain(path => path.EndsWith("old.log", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Apply_CompressesIntoTheArchive_AndReadsBackTheSameText()
    {
        Write("old.log", 3, bytes: 100);

        var plan = await _retention.ApplyAsync(_temp.Path, keepFiles: 5, keepDays: 0);

        plan.ToCompress.Should().HaveCount(1);
        File.Exists(Path.Combine(_temp.Path, "old.log")).Should().BeFalse("the plain file is replaced by its archive");
        var archived = Path.Combine(_temp.Path, LogRetention.ArchiveDirectory, "old.log.gz");
        File.Exists(archived).Should().BeTrue();

        await using var file = File.OpenRead(archived);
        await using var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        (await reader.ReadToEndAsync()).Should().Be(new string('x', 100));
    }

    [Fact]
    public async Task Apply_DeletesByCount_AndIsIdempotent()
    {
        for (var day = 1; day <= 4; day++)
        {
            Write($"launcher-2026110{day}.log", day);
        }

        await _retention.ApplyAsync(_temp.Path, keepFiles: 2, keepDays: 0);
        var afterFirst = Directory.EnumerateFiles(_temp.Path, "*.log*", SearchOption.AllDirectories).Count();

        var second = await _retention.ApplyAsync(_temp.Path, keepFiles: 2, keepDays: 0);

        second.ToDelete.Should().BeEmpty();
        Directory.EnumerateFiles(_temp.Path, "*.log*", SearchOption.AllDirectories).Count().Should().Be(afterFirst);
    }

    [Fact]
    public void Plan_IsEmpty_ForADirectoryThatDoesNotExist()
    {
        _retention.Plan(_temp.Combine("nope"), 5, 7).Should().Be(new RetentionPlan());
    }
}
