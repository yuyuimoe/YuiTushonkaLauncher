using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Localization;

namespace YuiTushonkaLauncher.Tests.Localization;

public sealed class LocaleBundleTests
{
    [Fact]
    public void NestedKeysBecomeDottedOnes()
    {
        var table = LocaleBundle.Flatten("""{ "instances": { "title": "Instances", "create": { "button": "Create" } } }""");

        table
            .Should()
            .BeEquivalentTo(new Dictionary<string, string> { ["instances.title"] = "Instances", ["instances.create.button"] = "Create" });
    }

    [Fact]
    public void NonStringValuesAreIgnored()
    {
        var table = LocaleBundle.Flatten("""{ "a": "text", "b": 3, "c": true, "d": ["x"], "e": null }""");

        table.Should().ContainSingle().Which.Key.Should().Be("a");
    }

    [Fact]
    public void ANonObjectDocumentIsRefused()
    {
        var flatten = () => LocaleBundle.Flatten("""["x"]""");

        flatten.Should().Throw<System.Text.Json.JsonException>();
    }

    [Fact]
    public void FallbackFillsTheGapsAndKeepsTheTranslation()
    {
        var merged = LocaleBundle.WithFallback(
            new Dictionary<string, string> { ["a"] = "traduzido" },
            new Dictionary<string, string> { ["a"] = "english", ["b"] = "english only" }
        );

        merged.Should().BeEquivalentTo(new Dictionary<string, string> { ["a"] = "traduzido", ["b"] = "english only" });
    }

    [Fact]
    public void DifferencesNameBothSides()
    {
        LocaleBundle
            .Differences(
                new Dictionary<string, string> { ["a"] = "", ["b"] = "" },
                new Dictionary<string, string> { ["b"] = "", ["c"] = "" }
            )
            .Should()
            .Equal("a", "c");
    }

    [Theory]
    [InlineData("{0} mods", new[] { "{0}" })]
    [InlineData("{name} is {count}", new[] { "{count}", "{name}" })]
    [InlineData("{0,-10:F2}", new[] { "{0}" })]
    [InlineData("no placeholders", new string[0])]
    [InlineData("a { b", new string[0])]
    public void PlaceholdersAreReadWithoutTheirFormatting(string value, string[] expected)
    {
        LocaleBundle.Placeholders(value).Should().Equal(expected);
    }

    [Fact]
    public void ARealSptLocaleParsesIntoAFlatTable()
    {
        // SPT's own shape: nested objects with a few non-string values mixed in.
        var table = LocaleBundle.Flatten(
            """
            {
              "launcher": { "menu": { "singleplayer": "Singleplayer", "quit": "Quit" } },
              "version": "4.1",
              "banks": ["x"]
            }
            """
        );

        table.Should().ContainKeys("launcher.menu.singleplayer", "launcher.menu.quit", "version");
    }
}
