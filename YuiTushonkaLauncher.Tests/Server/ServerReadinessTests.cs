using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Infrastructure.Server;

namespace YuiTushonkaLauncher.Tests.Server;

public sealed class ServerReadinessTests
{
    private readonly FastTimeProvider _time = new();

    private readonly FakeSptClient _client = new();

    private Task<TimeSpan> Wait(TimeSpan timeout, Func<bool>? alive = null, CancellationToken token = default, TimeSpan? attempt = null)
    {
        return ServerReadiness.WaitAsync(_client, timeout, alive ?? (() => true), _time, token, attempt);
    }

    [Fact]
    public async Task Wait_ReturnsAtOnce_WhenTheServerAnswersFirst()
    {
        var elapsed = await Wait(TimeSpan.FromSeconds(30));

        elapsed.Should().Be(TimeSpan.Zero);
        _client.PingCount.Should().Be(1);
    }

    [Fact]
    public async Task Wait_PollsWithGrowingPauses_UntilTheServerAnswers()
    {
        _client.FailPingsThenAnswer(3);

        var elapsed = await Wait(TimeSpan.FromSeconds(30));

        _client.PingCount.Should().Be(4);
        elapsed.Should().Be(TimeSpan.FromMilliseconds(100 + 200 + 400), "the pause starts at 100 ms and doubles");
    }

    [Fact]
    public async Task Wait_StopsDoublingTheWaitAtOneSecond()
    {
        _client.Up = false;

        var act = () => Wait(TimeSpan.FromSeconds(5));

        (await act.Should().ThrowAsync<ServerNotReadyException>()).Which.ProcessExited.Should().BeFalse();

        // Pauses 0.1 0.2 0.4 0.8 then 1 s each: nine pings before five seconds have passed. Without the cap it would be seven.
        _client.PingCount.Should().Be(9);
    }

    [Fact]
    public async Task Wait_SaysTheTimeoutInTheMessage()
    {
        _client.Up = false;

        var act = () => Wait(TimeSpan.FromSeconds(7));

        (await act.Should().ThrowAsync<ServerNotReadyException>()).Which.Message.Should().Contain("7 seconds");
    }

    [Fact]
    public async Task Wait_FailsAtOnce_WhenTheProcessIsAlreadyGone()
    {
        var act = () => Wait(TimeSpan.FromSeconds(30), alive: () => false);

        (await act.Should().ThrowAsync<ServerNotReadyException>()).Which.ProcessExited.Should().BeTrue();
        _client.PingCount.Should().Be(0, "no point asking a server that is not there");
    }

    [Fact]
    public async Task Wait_FailsAsSoonAsTheProcessDies_NotAtTheTimeout()
    {
        _client.Up = false;
        var checks = 0;

        var act = () => Wait(TimeSpan.FromMinutes(5), alive: () => ++checks < 3);

        (await act.Should().ThrowAsync<ServerNotReadyException>()).Which.ProcessExited.Should().BeTrue();
        _client.PingCount.Should().BeLessThan(5);
    }

    [Fact]
    public async Task Wait_DoesNotCountAPingFromAnotherServer()
    {
        // Our server exited (code 0, as the real one does on a busy port) but something else owns the port and answers.
        var checks = 0;

        var act = () => Wait(TimeSpan.FromSeconds(30), alive: () => ++checks == 1);

        (await act.Should().ThrowAsync<ServerNotReadyException>()).Which.ProcessExited.Should().BeTrue();
        _client.PingCount.Should().Be(1, "the ping was answered, and still did not count");
    }

    [Fact]
    public async Task Wait_MovesOn_WhenOneAttemptHangs()
    {
        var first = true;
        _client.PingBehavior = async token =>
        {
            if (first)
            {
                first = false;
                await Task.Delay(Timeout.Infinite, token);
            }

            return true;
        };

        var elapsed = await Wait(TimeSpan.FromSeconds(30), attempt: TimeSpan.FromMilliseconds(50));

        _client.PingCount.Should().Be(2);
        elapsed.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task Wait_HonoursCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        _client.PingBehavior = _ =>
        {
            cancellation.Cancel();
            return Task.FromResult(false);
        };

        var act = () => Wait(TimeSpan.FromSeconds(30), token: cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
