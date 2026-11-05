using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YuiTushonkaLauncher.Core.Forge;

namespace YuiTushonkaLauncher.Tests.Forge;

/// <summary>A graph that answers from a list instead of walking the catalogue, and records what it was asked.</summary>
public sealed class FakeDependencyGraph : IDependencyGraph
{
    /// <summary>The relations the fake pretends the API reported.</summary>
    public List<ForgeDependencyEdge> Edges { get; } = [];

    /// <summary>Every mod id the dependents tab asked about.</summary>
    public List<int> DependentRequests { get; } = [];

    /// <summary>Every mod id the dependencies side asked about.</summary>
    public List<int> DependencyRequests { get; } = [];

    /// <summary>How many times the cache was thrown away.</summary>
    public int Invalidations { get; private set; }

    /// <summary>How many times the page asked for the catalogue to be read early.</summary>
    public int WarmUps { get; private set; }

    public void WarmUp()
    {
        WarmUps++;
    }

    public Task<IReadOnlyList<ForgeDependencyEdge>> DependentsOfAsync(
        int modId,
        IProgress<DependencyGraphProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        DependentRequests.Add(modId);
        return Task.FromResult<IReadOnlyList<ForgeDependencyEdge>>([.. Edges.Where(edge => edge.ToId == modId)]);
    }

    public Task<IReadOnlyList<ForgeDependencyEdge>> DependenciesOfAsync(
        int modId,
        IProgress<DependencyGraphProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        DependencyRequests.Add(modId);
        return Task.FromResult<IReadOnlyList<ForgeDependencyEdge>>([.. Edges.Where(edge => edge.FromId == modId)]);
    }

    public void Invalidate()
    {
        Invalidations++;
    }
}
