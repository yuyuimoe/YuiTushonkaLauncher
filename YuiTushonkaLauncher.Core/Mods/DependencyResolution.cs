using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Core.Mods;

/// <summary>A package the caller wants installed.</summary>
public sealed record PackageRequest
{
    /// <summary>GUID of the package.</summary>
    public required string Guid { get; init; }

    /// <summary>Exact version wanted, or <c>null</c> for "whatever satisfies the constraints".</summary>
    public SemanticVersion? Version { get; init; }

    /// <summary>Whether the caller asked for it or it is pulled in as a dependency.</summary>
    public bool IsExplicit { get; init; } = true;
}

/// <summary>One package the resolver decided to install.</summary>
public sealed record ResolvedPackage
{
    public required string Guid { get; init; }

    public required SemanticVersion Version { get; init; }

    /// <summary>Sort position: lower installs first (dependencies before dependents).</summary>
    public required int Order { get; init; }

    /// <summary>GUID of the package that pulled this one in, or <c>null</c> for explicit requests.</summary>
    public string? RequiredBy { get; init; }

    /// <summary>True when the package is already installed at a satisfying version.</summary>
    public bool AlreadyInstalled { get; init; }
}

/// <summary>Why a package could not be resolved.</summary>
public sealed record ResolutionProblem
{
    public required string Guid { get; init; }

    /// <summary>What went wrong, phrased for the user.</summary>
    public required string Reason { get; init; }

    /// <summary>Constraint that could not be satisfied, when applicable.</summary>
    public string? Constraint { get; init; }
}

/// <summary>The plan for one install batch: what to install, in order, and what is wrong.</summary>
public sealed record ResolutionPlan
{
    public IReadOnlyList<ResolvedPackage> Install { get; init; } = [];

    public IReadOnlyList<ResolutionProblem> Problems { get; init; } = [];

    /// <summary>Cycles that were detected (each entry lists the GUIDs involved).</summary>
    public IReadOnlyList<IReadOnlyList<string>> Cycles { get; init; } = [];

    /// <summary>True when the plan can be applied as-is.</summary>
    public bool IsResolvable
    {
        get { return Problems.Count == 0 && Cycles.Count == 0; }
    }

    /// <summary>Packages that would actually be downloaded (already installed ones excluded).</summary>
    public IReadOnlyList<ResolvedPackage> Pending
    {
        get { return [.. Install.Where(package => !package.AlreadyInstalled)]; }
    }
}

/// <summary>Resolves a set of requested packages plus their dependencies into an install order.</summary>
public interface IDependencyResolver
{
    /// <summary>
    /// Resolves <paramref name="requests" /> against the Forge API and what is already installed in the
    /// instance. The result never throws for a resolvable set of inputs: conflicts and cycles come back
    /// as <see cref="ResolutionPlan.Problems" /> / <see cref="ResolutionPlan.Cycles" />.
    /// </summary>
    Task<ResolutionPlan> ResolveAsync(
        Guid instanceId,
        IReadOnlyCollection<PackageRequest> requests,
        CancellationToken cancellationToken = default
    );
}
