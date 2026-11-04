using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Infrastructure.Persistence;

namespace YuiTushonkaLauncher.Tests.Infrastructure;

public sealed class UnitOfWorkTests
{
    [Fact]
    public async Task ExecuteAsync_CommitsEverythingOnSuccess()
    {
        await using var database = new TestDatabase();
        await database.InitializeAsync();

        await database.UnitOfWork.ExecuteAsync(
            async (context, _) =>
            {
                context.SptReleases.Add(CreateRelease("4.1.6"));
                await Task.CompletedTask;
            }
        );

        await using var verification = database.CreateContext();
        (await verification.SptReleases.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_RollsBackWhenTheWorkThrows()
    {
        await using var database = new TestDatabase();
        await database.InitializeAsync();

        var act = async () =>
            await database.UnitOfWork.ExecuteAsync(
                async (context, _) =>
                {
                    context.SptReleases.Add(CreateRelease("4.1.6"));
                    await context.SaveChangesAsync();
                    throw new InvalidOperationException("install failed");
                }
            );

        await act.Should().ThrowAsync<InvalidOperationException>();

        await using var verification = database.CreateContext();
        (await verification.SptReleases.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsTheWorkResult()
    {
        await using var database = new TestDatabase();
        await database.InitializeAsync();

        var count = await database.UnitOfWork.ExecuteAsync(
            async (context, _) =>
            {
                context.SptReleases.Add(CreateRelease("4.1.6"));
                await Task.CompletedTask;
                return 42;
            }
        );

        count.Should().Be(42);
    }

    private static SptRelease CreateRelease(string version)
    {
        return new SptRelease
        {
            Id = Guid.NewGuid(),
            ForgeId = 55,
            Version = SemanticVersion.Parse(version),
            ModCount = 738,
            ReleaseUrl = "https://github.com/SP-Tushonka/build/releases/tag/4.1.6",
            IsCurrent = true,
            FetchedAt = DateTimeOffset.UtcNow,
        };
    }
}
