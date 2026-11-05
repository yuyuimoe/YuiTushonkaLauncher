using System.Text.Json.Serialization;

namespace YuiTushonkaLauncher.Infrastructure.Forge.Dtos;

/// <summary>The API envelope: <c>{ success, data, links, meta }</c> or <c>{ success:false, code, message }</c>.</summary>
internal sealed class ApiEnvelope<T>
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("data")]
    public T? Data { get; set; }

    [JsonPropertyName("meta")]
    public ApiMeta? Meta { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>Laravel pagination metadata.</summary>
internal sealed class ApiMeta
{
    [JsonPropertyName("current_page")]
    public int CurrentPage { get; set; }

    [JsonPropertyName("last_page")]
    public int LastPage { get; set; }

    [JsonPropertyName("per_page")]
    public int PerPage { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

internal sealed class ModDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("hub_id")]
    public int? HubId { get; set; }

    [JsonPropertyName("guid")]
    public string? Guid { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("teaser")]
    public string? Teaser { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("thumbnail")]
    public string? Thumbnail { get; set; }

    [JsonPropertyName("detail_url")]
    public string? DetailUrl { get; set; }

    [JsonPropertyName("downloads")]
    public int Downloads { get; set; }

    [JsonPropertyName("favourites_count")]
    public int FavouritesCount { get; set; }

    [JsonPropertyName("category_id")]
    public int? CategoryId { get; set; }

    [JsonPropertyName("featured")]
    public bool Featured { get; set; }

    [JsonPropertyName("fika_compatibility")]
    public bool FikaCompatibility { get; set; }

    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("owner")]
    public OwnerDto? Owner { get; set; }

    [JsonPropertyName("versions")]
    public List<ModVersionDto>? Versions { get; set; }
}

internal sealed class OwnerDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal sealed class ModVersionDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("link")]
    public string? Link { get; set; }

    [JsonPropertyName("content_length")]
    public long? ContentLength { get; set; }

    [JsonPropertyName("spt_version_constraint")]
    public string? SptVersionConstraint { get; set; }

    [JsonPropertyName("spt_versions")]
    public List<string>? SptVersions { get; set; }

    [JsonPropertyName("downloads")]
    public int Downloads { get; set; }

    [JsonPropertyName("fika_compatibility")]
    public string? FikaCompatibility { get; set; }

    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; set; }
}

internal sealed class AddonDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("mod_id")]
    public int ModId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("teaser")]
    public string? Teaser { get; set; }

    [JsonPropertyName("thumbnail")]
    public string? Thumbnail { get; set; }

    [JsonPropertyName("detail_url")]
    public string? DetailUrl { get; set; }

    [JsonPropertyName("is_detached")]
    public bool IsDetached { get; set; }

    [JsonPropertyName("downloads")]
    public int Downloads { get; set; }

    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; set; }

    [JsonPropertyName("versions")]
    public List<AddonVersionDto>? Versions { get; set; }
}

internal sealed class AddonVersionDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("link")]
    public string? Link { get; set; }

    [JsonPropertyName("content_length")]
    public long? ContentLength { get; set; }

    [JsonPropertyName("mod_version_constraint")]
    public string? ModVersionConstraint { get; set; }

    [JsonPropertyName("downloads")]
    public int Downloads { get; set; }

    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; set; }
}

internal sealed class SptVersionDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("mod_count")]
    public int ModCount { get; set; }

    [JsonPropertyName("link")]
    public string? Link { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }
}

internal sealed class CategoryDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;
}

internal sealed class PingDto
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

internal sealed class UpdatesDto
{
    [JsonPropertyName("spt_version")]
    public string? SptVersion { get; set; }

    [JsonPropertyName("updates")]
    public List<UpdatePairDto>? Updates { get; set; }

    [JsonPropertyName("blocked_updates")]
    public List<UpdatePairDto>? BlockedUpdates { get; set; }

    [JsonPropertyName("up_to_date")]
    public List<UpdateTargetDto>? UpToDate { get; set; }

    [JsonPropertyName("incompatible_with_spt")]
    public List<IncompatibleDto>? IncompatibleWithSpt { get; set; }
}

internal sealed class UpdateTargetDto
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("mod_id")]
    public int? ModId { get; set; }

    [JsonPropertyName("guid")]
    public string? Guid { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("spt_versions")]
    public List<string>? SptVersions { get; set; }
}

internal sealed class UpdatePairDto
{
    [JsonPropertyName("current_version")]
    public UpdateTargetDto? CurrentVersion { get; set; }

    [JsonPropertyName("recommended_version")]
    public UpdateCandidateDto? RecommendedVersion { get; set; }
}

internal sealed class UpdateCandidateDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("link")]
    public string? Link { get; set; }

    [JsonPropertyName("content_length")]
    public long? ContentLength { get; set; }

    [JsonPropertyName("fika_compatibility")]
    public string? FikaCompatibility { get; set; }

    [JsonPropertyName("spt_versions")]
    public List<string>? SptVersions { get; set; }
}

internal sealed class IncompatibleDto
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("mod_id")]
    public int? ModId { get; set; }

    [JsonPropertyName("guid")]
    public string? Guid { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("latest_compatible_version")]
    public string? LatestCompatibleVersion { get; set; }
}

/// <summary>One dependency row. Fields are optional: the payload is not fully documented.</summary>
/// <summary>
/// One entry of <c>/mods/dependencies</c>. The mod is identified by <c>id</c> — there is no <c>mod_id</c> field, and a
/// reader that expects one silently finds no dependencies at all. The version to install is not the entry's own but the
/// one in <c>latest_compatible_version</c>.
/// </summary>
internal sealed class DependencyDto
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("guid")]
    public string? Guid { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("is_optional")]
    public bool? IsOptional { get; set; }

    [JsonPropertyName("conflict")]
    public bool? Conflict { get; set; }

    [JsonPropertyName("latest_compatible_version")]
    public CompatibleVersionDto? LatestCompatibleVersion { get; set; }
}

/// <summary>The version of a dependency that the asking package's SPT release accepts.</summary>
internal sealed class CompatibleVersionDto
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("link")]
    public string? Link { get; set; }
}
