using System.Globalization;
using YuiTushonkaLauncher.Tools.CoverageGate;

var paths = new List<string>();
var floor = 0.90;
for (var index = 0; index < args.Length; index++)
{
    switch (args[index])
    {
        case "--report" when index + 1 < args.Length:
            paths.Add(args[++index]);
            break;
        case "--min" when index + 1 < args.Length:
            floor = double.Parse(args[++index], NumberStyles.Float, CultureInfo.InvariantCulture) / 100d;
            break;
        default:
            paths.Add(args[index]);
            break;
    }
}

if (paths.Count == 0)
{
    paths.Add("TestResults");
}

IReadOnlyList<AssemblyCoverage> assemblies;
try
{
    assemblies = CoverageReport.Read(paths);
}
catch (InvalidOperationException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

var verdict = CoverageReport.Evaluate(assemblies, floor);
foreach (var line in CoverageReport.Describe(verdict))
{
    Console.WriteLine(line);
}

if (verdict.Passed)
{
    Console.WriteLine("Coverage floor met.");
    return 0;
}

foreach (var failure in verdict.Failures)
{
    Console.Error.WriteLine($"FAIL: {failure.Assembly} is at {failure.Percent:F1} %, below the {floor * 100:F1} % floor.");
}

return 1;
