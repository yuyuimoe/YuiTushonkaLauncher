using System;
using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Services;

namespace YuiTushonkaLauncher.Tests.Ui;

/// <summary>
/// Avalonia refuses to show a window that was closed, and the manager and browser windows are opened from buttons the
/// user presses again. The slot is the part that remembers to forget: it is tested without a desktop, which is also
/// what keeps the test from depending on a message loop.
/// </summary>
public sealed class WindowSlotTests
{
    [Fact]
    public void TheSameInstanceIsReusedWhileItIsNotReleased()
    {
        var slot = new ReusableSlot<string>();
        var created = 0;

        var first = slot.GetOrCreate(() => "window " + ++created);
        var second = slot.GetOrCreate(() => "window " + ++created);

        second.Should().BeSameAs(first);
        created.Should().Be(1);
    }

    [Fact]
    public void AReleasedInstanceIsForgottenAndANewOneIsBuilt()
    {
        var slot = new ReusableSlot<string>();
        var created = 0;
        var first = slot.GetOrCreate(() => "window " + ++created);

        slot.Release(first);

        var second = slot.GetOrCreate(() => "window " + ++created);

        second.Should().NotBeSameAs(first);
        created.Should().Be(2);
        second.Should().Be("window 2");
    }

    [Fact]
    public void ReleasingSomethingElseLeavesTheCachedOneAlone()
    {
        var slot = new ReusableSlot<string>();
        var first = slot.GetOrCreate(() => "window 1");

        slot.Release("another window");

        slot.GetOrCreate(() => "window 2").Should().BeSameAs(first);
    }

    [Fact]
    public void ForgetDropsTheReferenceWithoutReleasingAnything()
    {
        var slot = new ReusableSlot<string>();
        var created = 0;
        slot.GetOrCreate(() => "window " + ++created);

        slot.Forget();

        slot.GetOrCreate(() => "window " + ++created).Should().Be("window 2");
    }
}
