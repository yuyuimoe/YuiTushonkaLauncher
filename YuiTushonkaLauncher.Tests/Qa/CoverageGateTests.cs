using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Tests.Infrastructure;
using YuiTushonkaLauncher.Tools.CoverageGate;

namespace YuiTushonkaLauncher.Tests.Qa;

/// <summary>
/// The gate itself: a floor that cannot fail is worse than no floor, so the parser, the merge rule and the verdict each
/// have a test.
/// </summary>
public sealed class CoverageGateTests
{
    private static string Report(params (string Assembly, double Rate, int Lines, int Covered)[] packages)
    {
        var packagesXml = string.Join(
            "\n",
            packages.Select(package =>
                $"""
                      <package name="{package.Assembly}" line-rate="{package.Rate.ToString(
                        System.Globalization.CultureInfo.InvariantCulture
                    )}" branch-rate="0">
                        {string.Concat(
                        Enumerable.Range(0, package.Lines).Select(index =>
                            $"""<line number="{index + 1}" hits="{(index < package.Covered ? 1 : 0)}" />"""
                        )
                    )}
                      </package>
                    """
            )
        );

        return $"""<?xml version="1.0" encoding="utf-8"?><coverage line-rate="0.9"><packages>{packagesXml}</packages></coverage>""";
    }

    [Fact]
    public void APassingReportPasses()
    {
        using var temp = new TempDirectory();
        var path = Path.Join(temp.Path, "coverage.cobertura.xml");
        File.WriteAllText(path, Report(("YuiTushonkaLauncher.Core", 0.95, 100, 95), ("YuiTushonkaLauncher.Infrastructure", 0.91, 100, 91)));

        var verdict = CoverageReport.Evaluate(CoverageReport.Read([path]), 0.90);

        verdict.Passed.Should().BeTrue();
        verdict.Assemblies.Should().HaveCount(2);
        verdict.ShortNames().Should().BeEquivalentTo("Core", "Infrastructure");
    }

    [Fact]
    public void AnAssemblyBelowTheGateFailsTheRun()
    {
        var assemblies = new[] { new AssemblyCoverage("Core", 0.95, 100, 95), new AssemblyCoverage("Infrastructure", 0.72, 100, 72) };

        var verdict = CoverageReport.Evaluate(assemblies, 0.90);

        verdict.Passed.Should().BeFalse();
        verdict.Failures.Should().ContainSingle().Which.Assembly.Should().Be("Infrastructure");
        string.Join(" ", CoverageReport.Describe(verdict)).Should().Contain("BELOW");
    }

    [Fact]
    public void OnlyCoreAndInfrastructureAreGated()
    {
        // Patching and the UI are reported; a low number there must not fail the build (their coverage is a separate
        // conversation, and Patching depends on a native patch library that cannot run everywhere).
        var assemblies = new[]
        {
            new AssemblyCoverage("Core", 0.99, 100, 99),
            new AssemblyCoverage("Infrastructure", 0.99, 100, 99),
            new AssemblyCoverage("Patching", 0.10, 100, 10),
            new AssemblyCoverage("Ui", 0.05, 100, 5),
        };

        CoverageReport.Evaluate(assemblies, 0.90).Passed.Should().BeTrue();
    }

    [Fact]
    public void TheBestResultPerAssemblyWins_WhenARunWritesSeveralFiles()
    {
        using var temp = new TempDirectory();
        var first = Path.Join(temp.Path, "a", "coverage.cobertura.xml");
        var second = Path.Join(temp.Path, "b", "coverage.cobertura.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(first)!);
        Directory.CreateDirectory(Path.GetDirectoryName(second)!);
        File.WriteAllText(first, Report(("YuiTushonkaLauncher.Core", 0.55, 100, 55)));
        File.WriteAllText(second, Report(("YuiTushonkaLauncher.Core", 0.97, 100, 97)));

        var assemblies = CoverageReport.Read([temp.Path]);

        assemblies.Should().ContainSingle().Which.LineRate.Should().Be(0.97);
    }

    [Fact]
    public void ADirectoryWithNoReportIsAnError_NotAPass()
    {
        using var temp = new TempDirectory();
        var read = () => CoverageReport.Read([temp.Path]);

        read.Should().Throw<InvalidOperationException>().WithMessage("*No coverage.cobertura.xml*");
    }

    [Fact]
    public void LineCountsComeFromTheLinesTheReportListed()
    {
        using var temp = new TempDirectory();
        var path = Path.Join(temp.Path, "coverage.cobertura.xml");
        File.WriteAllText(path, Report(("YuiTushonkaLauncher.Core", 0.5, 20, 10)));

        var core = CoverageReport.Read([path]).Single();

        core.LinesValid.Should().Be(20);
        core.LinesCovered.Should().Be(10);
        core.Percent.Should().Be(50.0);
    }

    [Fact]
    public void ShortNamesDropOnlyTheLauncherPrefix()
    {
        CoverageReport.ShortName("YuiTushonkaLauncher.Infrastructure").Should().Be("Infrastructure");
        CoverageReport.ShortName("Something.Else").Should().Be("Something.Else");
    }
}

/// <summary>A readable projection for assertions.</summary>
internal static class CoverageVerdictAssertionsExtensions
{
    public static IEnumerable<string> ShortNames(this CoverageVerdict verdict)
    {
        return verdict.Assemblies.Select(assembly => assembly.Assembly);
    }
}
