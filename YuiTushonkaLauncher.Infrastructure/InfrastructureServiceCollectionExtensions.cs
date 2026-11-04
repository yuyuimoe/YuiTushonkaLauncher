using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Infrastructure.Persistence;

namespace YuiTushonkaLauncher.Infrastructure;

/// <summary>Registers the persistence layer. The only place that knows EF Core exists.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Adds the SQLite database, its factory, settings, unit of work and the startup initializer.
    /// </summary>
    public static IServiceCollection AddLauncherPersistence(this IServiceCollection services)
    {
        services.AddSingleton<LauncherDbContextFactory>();
        services.AddSingleton<IDbContextFactory<LauncherDbContext>>(provider => provider.GetRequiredService<LauncherDbContextFactory>());

        services.AddSingleton<ISettingsService, EfSettingsService>();
        services.AddSingleton<IUnitOfWork, EfUnitOfWork>();
        services.AddSingleton<DatabaseInitializer>();

        return services;
    }
}
