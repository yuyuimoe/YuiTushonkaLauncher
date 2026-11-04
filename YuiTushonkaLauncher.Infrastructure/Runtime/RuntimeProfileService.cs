using Microsoft.EntityFrameworkCore;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Infrastructure.Persistence;

namespace YuiTushonkaLauncher.Infrastructure.Runtime;

/// <summary>Stores runtime profiles. Exactly one profile is the default; deleting a profile never leaves an instance dangling.</summary>
public sealed class RuntimeProfileService(IUnitOfWork unitOfWork) : IRuntimeProfileService
{
    private const int MaxNameLength = 128;

    private const int MaxPathLength = 1024;

    /// <inheritdoc />
    public async Task<IReadOnlyList<RuntimeProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await unitOfWork.ExecuteAsync(
            async (context, token) =>
                (IReadOnlyList<RuntimeProfile>)await context.RuntimeProfiles.AsNoTracking().OrderBy(row => row.Name).ToListAsync(token),
            cancellationToken
        );
    }

    /// <inheritdoc />
    public async Task<RuntimeProfile?> FindAsync(string name, CancellationToken cancellationToken = default)
    {
        return await unitOfWork.ExecuteAsync(
            async (context, token) => await context.RuntimeProfiles.AsNoTracking().FirstOrDefaultAsync(row => row.Name == name, token),
            cancellationToken
        );
    }

    /// <inheritdoc />
    public async Task<RuntimeProfile> CreateAsync(RuntimeProfileDraft draft, CancellationToken cancellationToken = default)
    {
        Validate(draft);

        return await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                await EnsureNameFreeAsync(context, draft.Name, null, token);

                var first = !await context.RuntimeProfiles.AnyAsync(token);
                var row = new RuntimeProfile { Id = Guid.NewGuid() };
                Apply(row, draft);

                // The first profile is the default whether or not it asked: an instance must always resolve to something.
                row.IsDefault = draft.MakeDefault || first;

                if (row.IsDefault)
                {
                    await ClearDefaultAsync(context, token);
                }

                context.RuntimeProfiles.Add(row);
                return row;
            },
            cancellationToken
        );
    }

    /// <inheritdoc />
    public async Task<RuntimeProfile> UpdateAsync(Guid id, RuntimeProfileDraft draft, CancellationToken cancellationToken = default)
    {
        Validate(draft);

        return await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                var row =
                    await context.RuntimeProfiles.FirstOrDefaultAsync(profile => profile.Id == id, token)
                    ?? throw new RuntimeProfileException("That runtime profile no longer exists.");
                await EnsureNameFreeAsync(context, draft.Name, id, token);

                var wasDefault = row.IsDefault;
                Apply(row, draft);
                row.IsDefault = wasDefault;

                if (draft.MakeDefault && !wasDefault)
                {
                    await ClearDefaultAsync(context, token);
                    row.IsDefault = true;
                }

                return row;
            },
            cancellationToken
        );
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                var row = await context.RuntimeProfiles.FirstOrDefaultAsync(profile => profile.Id == id, token);

                if (row is null)
                {
                    return;
                }

                await foreach (
                    var instance in context
                        .Instances.Where(instance => instance.RuntimeProfileId == id)
                        .AsAsyncEnumerable()
                        .WithCancellation(token)
                )
                {
                    instance.RuntimeProfileId = null;
                }

                context.RuntimeProfiles.Remove(row);

                if (row.IsDefault)
                {
                    // Keep the "always one default" promise: the oldest-named remaining profile takes over.
                    var next = await context
                        .RuntimeProfiles.Where(profile => profile.Id != id)
                        .OrderBy(profile => profile.Name)
                        .FirstOrDefaultAsync(token);

                    if (next is not null)
                    {
                        next.IsDefault = true;
                    }
                }
            },
            cancellationToken
        );
    }

    /// <inheritdoc />
    public async Task AssignAsync(Guid instanceId, Guid? profileId, CancellationToken cancellationToken = default)
    {
        await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                var instance =
                    await context.Instances.FirstOrDefaultAsync(row => row.Id == instanceId, token)
                    ?? throw new RuntimeProfileException("That instance no longer exists.");

                if (profileId is { } id && !await context.RuntimeProfiles.AnyAsync(profile => profile.Id == id, token))
                {
                    throw new RuntimeProfileException("That runtime profile no longer exists.");
                }

                instance.RuntimeProfileId = profileId;
            },
            cancellationToken
        );
    }

    /// <inheritdoc />
    public async Task<RuntimeProfile?> ResolveForInstanceAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        return await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                var assigned = await context
                    .Instances.AsNoTracking()
                    .Where(row => row.Id == instanceId)
                    .Select(row => row.RuntimeProfileId)
                    .FirstOrDefaultAsync(token);

                if (
                    assigned is { } id
                    && await context.RuntimeProfiles.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, token) is { } own
                )
                {
                    return own;
                }

                return await context.RuntimeProfiles.AsNoTracking().FirstOrDefaultAsync(row => row.IsDefault, token);
            },
            cancellationToken
        );
    }

    private static void Validate(RuntimeProfileDraft draft)
    {
        var name = draft.Name?.Trim() ?? string.Empty;

        if (name.Length is 0 or > MaxNameLength)
        {
            throw new RuntimeProfileException($"The profile name must be 1 to {MaxNameLength} characters.");
        }

        if (draft.Kind == RuntimeKind.Proton && string.IsNullOrWhiteSpace(draft.Executable))
        {
            throw new RuntimeProfileException("A Proton profile needs the path of a Proton build.");
        }

        foreach (var (field, value) in new[] { ("Executable", draft.Executable), ("PrefixPath", draft.PrefixPath) })
        {
            if (value is not null && (value.Length > MaxPathLength || value.Contains('\0', StringComparison.Ordinal)))
            {
                throw new RuntimeProfileException($"{field} is not a valid path.");
            }
        }

        if (draft.ExtraArguments is { Length: > 1024 })
        {
            throw new RuntimeProfileException("The extra arguments are longer than 1024 characters.");
        }

        var environment = draft.Environment.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var extra = LaunchOptions.Parse(draft.ExtraArguments);

        foreach (var pair in extra.Environment)
        {
            environment[pair.Key] = pair.Value;
        }

        var problems = LaunchOptions.ValidateEnvironment(environment).Concat(LaunchOptions.ValidateArguments(extra.Arguments)).ToList();

        if (problems.Count > 0)
        {
            throw new RuntimeProfileException(string.Join(" ", problems));
        }
    }

    private static async Task EnsureNameFreeAsync(LauncherDbContext context, string name, Guid? except, CancellationToken cancellationToken)
    {
        var trimmed = name.Trim();

        if (await context.RuntimeProfiles.AnyAsync(row => row.Name == trimmed && row.Id != except, cancellationToken))
        {
            throw new RuntimeProfileException($"A runtime profile called '{trimmed}' already exists.");
        }
    }

    private static async Task ClearDefaultAsync(LauncherDbContext context, CancellationToken cancellationToken)
    {
        await foreach (
            var other in context.RuntimeProfiles.Where(row => row.IsDefault).AsAsyncEnumerable().WithCancellation(cancellationToken)
        )
        {
            other.IsDefault = false;
        }
    }

    private static void Apply(RuntimeProfile row, RuntimeProfileDraft draft)
    {
        row.Name = draft.Name.Trim();
        row.Kind = draft.Kind;
        row.Executable = string.IsNullOrWhiteSpace(draft.Executable) ? null : draft.Executable.Trim();
        row.PrefixPath = string.IsNullOrWhiteSpace(draft.PrefixPath) ? null : draft.PrefixPath.Trim();
        row.Environment = draft.Environment.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        row.ExtraArguments = string.IsNullOrWhiteSpace(draft.ExtraArguments) ? null : draft.ExtraArguments.Trim();
        row.UseGameMode = draft.UseGameMode;
    }
}
