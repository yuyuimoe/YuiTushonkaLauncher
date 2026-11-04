using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Infrastructure.Processes;

namespace YuiTushonkaLauncher.Tests.Infrastructure;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void TryAcquire_SucceedsForTheFirstGuard()
    {
        using var temp = new TempDirectory();
        var paths = temp.CreateAppPaths();
        paths.EnsureCreated();

        using var guard = new SingleInstanceGuard(paths, NullLogger<SingleInstanceGuard>.Instance);

        guard.TryAcquire().Should().BeTrue();
    }

    [Fact]
    public void TryAcquire_IsIdempotent()
    {
        using var temp = new TempDirectory();
        var paths = temp.CreateAppPaths();
        paths.EnsureCreated();

        using var guard = new SingleInstanceGuard(paths, NullLogger<SingleInstanceGuard>.Instance);

        guard.TryAcquire().Should().BeTrue();
        guard.TryAcquire().Should().BeTrue();
    }

    [Fact]
    public void SecondGuard_FailsWhileTheFirstHoldsTheLock_AndSucceedsAfterRelease()
    {
        using var temp = new TempDirectory();
        var paths = temp.CreateAppPaths();
        paths.EnsureCreated();

        var first = new SingleInstanceGuard(paths, NullLogger<SingleInstanceGuard>.Instance);
        first.TryAcquire().Should().BeTrue();

        using (var second = new SingleInstanceGuard(paths, NullLogger<SingleInstanceGuard>.Instance))
        {
            second.TryAcquire().Should().BeFalse();
        }

        first.Dispose();

        using var third = new SingleInstanceGuard(paths, NullLogger<SingleInstanceGuard>.Instance);
        third.TryAcquire().Should().BeTrue();
    }

    [Fact]
    public void Dispose_CanBeCalledTwice()
    {
        using var temp = new TempDirectory();
        var paths = temp.CreateAppPaths();
        paths.EnsureCreated();

        var guard = new SingleInstanceGuard(paths, NullLogger<SingleInstanceGuard>.Instance);
        guard.TryAcquire().Should().BeTrue();

        guard.Dispose();
        guard.Dispose();
    }
}
