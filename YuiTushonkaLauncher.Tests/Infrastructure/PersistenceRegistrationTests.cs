using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Infrastructure;
using YuiTushonkaLauncher.Infrastructure.Paths;
using YuiTushonkaLauncher.Infrastructure.Persistence;

namespace YuiTushonkaLauncher.Tests.Infrastructure;

public sealed class PersistenceRegistrationTests
{
    [Fact]
    public void AddLauncherPersistence_ResolvesTheWholePersistenceStack()
    {
        using var temp = new TempDirectory();
        var paths = temp.CreateAppPaths();
        paths.EnsureCreated();

        using var provider = new ServiceCollection()
            .AddSingleton<IAppPaths>(paths)
            .AddLogging(builder => builder.AddProvider(NullLoggerProvider.Instance))
            .AddLauncherPersistence()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        provider.GetRequiredService<IDbContextFactory<LauncherDbContext>>().Should().NotBeNull();
        provider.GetRequiredService<IUnitOfWork>().Should().BeOfType<EfUnitOfWork>();
        provider.GetRequiredService<ISettingsService>().Should().BeOfType<EfSettingsService>();
        provider.GetRequiredService<DatabaseInitializer>().Should().NotBeNull();
    }

    [Fact]
    public void AppPathsWithDefaultRoots_UsesThePlatformConventions()
    {
        var paths = new AppPaths();

        paths.DataRoot.Should().EndWith(AppPaths.AppFolderName);
        paths.ConfigRoot.Should().EndWith(AppPaths.AppFolderName);
        paths.SettingsFile.Should().EndWith("settings.json");
        Path.IsPathRooted(paths.DataRoot).Should().BeTrue();
    }
}
