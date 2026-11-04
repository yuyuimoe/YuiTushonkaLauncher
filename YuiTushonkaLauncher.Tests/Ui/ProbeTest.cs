using FluentAssertions;
using Xunit;

namespace YuiTushonkaLauncher.Tests.Ui;

public sealed class ProbeTest
{
    [Fact]
    public void AvaloniaStartsWithNothingElse()
    {
        Headless.EnsureStarted();
        Avalonia.Application.Current.Should().NotBeNull();
    }
}
