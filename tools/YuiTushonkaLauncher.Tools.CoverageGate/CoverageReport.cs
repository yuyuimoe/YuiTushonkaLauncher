using System.Globalization;
using System.Xml.Linq;

namespace YuiTushonkaLauncher.Tools.CoverageGate;

/// <summary>Line coverage of one assembly, as cobertura recorded it.</summary>
/// <param name="Assembly">Assembly name without the launcher prefix, e.g. <c>Core</c>.</param>
/// <param name="LineRate">Covered lines over total lines, 0..1.</param>
/// <param name="LinesValid">How many lines the report could see.</param>
/// <param name="LinesCovered">How many of them a test hit.</param>
public sealed record AssemblyCoverage(string Assembly, double LineRate, long LinesValid, long LinesCovered)
{
    /// <summary>The percentage a person reads, rounded to one decimal.</summary>
    public double Percent
    {
        get { return Math.Round(LineRate * 100, 1, MidpointRounding.AwayFromZero); }
    }
}

/// <summary>The result of applying the floor.</summary>
/// <param name="Assemblies">Every assembly found, best first.</param>
/// <param name="Failures">The assemblies below the floor.</param>
/// <param name="Floor">The floor that was applied, as a fraction.</param>
public sealed record CoverageVerdict(IReadOnlyList<AssemblyCoverage> Assemblies, IReadOnlyList<AssemblyCoverage> Failures, double Floor)
{
    public bool Passed
    {
        get { return Failures.Count == 0; }
    }
}

/// <summary>
/// Reads one or more cobertura files, keeps the best result per assembly and applies a floor. A gate needs a single
/// answer per assembly because a run can produce one file per test project.
/// </summary>
public static class CoverageReport
{
    /// <summary>Assemblies the floor applies to, by short name.</summary>
    public static readonly IReadOnlyList<string> Gated = ["Core", "Infrastructure"];

    /// <summary>
    /// Parses every <c>coverage.cobertura.xml</c> under <paramref name="paths" />. A path that is a file is read as is; a
    /// directory is searched.
    /// </summary>
    /// <exception cref="InvalidOperationException">No cobertura file was found.</exception>
    public static IReadOnlyList<AssemblyCoverage> Read(IEnumerable<string> paths)
    {
        var files = new List<string>();
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                files.Add(path);
            }
            else if (Directory.Exists(path))
            {
                files.AddRange(Directory.EnumerateFiles(path, "coverage.cobertura.xml", SearchOption.AllDirectories));
            }
        }

        if (files.Count == 0)
        {
            throw new InvalidOperationException("No coverage.cobertura.xml was found under: " + string.Join(", ", paths));
        }

        var best = new Dictionary<string, AssemblyCoverage>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.Distinct(StringComparer.Ordinal))
        {
            foreach (var assembly in ReadFile(file))
            {
                if (!best.TryGetValue(assembly.Assembly, out var existing) || assembly.LineRate > existing.LineRate)
                {
                    best[assembly.Assembly] = assembly;
                }
            }
        }

        return
        [
            .. best.Values.OrderByDescending(assembly => assembly.LineRate).ThenBy(assembly => assembly.Assembly, StringComparer.Ordinal),
        ];
    }

    /// <summary>Applies the floor to the assemblies it names; everything else is reported but not gated.</summary>
    public static CoverageVerdict Evaluate(IReadOnlyList<AssemblyCoverage> assemblies, double floor)
    {
        var failures = assemblies
            .Where(assembly => Gated.Contains(assembly.Assembly, StringComparer.OrdinalIgnoreCase) && assembly.LineRate < floor)
            .ToList();
        return new CoverageVerdict(assemblies, failures, floor);
    }

    /// <summary>The table the CI log shows.</summary>
    public static IReadOnlyList<string> Describe(CoverageVerdict verdict)
    {
        var lines = new List<string>();
        foreach (var assembly in verdict.Assemblies)
        {
            var gated = Gated.Contains(assembly.Assembly, StringComparer.OrdinalIgnoreCase);
            var mark = gated ? (assembly.LineRate >= verdict.Floor ? "ok" : "BELOW") : "n/a";
            lines.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"  {assembly.Assembly, -16} {assembly.Percent, 6:F1} %  {assembly.LinesCovered}/{assembly.LinesValid} lines  {mark}"
                )
            );
        }

        lines.Add(string.Create(CultureInfo.InvariantCulture, $"  floor {verdict.Floor * 100:F1} % on {string.Join(" and ", Gated)}"));
        return lines;
    }

    private static IEnumerable<AssemblyCoverage> ReadFile(string path)
    {
        var document = XDocument.Load(path);
        foreach (var package in document.Descendants("package"))
        {
            var name = package.Attribute("name")?.Value;
            var rate = package.Attribute("line-rate")?.Value;
            if (name is null || rate is null || !double.TryParse(rate, NumberStyles.Float, CultureInfo.InvariantCulture, out var lineRate))
            {
                continue;
            }

            var lines = package.Descendants("line");
            yield return new AssemblyCoverage(
                ShortName(name),
                Math.Clamp(lineRate, 0d, 1d),
                lines.Count(),
                lines.Count(line => Hits(line) > 0)
            );
        }
    }

    private static long Hits(XElement line)
    {
        return long.TryParse(line.Attribute("hits")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hits) ? hits : 0;
    }

    /// <summary><c>YuiTushonkaLauncher.Infrastructure</c> becomes <c>Infrastructure</c>.</summary>
    public static string ShortName(string assembly)
    {
        const string prefix = "YuiTushonkaLauncher.";
        return assembly.StartsWith(prefix, StringComparison.Ordinal) ? assembly[prefix.Length..] : assembly;
    }
}
