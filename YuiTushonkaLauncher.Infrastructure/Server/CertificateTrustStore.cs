using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Core.Server;

namespace YuiTushonkaLauncher.Infrastructure.Server;

/// <summary>
/// Remembers the self-signed certificate of every server that is not this machine, so a different one later is noticed
/// instead of silently accepted. One small JSON file; a missing or broken file means "nothing recorded", never a
/// failure to connect.
/// </summary>
public sealed class CertificateTrustStore : ICertificateTrust
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ILogger<CertificateTrustStore> _logger;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly string _path;
    private readonly Dictionary<string, CertificateTrustInfo> _known = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    /// <summary>Creates the store under the launcher's data directory.</summary>
    public CertificateTrustStore(IAppPaths paths, TimeProvider time, ILogger<CertificateTrustStore> logger)
    {
        _time = time;
        _logger = logger;
        _path = Path.Combine(paths.CacheDirectory, "server-certificates.json");
    }

    /// <inheritdoc />
    public event EventHandler<CertificateTrustChange>? Changed;

    /// <inheritdoc />
    public CertificateTrustResult Evaluate(string address, string fingerprint)
    {
        lock (_gate)
        {
            Load();
            var now = DateTimeOffset.UtcNow;

            if (!_known.TryGetValue(address, out var recorded))
            {
                _known[address] = new CertificateTrustInfo(address, fingerprint, now, now);
                Save();
                return new CertificateTrustResult(CertificateTrustOutcome.FirstSeen);
            }

            if (string.Equals(recorded.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
            {
                _known[address] = recorded with { LastSeen = now };
                return new CertificateTrustResult(CertificateTrustOutcome.Unchanged);
            }

            // The old fingerprint stays until the new one has been seen again: one bad connection is not a new server.
            _logger.LogWarning(
                "The certificate of {Address} changed: recorded {Previous}, presented {Current}",
                address,
                recorded.Fingerprint,
                fingerprint
            );
            Changed?.Invoke(this, new CertificateTrustChange(address, recorded.Fingerprint, fingerprint));
            return new CertificateTrustResult(CertificateTrustOutcome.Changed, recorded.Fingerprint);
        }
    }

    /// <inheritdoc />
    public CertificateTrustInfo? Find(string address)
    {
        lock (_gate)
        {
            Load();
            return _known.GetValueOrDefault(address);
        }
    }

    /// <inheritdoc />
    public void Forget(string address)
    {
        lock (_gate)
        {
            Load();
            if (_known.Remove(address))
            {
                Save();
            }
        }
    }

    /// <summary>The SHA-256 of a certificate's raw bytes, as the key everything else compares.</summary>
    public static string Fingerprint(byte[] rawCertificate)
    {
        return Convert.ToHexString(SHA256.HashData(rawCertificate)).ToLowerInvariant();
    }

    private void Load()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<StorageFile>(File.ReadAllText(_path), Options);
            foreach (var entry in stored?.Entries ?? [])
            {
                _known[entry.Address] = entry.ToModel();
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or NotSupportedException)
        {
            _logger.LogWarning(exception, "The certificate record at {Path} could not be read; starting from nothing.", _path);
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(
                temp,
                JsonSerializer.Serialize(new StorageFile { Entries = [.. _known.Values.Select(StorageEntry.From)] }, Options)
            );
            File.Move(temp, _path, overwrite: true);
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "The certificate record could not be written to {Path}.", _path);
        }
    }

    private sealed class StorageFile
    {
        public List<StorageEntry> Entries { get; set; } = [];
    }

    private sealed class StorageEntry
    {
        public string Address { get; set; } = string.Empty;

        public string Fingerprint { get; set; } = string.Empty;

        public DateTimeOffset FirstSeen { get; set; }

        public DateTimeOffset LastSeen { get; set; }

        public static StorageEntry From(CertificateTrustInfo info)
        {
            return new StorageEntry
            {
                Address = info.Address,
                Fingerprint = info.Fingerprint,
                FirstSeen = info.FirstSeen,
                LastSeen = info.LastSeen,
            };
        }

        public CertificateTrustInfo ToModel()
        {
            return new CertificateTrustInfo(Address, Fingerprint, FirstSeen, LastSeen);
        }
    }
}
