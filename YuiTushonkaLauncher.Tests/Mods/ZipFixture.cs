using System.IO.Compression;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Mods;

/// <summary>Builds a throwaway zip archive for the archive tests.</summary>
public sealed class ZipFixture : IDisposable
{
    private readonly TempDirectory _temp = new();

    public ZipFixture()
    {
        Path = _temp.Combine($"archive-{Guid.NewGuid():n}.zip");
        using var archive = ZipFile.Open(Path, ZipArchiveMode.Create);

        // A valid empty zip so tests that add nothing still have a readable archive.
    }

    public string Path { get; }

    /// <summary>Adds an entry with text content.</summary>
    public ZipFixture Add(string entryPath, string content = "content")
    {
        using var archive = ZipFile.Open(Path, ZipArchiveMode.Update);
        var entry = archive.CreateEntry(entryPath, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(content);
        return this;
    }

    /// <summary>Adds a file from the test output of the given fixture assembly.</summary>
    public ZipFixture AddAssembly(string entryPath, string assemblyFileName)
    {
        using var archive = ZipFile.Open(Path, ZipArchiveMode.Update);
        var entry = archive.CreateEntry(entryPath, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var source = File.OpenRead(System.IO.Path.Combine(AppContext.BaseDirectory, assemblyFileName));
        source.CopyTo(stream);
        return this;
    }

    public void Dispose()
    {
        _temp.Dispose();
    }
}
