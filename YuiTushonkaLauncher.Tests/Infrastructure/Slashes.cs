namespace YuiTushonkaLauncher.Tests.Infrastructure;

/// <summary>
/// Paths are compared with forward slashes. The product builds them with <c>Path.Combine</c> (so a
/// Windows host produces backslashes) while the fixtures are written with literals; a test that pins one separator
/// only passes on one operating system, which is what the first CI run on Windows found.
/// </summary>
public static class Slashes
{
    /// <summary>The path with every separator as <c>/</c>.</summary>
    public static string AsSlashes(this string path)
    {
        return path.Replace('\\', '/');
    }
}
