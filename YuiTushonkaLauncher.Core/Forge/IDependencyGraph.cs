namespace YuiTushonkaLauncher.Core.Forge;

/// <summary>One "needs" relation between two published mods, as the API's dependency answers describe it.</summary>
public sealed record ForgeDependencyEdge
{
    /// <summary>The mod that declares the dependency.</summary>
    public required int FromId { get; init; }

    /// <summary>The declaring mod's GUID, when the API gave one.</summary>
    public string? FromGuid { get; init; }

    /// <summary>The declaring mod's name, for a readable row.</summary>
    public required string FromName { get; init; }

    /// <summary>The mod that is needed.</summary>
    public required int ToId { get; init; }

    /// <summary>The needed mod's GUID, when the API gave one.</summary>
    public string? ToGuid { get; init; }

    /// <summary>The needed mod's name.</summary>
    public required string ToName { get; init; }
}

/// <summary>How far along a catalogue walk is, so a long build can say something.</summary>
/// <param name="Page">The page just read.</param>
/// <param name="Pages">How many pages the catalogue has.</param>
/// <param name="Edges">How many relations have been found so far.</param>
public sealed record DependencyGraphProgress(int Page, int Pages, int Edges);

/// <summary>
/// Who needs whom, over the whole published catalogue. The API answers "what does this need" — never "who needs this",
/// there is no reverse endpoint — so the reverse direction is derived: every mod's dependency list is asked for, once,
/// and the answers are kept. It is a cache of API answers, not a second source of truth.
/// </summary>
public interface IDependencyGraph
{
    /// <summary>The mods that declare a dependency on <paramref name="modId" />.</summary>
    Task<IReadOnlyList<ForgeDependencyEdge>> DependentsOfAsync(
        int modId,
        IProgress<DependencyGraphProgress>? progress = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>The mods <paramref name="modId" /> declares a dependency on, from the same answers.</summary>
    Task<IReadOnlyList<ForgeDependencyEdge>> DependenciesOfAsync(
        int modId,
        IProgress<DependencyGraphProgress>? progress = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Starts the walk without anyone waiting for it. Called when the browser opens, so the dependents tab usually has
    /// an answer already instead of spending a hundred requests behind a click.
    /// </summary>
    void WarmUp();

    /// <summary>Forgets the cached answers so the next question walks the catalogue again.</summary>
    void Invalidate();
}
