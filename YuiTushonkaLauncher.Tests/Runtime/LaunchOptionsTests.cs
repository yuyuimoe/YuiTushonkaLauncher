using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Runtime;

namespace YuiTushonkaLauncher.Tests.Runtime;

public sealed class LaunchOptionsTests
{
    [Fact]
    public void Tokenize_SplitsOnWhitespaceAndKeepsQuotedRunsTogether()
    {
        LaunchOptions
            .Tokenize("""MANGOHUD=1 -arg="/path with spaces" WINEDLLOVERRIDES="d3d11=n,b" plain""")
            .Should()
            .Equal("MANGOHUD=1", "-arg=/path with spaces", "WINEDLLOVERRIDES=d3d11=n,b", "plain");
    }

    [Fact]
    public void Tokenize_AcceptsSingleQuotesEscapesAndUnterminatedQuotes()
    {
        LaunchOptions.Tokenize("'a b' c\\\"d").Should().Equal("a b", "c\"d");
        LaunchOptions.Tokenize("-x=\"never closed").Should().Equal("-x=never closed");
    }

    [Fact]
    public void Tokenize_KeepsAnEmptyQuotedValueAsAToken()
    {
        LaunchOptions.Tokenize("EMPTY=\"\" \"\"").Should().Equal("EMPTY=", "");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t ")]
    public void Tokenize_OfNothingIsEmpty(string? line)
    {
        LaunchOptions.Tokenize(line).Should().BeEmpty();
    }

    [Fact]
    public void Parse_SeparatesVariablesFromArguments()
    {
        var parsed = LaunchOptions.Parse("DXVK_HUD=fps --disable-software-renderer -token=abc loose");

        parsed.Environment.Should().Equal(new Dictionary<string, string> { ["DXVK_HUD"] = "fps" });
        parsed.Arguments.Should().Equal("--disable-software-renderer", "-token=abc", "loose");
    }

    [Fact]
    public void Parse_LetsARepeatedNameOverwriteInsteadOfThrowing()
    {
        LaunchOptions.Parse("A=1 A=2").Environment["A"].Should().Be("2");
    }

    [Fact]
    public void Parse_KeepsEqualsSignsInsideTheValue()
    {
        LaunchOptions.Parse("WINEDLLOVERRIDES=winhttp=n,b").Environment["WINEDLLOVERRIDES"].Should().Be("winhttp=n,b");
    }

    [Fact]
    public void Format_RoundTripsThroughParse()
    {
        var environment = new Dictionary<string, string> { ["B"] = "two words", ["A"] = "1" };
        var line = LaunchOptions.Format(environment, ["--flag", "a b"]);
        var parsed = LaunchOptions.Parse(line);

        line.Should().StartWith("A=1 B=");
        parsed.Environment.Should().BeEquivalentTo(environment);
        parsed.Arguments.Should().Equal("--flag", "a b");
    }

    [Fact]
    public void ValidateEnvironment_AcceptsOrdinaryVariables()
    {
        LaunchOptions
            .ValidateEnvironment(
                new Dictionary<string, string>
                {
                    ["DXVK_HUD"] = "fps",
                    ["_X1"] = "",
                    ["WINEDLLOVERRIDES"] = "winhttp=n,b",
                }
            )
            .Should()
            .BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("1ABC")]
    [InlineData("A-B")]
    [InlineData("A B")]
    [InlineData("A=B")]
    [InlineData("é")]
    public void ValidateEnvironment_RejectsNamesThatAreNotPortableIdentifiers(string name)
    {
        LaunchOptions
            .ValidateEnvironment(new Dictionary<string, string> { [name] = "x" })
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("not a valid variable name");
    }

    [Theory]
    [InlineData("WINEPREFIX")]
    [InlineData("PROTONPATH")]
    [InlineData("GAMEID")]
    [InlineData("STORE")]
    [InlineData("STEAM_COMPAT_DATA_PATH")]
    public void ValidateEnvironment_RejectsVariablesTheLauncherOwns(string name)
    {
        LaunchOptions
            .ValidateEnvironment(new Dictionary<string, string> { [name] = "/x" })
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("set by the launcher");
    }

    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\rb")]
    [InlineData("a\0b")]
    public void ValidateEnvironment_RejectsValuesWithControlCharacters(string value)
    {
        LaunchOptions
            .ValidateEnvironment(new Dictionary<string, string> { ["A"] = value })
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("line break or a NUL");
    }

    [Fact]
    public void ValidateEnvironment_CapsTheCountAndTheValueLength()
    {
        var many = Enumerable.Range(0, LaunchOptions.MaxVariables + 1).ToDictionary(index => "V" + index, _ => "x");
        var longValue = new Dictionary<string, string> { ["A"] = new string('x', LaunchOptions.MaxValueLength + 1) };

        LaunchOptions.ValidateEnvironment(many).Should().ContainSingle().Which.Should().Contain("At most");
        LaunchOptions.ValidateEnvironment(longValue).Should().ContainSingle().Which.Should().Contain("longer than");
    }

    [Fact]
    public void ValidateEnvironment_TruncatesAHugeNameInTheMessage()
    {
        var problem = LaunchOptions.ValidateEnvironment(new Dictionary<string, string> { [new string('-', 500)] = "x" }).Single();

        problem.Length.Should().BeLessThan(200);
    }

    [Fact]
    public void ValidateArguments_RejectsLineBreaksAndNul()
    {
        LaunchOptions.ValidateArguments(["ok", "a\nb", "c\0"]).Should().HaveCount(2);
        LaunchOptions.ValidateArguments(["fine", "also fine"]).Should().BeEmpty();
    }

    [Theory]
    [InlineData("winhttp=n,b", true)]
    [InlineData("winhttp=n", true)]
    [InlineData("WINHTTP=n,b", true)]
    [InlineData("d3d11=n;winhttp=n,b", true)]
    [InlineData("a,winhttp=n,b", true)]
    [InlineData("winhttp,d3d11=n,b", true)]
    [InlineData("winhttp=b,n", false)]
    [InlineData("winhttp=b", false)]
    [InlineData("winhttp=d", false)]
    [InlineData("winhttp", false)]
    [InlineData("d3d11=n,b", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OverridesWinhttp_RecognisesNativeFirstOnly(string? overrides, bool expected)
    {
        LaunchOptions.OverridesWinhttp(overrides).Should().Be(expected);
    }
}
