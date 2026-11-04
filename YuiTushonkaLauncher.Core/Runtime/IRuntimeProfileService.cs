using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Core.Runtime;

/// <summary>The editable part of a runtime profile.</summary>
public sealed record RuntimeProfileDraft
{
    public required string Name { get; init; }

    public required RuntimeKind Kind { get; init; }

    /// <summary>A Proton build directory (or its <c>proton</c> script) or a <c>wine</c> binary.</summary>
    public string? Executable { get; init; }

    /// <summary>The Wine prefix; <c>null</c> means one inside each instance.</summary>
    public string? PrefixPath { get; init; }

    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

    /// <summary>Extra arguments in the Steam launch-options format (<c>-flag NAME=value "a b"</c>).</summary>
    public string? ExtraArguments { get; init; }

    public bool UseGameMode { get; init; }

    public bool MakeDefault { get; init; }
}

/// <summary>A profile was rejected; the message says which field and why.</summary>
public sealed class RuntimeProfileException(string message) : Exception(message);

/// <summary>Stores runtime profiles and decides which one an instance uses.</summary>
public interface IRuntimeProfileService
{
    Task<IReadOnlyList<RuntimeProfile>> ListAsync(CancellationToken cancellationToken = default);

    Task<RuntimeProfile?> FindAsync(string name, CancellationToken cancellationToken = default);

    /// <exception cref="RuntimeProfileException">The draft is invalid or the name is taken.</exception>
    Task<RuntimeProfile> CreateAsync(RuntimeProfileDraft draft, CancellationToken cancellationToken = default);

    /// <exception cref="RuntimeProfileException">The draft is invalid, the name is taken or the profile is gone.</exception>
    Task<RuntimeProfile> UpdateAsync(Guid id, RuntimeProfileDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Deletes a profile; instances that used it fall back to the default.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Uses <paramref name="profileId" /> for an instance; <c>null</c> goes back to the default profile.</summary>
    Task AssignAsync(Guid instanceId, Guid? profileId, CancellationToken cancellationToken = default);

    /// <summary>The profile an instance runs with: its own, else the default, else <c>null</c>.</summary>
    Task<RuntimeProfile?> ResolveForInstanceAsync(Guid instanceId, CancellationToken cancellationToken = default);
}
