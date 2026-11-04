using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Forge;

namespace YuiTushonkaLauncher.Tests.Forge;

/// <summary>A scriptable <see cref="IForgeClient" /> for the phase-6 services.</summary>
public sealed class FakeForgeClient : IForgeClient
{
    /// <summary>Catalogue keyed by GUID.</summary>
    public Dictionary<string, ForgeMod> Mods { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Dependencies keyed by "<c>guid:version</c>".</summary>
    public Dictionary<string, List<ForgeDependency>> Dependencies { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What <see cref="CheckUpdatesAsync" /> should answer.</summary>
    public ForgeUpdateReport? Report { get; set; }

    /// <summary>Addon versions keyed by the addon's Forge id.</summary>
    public Dictionary<int, List<ForgeAddonVersion>> AddonVersions { get; } = [];

    /// <summary>When set, every version lookup fails like an unreachable Forge.</summary>
    public bool Unreachable { get; set; }

    /// <summary>GUID batches the resolver asked for, in order.</summary>
    public List<string[]> GuidRequests { get; } = [];

    /// <summary>Adds a mod with one version.</summary>
    public FakeForgeClient WithMod(string guid, string version, int forgeId = 1, string? name = null)
    {
        Mods[guid] = new ForgeMod
        {
            Id = forgeId,
            Guid = guid,
            Name = name ?? guid,
            Slug = guid,
            Author = "fixture",
            Versions =
            [
                new ForgeModVersion
                {
                    Id = forgeId * 10,
                    Version = SemanticVersion.Parse(version),
                    SptConstraint = VersionRange.Any,
                    DownloadUrl = $"https://sp-mod.com/mod/download/{forgeId}/x/{version}",
                },
            ],
        };

        return this;
    }

    /// <summary>Adds an addon version that supports the mod versions in <paramref name="modConstraint" />.</summary>
    public FakeForgeClient WithAddon(int addonId, string version, string modConstraint = "*")
    {
        if (!AddonVersions.TryGetValue(addonId, out var list))
        {
            list = [];
            AddonVersions[addonId] = list;
        }

        list.Add(
            new ForgeAddonVersion
            {
                Id = addonId * 10,
                Version = SemanticVersion.Parse(version),
                ModVersionConstraint = VersionRange.Parse(modConstraint),
                DownloadUrl = $"https://sp-mod.com/addon/download/{addonId}/x/{version}",
            }
        );

        return this;
    }

    /// <summary>Declares that <paramref name="owner" /> depends on <paramref name="dependency" />.</summary>
    public FakeForgeClient WithDependency(
        string owner,
        string ownerVersion,
        string dependency,
        string? dependentVersion = null,
        bool optional = false
    )
    {
        var key = $"{owner}:{ownerVersion}";
        if (!Dependencies.TryGetValue(key, out var list))
        {
            list = [];
            Dependencies[key] = list;
        }

        list.Add(
            new ForgeDependency
            {
                Guid = dependency,
                Version = dependentVersion is null ? null : SemanticVersion.Parse(dependentVersion),
                IsOptional = optional,
            }
        );

        return this;
    }

    public Task<ForgePing> PingAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ForgePing { Message = "pong" });
    }

    public Task<ForgePage<ForgeMod>> GetModsAsync(ForgeModQuery query, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            new ForgePage<ForgeMod>
            {
                Items = [.. Mods.Values],
                Page = 1,
                PerPage = 25,
                Total = Mods.Count,
                LastPage = 1,
            }
        );
    }

    public Task<ForgeMod> GetModAsync(int modId, CancellationToken cancellationToken = default)
    {
        var mod = Mods.Values.FirstOrDefault(candidate => candidate.Id == modId);
        return mod is null ? throw new ForgeRequestException(404, "NOT_FOUND", "missing") : Task.FromResult(mod);
    }

    public Task<IReadOnlyList<ForgeModVersion>> GetModVersionsAsync(int modId, CancellationToken cancellationToken = default)
    {
        if (Unreachable)
        {
            throw new ForgeUnavailableException("the fake Forge is unreachable");
        }

        var mod = Mods.Values.FirstOrDefault(candidate => candidate.Id == modId);
        return Task.FromResult(mod?.Versions ?? []);
    }

    public Task<IReadOnlyList<ForgeMod>> FindModsByGuidsAsync(
        IReadOnlyCollection<string> guids,
        CancellationToken cancellationToken = default
    )
    {
        GuidRequests.Add([.. guids]);

        return Task.FromResult<IReadOnlyList<ForgeMod>>([.. guids.Where(guid => Mods.ContainsKey(guid)).Select(guid => Mods[guid])]);
    }

    public Task<ForgeUpdateReport> CheckUpdatesAsync(
        IReadOnlyCollection<InstalledPackage> installed,
        SemanticVersion sptVersion,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(Report ?? new ForgeUpdateReport { SptVersion = sptVersion });
    }

    public Task<IReadOnlyDictionary<string, IReadOnlyList<ForgeDependency>>> GetDependenciesAsync(
        IReadOnlyCollection<InstalledPackage> packages,
        SemanticVersion sptVersion,
        CancellationToken cancellationToken = default
    )
    {
        var result = new Dictionary<string, IReadOnlyList<ForgeDependency>>(StringComparer.OrdinalIgnoreCase);

        foreach (var package in packages)
        {
            var key = $"{package.Guid}:{package.Version}";
            result[key] = Dependencies.TryGetValue(key, out var list) ? list : [];
        }

        return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<ForgeDependency>>>(result);
    }

    public Task<ForgePage<ForgeAddon>> GetAddonsAsync(ForgeAddonQuery query, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            new ForgePage<ForgeAddon>
            {
                Items = [],
                Page = 1,
                PerPage = 25,
                Total = 0,
                LastPage = 1,
            }
        );
    }

    public Task<IReadOnlyList<ForgeAddonVersion>> GetAddonVersionsAsync(int addonId, CancellationToken cancellationToken = default)
    {
        if (Unreachable)
        {
            throw new ForgeUnavailableException("the fake Forge is unreachable");
        }

        return Task.FromResult<IReadOnlyList<ForgeAddonVersion>>(AddonVersions.TryGetValue(addonId, out var list) ? list : []);
    }

    public Task<IReadOnlyList<ForgeSptVersion>> GetSptVersionsAsync(int perPage = 50, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ForgeSptVersion>>([]);
    }

    public Task<IReadOnlyList<ForgeCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ForgeCategory>>([]);
    }
}
