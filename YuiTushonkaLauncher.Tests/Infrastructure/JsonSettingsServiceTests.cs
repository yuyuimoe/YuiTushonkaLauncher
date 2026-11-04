using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Configuration;
using YuiTushonkaLauncher.Infrastructure.Configuration;

namespace YuiTushonkaLauncher.Tests.Infrastructure;

public sealed class JsonSettingsServiceTests
{
    [Fact]
    public async Task LoadAsync_ReturnsDefaults_WhenNoFileExists()
    {
        using var temp = new TempDirectory();
        var paths = temp.CreateAppPaths();
        paths.EnsureCreated();

        using var service = new JsonSettingsService(paths, NullLogger<JsonSettingsService>.Instance);
        var loaded = await service.LoadAsync();

        loaded.Should().Be(LauncherSettings.Default);
        service.Current.Should().Be(LauncherSettings.Default);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsEveryValue()
    {
        using var temp = new TempDirectory();
        var paths = temp.CreateAppPaths();
        paths.EnsureCreated();

        var settings = LauncherSettings.Default with
        {
            Language = "pt-BR",
            Theme = "Dark",
            MaxConcurrentDownloads = 5,
            LogRetentionDays = 30,
            LogRetentionFiles = 7,
            FirstRunCompleted = true,
            InstancesRoot = temp.Combine("instances-custom"),
        };

        using (var writer = new JsonSettingsService(paths, NullLogger<JsonSettingsService>.Instance))
        {
            await writer.SaveAsync(settings);
        }

        using var reader = new JsonSettingsService(paths, NullLogger<JsonSettingsService>.Instance);
        var loaded = await reader.LoadAsync();

        loaded.Should().Be(settings);
        File.Exists(paths.SettingsFile).Should().BeTrue();

        // No temp file may survive a successful save.
        File.Exists(paths.SettingsFile + ".tmp").Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_QuarantinesCorruptFile_AndFallsBackToDefaults()
    {
        using var temp = new TempDirectory();
        var paths = temp.CreateAppPaths();
        paths.EnsureCreated();
        await File.WriteAllTextAsync(paths.SettingsFile, "{ not json at all ");

        using var service = new JsonSettingsService(paths, NullLogger<JsonSettingsService>.Instance);
        var loaded = await service.LoadAsync();

        loaded.Should().Be(LauncherSettings.Default);
        File.Exists(paths.SettingsFile).Should().BeFalse();
        File.Exists(paths.SettingsFile + ".invalid").Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_RaisesChanged()
    {
        using var temp = new TempDirectory();
        var paths = temp.CreateAppPaths();
        paths.EnsureCreated();
        using var service = new JsonSettingsService(paths, NullLogger<JsonSettingsService>.Instance);

        LauncherSettings? observed = null;
        service.Changed += (_, updated) => observed = updated;

        var settings = LauncherSettings.Default with { Language = "pt-BR" };
        await service.SaveAsync(settings);

        observed.Should().Be(settings);
        service.Current.Should().Be(settings);
    }
}
