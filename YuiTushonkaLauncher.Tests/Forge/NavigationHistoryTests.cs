using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Forge;

namespace YuiTushonkaLauncher.Tests.Forge;

/// <summary>
/// Selection in the browser is navigation: a dependency opens another mod, and the user must be able to come back.
/// </summary>
public sealed class NavigationHistoryTests
{
    [Fact]
    public void FreshHistoryGoesNowhere()
    {
        var history = new NavigationHistory<string>();

        history.CanGoBack.Should().BeFalse();
        history.CanGoForward.Should().BeFalse();
        history.Back().Should().BeNull();
        history.Forward().Should().BeNull();
    }

    [Fact]
    public void PushThenBackThenForwardWalksTheSamePath()
    {
        var history = new NavigationHistory<string>();
        history.Push("a");
        history.Push("b");
        history.Push("c");

        history.Current.Should().Be("c");
        history.Back().Should().Be("b");
        history.Back().Should().Be("a");
        history.CanGoBack.Should().BeFalse();
        history.Forward().Should().Be("b");
        history.Forward().Should().Be("c");
        history.CanGoForward.Should().BeFalse();
    }

    [Fact]
    public void ANewSelectionDropsTheForwardStack()
    {
        var history = new NavigationHistory<string>();
        history.Push("a");
        history.Push("b");
        history.Back();

        history.Push("c");

        history.CanGoForward.Should().BeFalse();
        history.Current.Should().Be("c");
    }

    [Fact]
    public void PushingWhatIsAlreadyShownDoesNothing()
    {
        var history = new NavigationHistory<string>();
        history.Push("a");

        history.Push("a").Should().BeFalse();
        history.Count.Should().Be(1);
    }

    [Fact]
    public void ClearForgetsEverything()
    {
        var history = new NavigationHistory<string>();
        history.Push("a");
        history.Push("b");

        history.Clear();

        history.Current.Should().BeNull();
        history.Count.Should().Be(0);
    }
}
