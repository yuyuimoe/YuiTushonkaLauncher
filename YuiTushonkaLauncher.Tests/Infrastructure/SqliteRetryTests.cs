using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using YuiTushonkaLauncher.Infrastructure.Persistence;

namespace YuiTushonkaLauncher.Tests.Infrastructure;

public sealed class SqliteRetryTests
{
    [Fact]
    public async Task ExecuteAsync_RetriesBusyErrorsAndThenSucceeds()
    {
        var attempts = 0;

        var result = await SqliteRetry.ExecuteAsync(() =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new SqliteException("database is locked", 5);
            }

            return Task.FromResult(42);
        });

        result.Should().Be(42);
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAsync_GivesUpAfterTheAttemptLimit()
    {
        var attempts = 0;

        var act = async () =>
            await SqliteRetry.ExecuteAsync(() =>
            {
                attempts++;
                throw new SqliteException("database is locked", 6);
            });

        await act.Should().ThrowAsync<SqliteException>();
        attempts.Should().Be(4);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotRetryUnrelatedFailures()
    {
        var attempts = 0;

        var act = async () =>
            await SqliteRetry.ExecuteAsync(() =>
            {
                attempts++;
                throw new SqliteException("no such table", 1);
            });

        await act.Should().ThrowAsync<SqliteException>();
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task VoidOverload_PropagatesSuccess()
    {
        var ran = false;

        await SqliteRetry.ExecuteAsync(() =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        ran.Should().BeTrue();
    }
}
