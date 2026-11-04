using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Patching;
using YuiTushonkaLauncher.Patching;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Patching;

/// <summary>
/// The real HDiffPatch engine. The error paths run everywhere; the end-to-end test needs a real SPT
/// install (it patches a copy of the vanilla assembly with the shipped delta); set <c>YTT_SPT_BASE</c> to the
/// SPT-installer output to run them (<c>dotnet test --filter Live=Spt</c>), they pass trivially without it.
/// </summary>
public sealed class SharpHDiffPatchEngineTests : IDisposable
{
    /// <summary>Points at the SPT-installer output; the same variable the other live tests use.</summary>
    private const string BaseInstallVariable = "YTT_SPT_BASE";

    private readonly TempDirectory _temp = new();
    private readonly SharpHDiffPatchEngine _engine = new(NullLogger<SharpHDiffPatchEngine>.Instance);

    private static string Fixture(string name)
    {
        return Path.Combine(AppContext.BaseDirectory, "Patching", "Fixtures", name);
    }

    [Fact]
    public void ReadHeader_OfARealDelta_DeclaresBothSizes()
    {
        var header = _engine.ReadHeader(Fixture("small.delta"));

        header.ExpectedInputSize.Should().Be(new FileInfo(Fixture("old.bin")).Length);
        header.ExpectedOutputSize.Should().Be(new FileInfo(Fixture("new.bin")).Length);
    }

    [Fact]
    public void Apply_RealDelta_ReproducesTheNewFileByteForByte()
    {
        var output = _temp.Combine("patched.bin");
        var before = File.ReadAllBytes(Fixture("old.bin"));

        _engine.Apply(Fixture("small.delta"), Fixture("old.bin"), output);

        File.ReadAllBytes(output).Should().Equal(File.ReadAllBytes(Fixture("new.bin")));
        File.ReadAllBytes(Fixture("old.bin")).Should().Equal(before, "the source must never be modified");
    }

    [Fact]
    public void Apply_RealDelta_ReportsProgressUpToTheTotal()
    {
        var reports = new List<PatchProgress>();

        // A synchronous IProgress: Progress<T> would post to the thread pool and race the assertions.
        _engine.Apply(Fixture("small.delta"), Fixture("old.bin"), _temp.Combine("patched.bin"), new InlineProgress(reports));

        reports.Should().NotBeEmpty();
        reports.Should().OnlyContain(report => report.FileName == "old.bin");
        reports[^1].BytesWritten.Should().Be(new FileInfo(Fixture("new.bin")).Length);
    }

    [Fact]
    public void Apply_RealDelta_WithACancelledToken_Throws()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var act = () => _engine.Apply(Fixture("small.delta"), Fixture("old.bin"), _temp.Combine("patched.bin"), null, cancelled.Token);

        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public async Task Apply_TwoPatchesAtOnce_DoNotMixTheirProgress()
    {
        var first = new List<PatchProgress>();
        var second = new List<PatchProgress>();

        // The library reports through static events, so the engine serialises calls; both runs must
        // still finish with correct output and only see their own file's progress.
        await Task.WhenAll(
            Task.Run(() => _engine.Apply(Fixture("small.delta"), Fixture("old.bin"), _temp.Combine("a.bin"), new InlineProgress(first))),
            Task.Run(() => _engine.Apply(Fixture("small.delta"), Fixture("old.bin"), _temp.Combine("b.bin"), new InlineProgress(second)))
        );

        File.ReadAllBytes(_temp.Combine("a.bin")).Should().Equal(File.ReadAllBytes(Fixture("new.bin")));
        File.ReadAllBytes(_temp.Combine("b.bin")).Should().Equal(File.ReadAllBytes(Fixture("new.bin")));
        first.Should().NotBeEmpty();
        second.Should().NotBeEmpty();
    }

    [Fact]
    public void ReadHeader_ForAMissingFile_Throws()
    {
        var act = () => _engine.ReadHeader(_temp.Combine("missing.delta"));

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void ReadHeader_ForSomethingThatIsNotADelta_ThrowsInvalidData()
    {
        var path = _temp.Combine("garbage.delta");
        File.WriteAllText(path, "this is not an HDiffPatch file");

        var act = () => _engine.ReadHeader(path);

        act.Should().Throw<InvalidDataException>().WithMessage("*garbage.delta*");
    }

    [Fact]
    public void Apply_WithABogusDelta_Throws()
    {
        var delta = _temp.Combine("bogus.delta");
        var source = _temp.Combine("source.bin");
        File.WriteAllText(delta, "nope");
        File.WriteAllBytes(source, [1, 2, 3]);

        var act = () => _engine.Apply(delta, source, _temp.Combine("out.bin"));

        act.Should().Throw<Exception>();
    }

    [Fact]
    [Trait("Live", "Spt")]
    public void Apply_RealSptDelta_ProducesAValidAssemblyOfTheDeclaredSize()
    {
        var basePath = Environment.GetEnvironmentVariable(BaseInstallVariable);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            // No real install configured: the fake-engine tests above cover the service, this one
            // proves the real library against the real delta.
            return;
        }

        var delta = Directory
            .EnumerateFiles(Path.Combine(basePath, "spt"), "Assembly-CSharp.dll.delta", SearchOption.AllDirectories)
            .FirstOrDefault();
        var vanilla = Path.Combine(basePath, "gamefiles", "EscapeFromTarkov_Data", "Managed", "Assembly-CSharp.dll.spt-bak");
        if (delta is null || !File.Exists(vanilla))
        {
            return;
        }

        var header = _engine.ReadHeader(delta);
        new FileInfo(vanilla).Length.Should().Be(header.ExpectedInputSize, "the delta must declare the vanilla size it patches");

        var output = _temp.Combine("patched.dll");
        var progress = new List<PatchProgress>();
        _engine.Apply(delta, vanilla, output, new Progress<PatchProgress>(progress.Add));

        new FileInfo(output).Length.Should().Be(header.ExpectedOutputSize);

        var before = Describe(vanilla);
        var after = Describe(output);

        // The patch rewrites the assembly (4.1.6: 15,137 -> 15,088 types): it must stay a loadable
        // Assembly-CSharp and must not be the vanilla file under another name.
        after.AssemblyName.Should().Be("Assembly-CSharp");
        before.AssemblyName.Should().Be("Assembly-CSharp");
        after.TypeCount.Should().NotBe(before.TypeCount, "the patch changes the assembly's type table");
        FileHash.TryCompute(output).Should().NotBe(FileHash.TryCompute(vanilla));

        File.ReadAllBytes(vanilla).Length.Should().Be((int)header.ExpectedInputSize, "the source must not be modified");
    }

    [Fact]
    [Trait("Live", "Spt")]
    public void Apply_RealSptDelta_IsDeterministic()
    {
        var basePath = Environment.GetEnvironmentVariable(BaseInstallVariable);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return;
        }

        var delta = Directory
            .EnumerateFiles(Path.Combine(basePath, "spt"), "Assembly-CSharp.dll.delta", SearchOption.AllDirectories)
            .FirstOrDefault();
        var vanilla = Path.Combine(basePath, "gamefiles", "EscapeFromTarkov_Data", "Managed", "Assembly-CSharp.dll.spt-bak");
        if (delta is null || !File.Exists(vanilla))
        {
            return;
        }

        _engine.Apply(delta, vanilla, _temp.Combine("first.dll"));
        _engine.Apply(delta, vanilla, _temp.Combine("second.dll"));

        FileHash.TryCompute(_temp.Combine("first.dll")).Should().Be(FileHash.TryCompute(_temp.Combine("second.dll")));
    }

    public void Dispose()
    {
        _temp.Dispose();
    }

    private sealed class InlineProgress(List<PatchProgress> sink) : IProgress<PatchProgress>
    {
        public void Report(PatchProgress value)
        {
            lock (sink)
            {
                sink.Add(value);
            }
        }
    }

    private static (string AssemblyName, int TypeCount) Describe(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();

        return (reader.GetString(reader.GetAssemblyDefinition().Name), reader.TypeDefinitions.Count);
    }
}
