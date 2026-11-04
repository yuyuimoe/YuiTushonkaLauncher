using System.Text.Json;
using System.Text.Json.Serialization;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Forge;
using YuiTushonkaLauncher.Infrastructure.Forge.Dtos;

namespace YuiTushonkaLauncher.Infrastructure.Forge;

/// <summary>JSON settings and DTO-to-domain mapping for the Forge API.</summary>
internal static class ForgeMapper
{
    /// <summary>Serializer options: web defaults plus tolerance for numbers sent as strings.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static ForgeMod ToModel(ModDto dto)
    {
        return new ForgeMod
        {
            Id = dto.Id,
            HubId = dto.HubId,
            Guid = string.IsNullOrWhiteSpace(dto.Guid) ? null : dto.Guid,
            Name = dto.Name,
            Slug = dto.Slug,
            Teaser = dto.Teaser,
            Description = dto.Description,
            ThumbnailUrl = string.IsNullOrWhiteSpace(dto.Thumbnail) ? null : dto.Thumbnail,
            DetailUrl = dto.DetailUrl,
            Author = dto.Owner?.Name,
            Downloads = dto.Downloads,
            Favourites = dto.FavouritesCount,
            CategoryId = dto.CategoryId,
            IsFeatured = dto.Featured,
            FikaCompatible = dto.FikaCompatibility,
            PublishedAt = dto.PublishedAt,
            UpdatedAt = dto.UpdatedAt,
            Versions = dto.Versions is null ? [] : [.. dto.Versions.Select(ToModel)],
        };
    }

    public static ForgeModVersion ToModel(ModVersionDto dto)
    {
        if (!SemanticVersion.TryParse(dto.Version, out var version))
        {
            throw new ForgeException($"The API returned an unparsable mod version '{dto.Version}'.");
        }

        return new ForgeModVersion
        {
            Id = dto.Id,
            Version = version,
            SptConstraint = VersionRange.TryParse(dto.SptVersionConstraint, out var constraint) ? constraint : VersionRange.Any,
            SupportedSptVersions = ParseVersions(dto.SptVersions),
            DownloadUrl = dto.Link ?? string.Empty,
            ContentLength = dto.ContentLength,
            Downloads = dto.Downloads,
            FikaCompatibility = dto.FikaCompatibility,
            PublishedAt = dto.PublishedAt,
        };
    }

    public static ForgeAddon ToModel(AddonDto dto)
    {
        return new ForgeAddon
        {
            Id = dto.Id,
            ModId = dto.ModId,
            Name = dto.Name,
            Slug = dto.Slug,
            IsDetached = dto.IsDetached,
            Teaser = dto.Teaser,
            ThumbnailUrl = string.IsNullOrWhiteSpace(dto.Thumbnail) ? null : dto.Thumbnail,
            DetailUrl = dto.DetailUrl,
            Downloads = dto.Downloads,
            PublishedAt = dto.PublishedAt,
            Versions = dto.Versions is null ? [] : [.. dto.Versions.Select(ToModel)],
        };
    }

    public static ForgeAddonVersion ToModel(AddonVersionDto dto)
    {
        if (!SemanticVersion.TryParse(dto.Version, out var version))
        {
            throw new ForgeException($"The API returned an unparsable addon version '{dto.Version}'.");
        }

        return new ForgeAddonVersion
        {
            Id = dto.Id,
            Version = version,
            ModVersionConstraint = VersionRange.TryParse(dto.ModVersionConstraint, out var constraint) ? constraint : VersionRange.Any,
            DownloadUrl = dto.Link ?? string.Empty,
            ContentLength = dto.ContentLength,
            Downloads = dto.Downloads,
            PublishedAt = dto.PublishedAt,
        };
    }

    public static ForgeSptVersion ToModel(SptVersionDto dto)
    {
        if (!SemanticVersion.TryParse(dto.Version, out var version))
        {
            throw new ForgeException($"The API returned an unparsable SPT version '{dto.Version}'.");
        }

        return new ForgeSptVersion
        {
            ForgeId = dto.Id,
            Version = version,
            ModCount = dto.ModCount,
            ReleaseUrl = dto.Link,
            UpdatedAt = dto.UpdatedAt,
        };
    }

    public static ForgeCategory ToModel(CategoryDto dto)
    {
        return new ForgeCategory
        {
            Id = dto.Id,
            Title = dto.Title,
            Slug = dto.Slug,
        };
    }

    public static ForgeUpdateReport ToModel(UpdatesDto dto, SemanticVersion sptVersion)
    {
        return new ForgeUpdateReport
        {
            SptVersion = sptVersion,
            Updates = [.. (dto.Updates ?? []).Select(ToModel)],
            BlockedUpdates = [.. (dto.BlockedUpdates ?? []).Select(pair => ToTarget(pair.CurrentVersion))],
            UpToDate = [.. (dto.UpToDate ?? []).Select(ToTarget)],
            Incompatible = [.. (dto.IncompatibleWithSpt ?? []).Select(ToModel)],
        };
    }

    public static ForgeDependency ToModel(DependencyDto dto)
    {
        return new ForgeDependency
        {
            ModId = dto.ModId,
            Guid = dto.Guid,
            Name = dto.Name,
            Version = SemanticVersion.TryParse(dto.Version, out var version) ? version : null,
            IsOptional = dto.IsOptional,
        };
    }

    private static ForgeUpdate ToModel(UpdatePairDto pair)
    {
        if (pair.CurrentVersion is null || pair.RecommendedVersion is null)
        {
            throw new ForgeException("The API returned an update entry without a current or recommended version.");
        }

        var recommended = pair.RecommendedVersion;

        return new ForgeUpdate
        {
            Current = ToTarget(pair.CurrentVersion),
            Recommended = new ForgeUpdateCandidate
            {
                VersionId = recommended.Id,
                Version = ParseRequired(recommended.Version),
                DownloadUrl = recommended.Link ?? string.Empty,
                ContentLength = recommended.ContentLength,
                FikaCompatibility = recommended.FikaCompatibility,
                SupportedSptVersions = ParseVersions(recommended.SptVersions),
            },
        };
    }

    private static ForgeIncompatible ToModel(IncompatibleDto dto)
    {
        return new ForgeIncompatible
        {
            Current = new ForgeUpdateTarget
            {
                ModId = dto.ModId,
                VersionId = dto.Id,
                Guid = dto.Guid,
                Name = dto.Name,
                Version = ParseRequired(dto.Version),
            },
            Reason = dto.Reason ?? "unknown",
            LatestCompatibleVersion = SemanticVersion.TryParse(dto.LatestCompatibleVersion, out var latest) ? latest : null,
        };
    }

    private static ForgeUpdateTarget ToTarget(UpdateTargetDto? dto)
    {
        if (dto is null)
        {
            throw new ForgeException("The API returned an empty update target.");
        }

        return new ForgeUpdateTarget
        {
            ModId = dto.ModId,
            VersionId = dto.Id,
            Guid = dto.Guid,
            Name = dto.Name,
            Slug = dto.Slug,
            Version = ParseRequired(dto.Version),
        };
    }

    private static SemanticVersion ParseRequired(string value)
    {
        if (!SemanticVersion.TryParse(value, out var version))
        {
            throw new ForgeException($"The API returned an unparsable version '{value}'.");
        }

        return version;
    }

    private static IReadOnlyList<SemanticVersion> ParseVersions(List<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return [];
        }

        var parsed = new List<SemanticVersion>(values.Count);
        foreach (var value in values)
        {
            if (SemanticVersion.TryParse(value, out var version))
            {
                parsed.Add(version);
            }
        }

        return parsed;
    }
}
