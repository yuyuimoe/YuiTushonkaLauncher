using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Core.Configuration;

namespace YuiTushonkaLauncher.Infrastructure.Persistence;

/// <summary>Database-backed settings, cached in memory and written through one row.</summary>
public sealed class EfSettingsService : ISettingsService, IDisposable
{
    private readonly IDbContextFactory<LauncherDbContext> _contextFactory;
    private readonly ILogger<EfSettingsService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LauncherSettings _current = LauncherSettings.Default;

    /// <summary>Creates the service.</summary>
    public EfSettingsService(IDbContextFactory<LauncherDbContext> contextFactory, ILogger<EfSettingsService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public LauncherSettings Current
    {
        get { return _current; }
    }

    /// <inheritdoc />
    public event EventHandler<LauncherSettings>? Changed;

    /// <inheritdoc />
    public async Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var record = await context.Settings.SingleOrDefaultAsync(row => row.Id == SettingsRecord.SingletonId, cancellationToken);

            if (record is null)
            {
                // First run: store the defaults so later reads do not have to special-case an empty table.
                record = SettingsRecord.FromSettings(LauncherSettings.Default);
                context.Settings.Add(record);
                await context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Created the default settings row");
            }

            SetCurrent(record.ToSettings());
            return _current;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await SqliteRetry.ExecuteAsync(
                async () =>
                {
                    await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                    var record = await context.Settings.SingleOrDefaultAsync(
                        row => row.Id == SettingsRecord.SingletonId,
                        cancellationToken
                    );

                    if (record is null)
                    {
                        record = SettingsRecord.FromSettings(settings);
                        context.Settings.Add(record);
                    }
                    else
                    {
                        record.CopyFromSettings(settings);
                    }

                    await context.SaveChangesAsync(cancellationToken);
                },
                cancellationToken
            );

            SetCurrent(settings);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _gate.Dispose();
    }

    private void SetCurrent(LauncherSettings settings)
    {
        _current = settings;
        Changed?.Invoke(this, settings);
    }
}
