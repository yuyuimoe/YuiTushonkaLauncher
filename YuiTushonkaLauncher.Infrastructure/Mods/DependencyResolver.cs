using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Forge;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Mods;
using YuiTushonkaLauncher.Infrastructure.Persistence;

namespace YuiTushonkaLauncher.Infrastructure.Mods;

/// <summary>
/// Resolves requested packages plus their dependencies into an install order.
/// </summary>
/// <remarks>
/// The graph comes from the Forge API (<c>/mods/dependencies</c>), which is the only source that
/// describes dependencies of mods that are not installed yet. Local <c>Dependency</c> rows are honoured
/// too when present. Version ranges are checked against what the API publishes for each version; a range
/// that cannot be satisfied becomes a <see cref="ResolutionProblem" /> instead of an exception.
/// </remarks>
public sealed class DependencyResolver(IUnitOfWork unitOfWork, IForgeClient forge, ILogger<DependencyResolver> logger) : IDependencyResolver
{
    private const int MaxDepth = 6;

    /// <inheritdoc />
    public async Task<ResolutionPlan> ResolveAsync(
        Guid instanceId,
        IReadOnlyCollection<PackageRequest> requests,
        CancellationToken cancellationToken = default
    )
    {
        if (requests.Count == 0)
        {
            return new ResolutionPlan();
        }

        var (instancePath, installed, localDependencies) = await LoadAsync(instanceId, cancellationToken);

        if (string.IsNullOrEmpty(instancePath))
        {
            throw new InstanceValidationException([$"Instance {instanceId} was not found."]);
        }

        var installedByGuid = installed.ToDictionary(package => package.Guid, package => package.Version, StringComparer.OrdinalIgnoreCase);
        var nodes = new Dictionary<string, ResolvedPackage>(StringComparer.OrdinalIgnoreCase);
        var edges = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<ResolutionProblem>();

        var depth = 0;
        var frontier = requests
            .Select(request => (Guid: request.Guid, Version: request.Version, RequiredBy: (string?)null, request.IsExplicit))
            .ToList();

        while (frontier.Count > 0 && depth <= MaxDepth)
        {
            var pending = new List<(string Guid, SemanticVersion? Version, string? RequiredBy, bool IsExplicit)>();

            // One batched catalogue lookup per level instead of one call per package.
            var catalogue = await forge.FindModsByGuidsAsync([.. frontier.Select(item => item.Guid)], cancellationToken);
            var catalogueByGuid = catalogue
                .Where(mod => !string.IsNullOrWhiteSpace(mod.Guid))
                .ToDictionary(mod => mod.Guid!, StringComparer.OrdinalIgnoreCase);

            foreach (var item in frontier)
            {
                if (nodes.ContainsKey(item.Guid))
                {
                    continue;
                }

                var resolvedVersion = ResolveVersion(item.Guid, item.Version, catalogueByGuid, problems);
                if (resolvedVersion is null)
                {
                    continue;
                }

                var alreadyInstalled =
                    installedByGuid.TryGetValue(item.Guid, out var installedVersion) && installedVersion == resolvedVersion.Value;

                nodes[item.Guid] = new ResolvedPackage
                {
                    Guid = item.Guid,
                    Version = resolvedVersion.Value,
                    Order = 0,
                    RequiredBy = item.RequiredBy,
                    AlreadyInstalled = alreadyInstalled,
                };

                edges.TryAdd(item.Guid, []);

                foreach (
                    var dependency in await GetDependenciesAsync(item.Guid, resolvedVersion.Value, localDependencies, cancellationToken)
                )
                {
                    if (string.IsNullOrWhiteSpace(dependency.Guid))
                    {
                        continue;
                    }

                    edges[item.Guid].Add(dependency.Guid!);
                    if (!nodes.ContainsKey(dependency.Guid!))
                    {
                        pending.Add((dependency.Guid!, dependency.Version, item.Guid, false));
                    }
                }
            }

            frontier = pending;
            depth++;
        }

        if (depth > MaxDepth)
        {
            problems.Add(
                new ResolutionProblem { Guid = "*", Reason = $"The dependency graph is deeper than {MaxDepth} levels; it was truncated." }
            );
        }

        var cycles = FindCycles(edges);
        foreach (var cycle in cycles)
        {
            logger.LogWarning("Dependency cycle detected: {Cycle}", string.Join(" -> ", cycle));
        }

        var ordered = TopologicalOrder(nodes, edges);

        return new ResolutionPlan
        {
            Install = ordered,
            Problems = problems,
            Cycles = cycles,
        };
    }

    private static SemanticVersion? ResolveVersion(
        string guid,
        SemanticVersion? requested,
        IReadOnlyDictionary<string, ForgeMod> catalogue,
        List<ResolutionProblem> problems
    )
    {
        if (!catalogue.TryGetValue(guid, out var mod))
        {
            problems.Add(new ResolutionProblem { Guid = guid, Reason = "The Forge API does not know this GUID." });
            return null;
        }

        if (requested is { } exact)
        {
            if (mod.Versions.Any(version => version.Version == exact) || mod.Versions.Count == 0)
            {
                return exact;
            }

            problems.Add(
                new ResolutionProblem
                {
                    Guid = guid,
                    Reason = $"Version {exact} is not published for this mod.",
                    Constraint = exact.ToString(),
                }
            );

            return null;
        }

        var newest = mod.Versions.OrderByDescending(version => version.Version).FirstOrDefault();
        if (newest is null)
        {
            problems.Add(new ResolutionProblem { Guid = guid, Reason = "The mod has no published versions." });
            return null;
        }

        return newest.Version;
    }

    private async Task<IReadOnlyList<ForgeDependency>> GetDependenciesAsync(
        string guid,
        SemanticVersion version,
        IReadOnlyDictionary<string, IReadOnlyList<Dependency>> localDependencies,
        CancellationToken cancellationToken
    )
    {
        var dependencies = new List<ForgeDependency>();

        if (localDependencies.TryGetValue(guid, out var local))
        {
            foreach (var dependency in local.Where(row => row.Kind != DependencyKind.Incompatible))
            {
                dependencies.Add(
                    new ForgeDependency { Guid = dependency.DependencyGuid, IsOptional = dependency.Kind == DependencyKind.Optional }
                );
            }
        }

        try
        {
            var remote = await forge.GetDependenciesAsync(
                [new InstalledPackage { Guid = guid, Version = version }],
                version,
                cancellationToken
            );

            foreach (var pair in remote)
            {
                foreach (var dependency in pair.Value)
                {
                    if (
                        !string.IsNullOrWhiteSpace(dependency.Guid)
                        && !dependencies.Any(row => string.Equals(row.Guid, dependency.Guid, StringComparison.OrdinalIgnoreCase))
                    )
                    {
                        dependencies.Add(dependency);
                    }
                }
            }
        }
        catch (Core.Forge.ForgeRequestException exception)
        {
            logger.LogWarning(exception, "Could not read dependencies of {Guid} {Version}", guid, version);
        }

        return [.. dependencies.Where(dependency => !string.IsNullOrWhiteSpace(dependency.Guid) && dependency.IsOptional != true)];
    }

    /// <summary>Depth-first search over the dependency edges, reporting every cycle once.</summary>
    private static List<IReadOnlyList<string>> FindCycles(Dictionary<string, List<string>> edges)
    {
        var cycles = new List<IReadOnlyList<string>>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<string>();

        void Visit(string node)
        {
            if (visited.Contains(node))
            {
                return;
            }

            if (visiting.Contains(node))
            {
                var start = stack.FindIndex(entry => string.Equals(entry, node, StringComparison.OrdinalIgnoreCase));
                if (start >= 0)
                {
                    cycles.Add([.. stack[start..], node]);
                }

                return;
            }

            visiting.Add(node);
            stack.Add(node);

            if (edges.TryGetValue(node, out var targets))
            {
                foreach (var target in targets)
                {
                    Visit(target);
                }
            }

            stack.RemoveAt(stack.Count - 1);
            visiting.Remove(node);
            visited.Add(node);
        }

        foreach (var node in edges.Keys)
        {
            Visit(node);
        }

        return cycles;
    }

    /// <summary>Dependencies first; ties broken by GUID so the order is stable.</summary>
    private static List<ResolvedPackage> TopologicalOrder(Dictionary<string, ResolvedPackage> nodes, Dictionary<string, List<string>> edges)
    {
        var ordered = new List<ResolvedPackage>(nodes.Count);
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Emit(string node)
        {
            if (!emitted.Add(node))
            {
                return;
            }

            if (edges.TryGetValue(node, out var targets))
            {
                foreach (var target in targets.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                {
                    Emit(target);
                }
            }

            if (nodes.TryGetValue(node, out var package))
            {
                ordered.Add(package with { Order = ordered.Count });
            }
        }

        foreach (var node in nodes.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            Emit(node);
        }

        return ordered;
    }

    private async Task<(
        string? InstancePath,
        List<InstalledPackage> Installed,
        Dictionary<string, IReadOnlyList<Dependency>> LocalDependencies
    )> LoadAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        return await unitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                var instance = await context
                    .Instances.Include(row => row.InstalledMods)
                    .AsNoTracking()
                    .SingleOrDefaultAsync(row => row.Id == instanceId, token);

                if (instance is null)
                {
                    return ((string?)null, new List<InstalledPackage>(), new Dictionary<string, IReadOnlyList<Dependency>>());
                }

                var installed = instance
                    .InstalledMods.Where(mod => mod.ParentModGuid is null)
                    .Select(mod => new InstalledPackage { Guid = mod.Guid, Version = mod.Version })
                    .ToList();

                var dependencies = await context.Dependencies.AsNoTracking().ToListAsync(token);
                var local = dependencies
                    .GroupBy(row => row.OwnerGuid, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => (IReadOnlyList<Dependency>)[.. group], StringComparer.OrdinalIgnoreCase);

                return (instance.Path, installed, local);
            },
            cancellationToken
        );
    }
}
