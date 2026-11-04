using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Infrastructure.Paths;

namespace YuiTushonkaLauncher.Tests.Infrastructure;

public sealed class AppPathsTests
{
    [Fact]
    public void EnsureCreated_CreatesEveryOwnedDirectory()
    {
        using var temp = new TempDirectory();
        var paths = temp.CreateAppPaths();

        paths.EnsureCreated();

        Directory.Exists(paths.DataRoot).Should().BeTrue();
        Directory.Exists(paths.ConfigRoot).Should().BeTrue();
        Directory.Exists(paths.LogsDirectory).Should().BeTrue();
        Directory.Exists(paths.CacheDirectory).Should().BeTrue();
        Directory.Exists(paths.DownloadsDirectory).Should().BeTrue();
        Directory.Exists(paths.InstancesDirectory).Should().BeTrue();
    }

    [Fact]
    public void DerivedPaths_LiveUnderTheExpectedRoots()
    {
        using var temp = new TempDirectory();
        var paths = temp.CreateAppPaths();

        paths.LogsDirectory.Should().Be(Path.Join(paths.DataRoot, "logs"));
        paths.CacheDirectory.Should().Be(Path.Join(paths.DataRoot, "cache"));
        paths.DownloadsDirectory.Should().Be(Path.Join(paths.DataRoot, "downloads"));
        paths.InstancesDirectory.Should().Be(Path.Join(paths.DataRoot, "instances"));
        paths.DatabaseFile.Should().Be(Path.Join(paths.DataRoot, "launcher.db"));
        paths.LockFile.Should().Be(Path.Join(paths.DataRoot, "launcher.lock"));

        // Settings are user-editable, so they belong in the config root, not next to the database.
        paths.SettingsFile.Should().Be(Path.Join(paths.ConfigRoot, "settings.json"));
    }

    [Fact]
    public void ExplicitRoots_AreAbsoluteEvenWhenRelative()
    {
        using var temp = new TempDirectory();
        var relativeData = Path.GetRelativePath(Directory.GetCurrentDirectory(), temp.Combine("data"));

        var paths = new AppPaths(relativeData, temp.Combine("config"));

        Path.IsPathRooted(paths.DataRoot).Should().BeTrue();
    }

    [Fact]
    public void EnsureCreated_IsIdempotent()
    {
        using var temp = new TempDirectory();
        var paths = temp.CreateAppPaths();

        paths.EnsureCreated();
        paths.EnsureCreated();

        Directory.Exists(paths.InstancesDirectory).Should().BeTrue();
    }
}
