using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Logs;
using YuiTushonkaLauncher.Infrastructure.Logs;
using YuiTushonkaLauncher.Infrastructure.Paths;
using YuiTushonkaLauncher.Tests.Infrastructure;
using YuiTushonkaLauncher.Tests.Instances;
using YuiTushonkaLauncher.Tests.Mods;

namespace YuiTushonkaLauncher.Tests.Qa;

/// <summary>
/// Performance smoke tests: they print the number they measured and fail only on a generous ceiling, so a slow machine
/// does not break the build but a change that turns a linear scan into a quadratic one does. The numbers from a real run
/// are recorded in <c>docs/qa/perf.md</c>.
/// </summary>
[Collection(SerialCollection.Name)]
public sealed class PerfSmokeTests(ITestOutputHelper output)
{
    /// <summary>A thousand installed mods must be one pass over the rows, not a rebuild per row.</summary>
    [Fact]
    [Trait("Category", "Perf")]
    public async Task LoadingAndFilteringAThousandMods()
    {
        await using var context = await ModTestContext.CreateAsync("perf-mods");
        var rows = 1000;
        await context.Database.UnitOfWork.ExecuteAsync(
            async (db, token) =>
            {
                for (var index = 0; index < rows; index++)
                {
                    db.InstalledMods.Add(
                        new InstalledMod
                        {
                            Id = Guid.NewGuid(),
                            InstanceId = context.Instance.Id,
                            Guid = $"com.example.mod{index}",
                            Name = $"Mod {index}",
                            Version = new SemanticVersion(1, index, 0),
                            Kind = ModKind.Client,
                            InstalledAt = DateTimeOffset.UnixEpoch,
                        }
                    );
                }

                await Task.CompletedTask;
            }
        );

        var load = Stopwatch.StartNew();
        var instance = await context.Instances.GetAsync(context.Instance.Id);
        load.Stop();

        var filter = Stopwatch.StartNew();
        for (var index = 0; index < 100; index++)
        {
            _ = instance!.InstalledMods.Where(mod => mod.Name.Contains("Mod " + index, StringComparison.Ordinal)).ToList();
        }

        filter.Stop();

        output.WriteLine($"load {rows} mods from SQLite: {load.Elapsed.TotalMilliseconds:F0} ms");
        output.WriteLine($"100 filters over {rows} rows: {filter.Elapsed.TotalMilliseconds:F0} ms");

        instance!.InstalledMods.Should().HaveCount(rows);
        load.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        filter.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(4));
    }

    /// <summary>A clone is links plus copies; its cost must be dominated by the files that must be copied.</summary>
    [Fact]
    [Trait("Category", "Perf")]
    public async Task CreatingAnInstanceFromATwoHundredFileBase()
    {
        await using var world = await ModTestContext.CreateAsync("perf-create");
        var install = FakeBaseInstall.CreateSpt41();
        for (var index = 0; index < 200; index++)
        {
            install.WriteFile($"gamefiles/EscapeFromTarkov_Data/StreamingAssets/chunk{index}.bin", new string('x', 4096));
        }

        Directory.CreateDirectory(world.Locations.Combine("perf-instances"));
        var timer = Stopwatch.StartNew();
        var instance = await world.Instances.CreateAsync(
            new InstanceCreationRequest
            {
                Name = "perf",
                Location = world.Locations.Combine("perf-instances"),
                BaseInstallPath = install.Root,
            }
        );
        timer.Stop();

        output.WriteLine($"create from 200+ files: {timer.Elapsed.TotalMilliseconds:F0} ms ({instance.SptVersion})");
        File.Exists(Path.Join(instance.Path, "EscapeFromTarkov_Data", "StreamingAssets", "chunk199.bin")).Should().BeTrue();
        timer.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20));
    }

    /// <summary>
    /// Reading the tail of a large log must cost a window, not the file: the reader is the only thing standing between a
    /// 320 MiB session and a frozen window.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void TailingFortyFiveMegabytesAllocatesAWindowNotTheFile()
    {
        using var temp = new TempDirectory();
        var paths = new AppPaths(temp.Combine("data"), temp.Combine("config"));
        paths.EnsureCreated();
        var directory = paths.LogsDirectory;
        Directory.CreateDirectory(directory);
        var path = Path.Join(directory, "launcher-big.log");

        // 45 MiB: below the 64 MiB follow limit, so the size refusal cannot be what saves the test.
        var line = new string('x', 200);
        using (var stream = new StreamWriter(path))
        {
            for (var index = 0; index < 220_000; index++)
            {
                stream.WriteLine($"{index} {line}");
            }
        }

        var size = new FileInfo(path).Length;
        size.Should().BeGreaterThan(40L * 1024 * 1024);
        var reader = new LogReader();
        var file = new LogFile(LogSourceKind.Launcher, path, "launcher-big.log", size, DateTimeOffset.UtcNow, IsDefault: true);

        // The counters are process-wide and the read hops threads, so the measurement waits for the task instead of
        // sampling a thread's total around an await (which is how this test once "measured" 53 MB).
        GC.Collect();
        var before = GC.GetTotalAllocatedBytes();
        var timer = Stopwatch.StartNew();
        var tail = reader.ReadTailAsync(file, 200).GetAwaiter().GetResult();
        timer.Stop();
        var allocated = GC.GetTotalAllocatedBytes() - before;

        output.WriteLine($"tail of {size / 1024 / 1024} MiB: {timer.Elapsed.TotalMilliseconds:F0} ms, {allocated / 1024} KiB allocated");
        tail.Lines.Should().HaveCount(200);
        tail.TooLarge.Should().BeFalse();
        allocated.Should().BeLessThan(20L * 1024 * 1024, "the reader takes a 512 KiB window, it does not load a 45 MiB file");
        timer.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }
}
