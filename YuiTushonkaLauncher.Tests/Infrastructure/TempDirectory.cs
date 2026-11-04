using YuiTushonkaLauncher.Infrastructure.Paths;

namespace YuiTushonkaLauncher.Tests.Infrastructure;

/// <summary>Creates a throwaway directory tree and removes it on dispose.</summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Join(System.IO.Path.GetTempPath(), "ytt-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>The absolute path of the temporary root.</summary>
    public string Path { get; }

    /// <summary>A child path inside the temporary root.</summary>
    public string Combine(params string[] parts)
    {
        return System.IO.Path.Join(Path, System.IO.Path.Combine(parts));
    }

    /// <summary>Builds an <see cref="AppPaths" /> rooted in this directory.</summary>
    public AppPaths CreateAppPaths()
    {
        return new AppPaths(Combine("data"), Combine("config"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leaked temp directory must never fail a test run.
        }
    }
}
