using System.Text.Json;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Core.Configuration;

namespace YuiTushonkaLauncher.Infrastructure.Configuration;

/// <summary>
/// JSON-file settings store used for bootstrap settings. Phase 2 moves settings into the SQLite
/// database; this type is dropped then, and no consumer has to change.
/// </summary>
public sealed class JsonSettingsService : ISettingsService, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly ILogger<JsonSettingsService> _logger;
    private readonly string _settingsFile;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LauncherSettings _current = LauncherSettings.Default;

    /// <summary>Creates the service for the given path set.</summary>
    public JsonSettingsService(IAppPaths paths, ILogger<JsonSettingsService> logger)
    {
        _logger = logger;
        _settingsFile = paths.SettingsFile;
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
            if (!File.Exists(_settingsFile))
            {
                _logger.LogDebug("No settings file at {Path}; using defaults", _settingsFile);
                SetCurrent(LauncherSettings.Default);
                return _current;
            }

            try
            {
                await using var stream = File.OpenRead(_settingsFile);
                var loaded = await JsonSerializer.DeserializeAsync<LauncherSettings>(stream, SerializerOptions, cancellationToken);
                SetCurrent(loaded ?? LauncherSettings.Default);
            }
            catch (JsonException exception)
            {
                // A corrupt settings file must not block startup: keep it for the user, start clean.
                var quarantine = $"{_settingsFile}.invalid";
                _logger.LogWarning(exception, "Settings file {Path} is corrupt; moved to {Quarantine}", _settingsFile, quarantine);
                File.Move(_settingsFile, quarantine, overwrite: true);
                SetCurrent(LauncherSettings.Default);
            }

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
            var directory = Path.GetDirectoryName(_settingsFile);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write to a sibling temp file, then swap it in: a crash mid-write cannot truncate settings.
            var tempFile = $"{_settingsFile}.tmp";
            await using (var stream = File.Create(tempFile))
            {
                await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(tempFile, _settingsFile, overwrite: true);
            _logger.LogDebug("Settings saved to {Path}", _settingsFile);
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
