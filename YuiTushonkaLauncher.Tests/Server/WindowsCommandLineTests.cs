using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Infrastructure.Server;

namespace YuiTushonkaLauncher.Tests.Server;

/// <summary>
/// <see cref="WindowsCommandLine" /> is pure string work, so its quoting rules are checked everywhere the suite runs, not
/// only on a Windows machine. The expected strings are what <c>CommandLineToArgvW</c> reads back, so a regression here is a
/// child that gets a different argument than the launcher meant to pass.
/// </summary>
public sealed class WindowsCommandLineTests
{
    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("no-spaces", "no-spaces")]
    [InlineData("C:\\path\\to\\app.exe", "C:\\path\\to\\app.exe")]
    public void Quote_LeavesAnArgumentAlone_WhenItNeedsNoQuotes(string argument, string expected)
    {
        WindowsCommandLine.Quote(argument).Should().Be(expected);
    }

    [Theory]
    [InlineData("with space", "\"with space\"")]
    [InlineData("with\ttab", "\"with\ttab\"")]
    [InlineData("C:\\Program Files\\App\\app.exe", "\"C:\\Program Files\\App\\app.exe\"")]
    public void Quote_WrapsAnArgumentInQuotes_WhenItHasWhitespace(string argument, string expected)
    {
        WindowsCommandLine.Quote(argument).Should().Be(expected);
    }

    [Fact]
    public void Quote_QuotesTheEmptyString()
    {
        WindowsCommandLine.Quote(string.Empty).Should().Be("\"\"");
    }

    [Fact]
    public void Quote_EscapesAQuoteInTheMiddle()
    {
        WindowsCommandLine.Quote("with \"quote\"").Should().Be("\"with \\\"quote\\\"\"");
    }

    [Fact]
    public void Quote_DoublesBackslashesThatPrecedeAQuote()
    {
        WindowsCommandLine.Quote("a\\\"b").Should().Be("\"a\\\\\\\"b\"");
    }

    [Fact]
    public void Quote_DoublesBackslashesAtTheEnd()
    {
        WindowsCommandLine.Quote("ends with \\").Should().Be("\"ends with \\\\\"");
    }

    [Fact]
    public void Build_QuotesTheFileNameAndEveryArgument()
    {
        var command = new LaunchCommand { FileName = "C:\\Program Files\\App\\app.exe", Arguments = ["--flag", "with space", "a\\\"b"] };

        WindowsCommandLine.Build(command).Should().Be("\"C:\\Program Files\\App\\app.exe\" --flag \"with space\" \"a\\\\\\\"b\"");
    }

    [Fact]
    public void Build_UsesTheFileNameOnly_WhenThereAreNoArguments()
    {
        var command = new LaunchCommand { FileName = "app.exe" };

        WindowsCommandLine.Build(command).Should().Be("app.exe");
    }
}
