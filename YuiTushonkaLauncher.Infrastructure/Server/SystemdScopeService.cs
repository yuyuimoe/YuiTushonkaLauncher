using YuiTushonkaLauncher.Core.Runtime;

namespace YuiTushonkaLauncher.Infrastructure.Server;

/// <summary>
/// Speaks to <c>systemctl --user</c> about the transient units a launch creates. A scope is the only identity that
/// survives a program that forks and exits (umu, Proton and the game itself all do), so the unit name is what the
/// run record keeps.
/// </summary>
public sealed class SystemdScopeService(IExecutableFinder finder, IProcessRunner runner) : IScopeService
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    public bool IsActive(string unit)
    {
        var systemctl = finder.Find("systemctl");

        if (systemctl is null || !IsOurs(unit))
        {
            // Only units this launcher named are ever touched.
            return false;
        }

        // The suffix matters: without it systemd looks for "<name>.service" and reports a scope as inactive.
        var result = runner
            .RunAsync(
                new LaunchCommand { FileName = systemctl, Arguments = ["--user", "is-active", "--quiet", Named(unit)] },
                timeout: QueryTimeout
            )
            .GetAwaiter()
            .GetResult();

        return result.ExitCode == 0;
    }

    private static bool IsOurs(string unit)
    {
        return unit.StartsWith("ytt-", StringComparison.Ordinal);
    }

    private static string Named(string unit)
    {
        return unit.EndsWith(".scope", StringComparison.Ordinal) ? unit : unit + ".scope";
    }

    /// <inheritdoc />
    public async Task<bool> StopAsync(string unit, TimeSpan grace, CancellationToken cancellationToken = default)
    {
        var systemctl = finder.Find("systemctl");

        if (systemctl is null || !IsOurs(unit))
        {
            return false;
        }

        await runner.RunAsync(
            new LaunchCommand { FileName = systemctl, Arguments = ["--user", "kill", "--signal=TERM", Named(unit)] },
            timeout: QueryTimeout,
            cancellationToken: cancellationToken
        );

        var deadline = DateTimeOffset.UtcNow + grace;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (!IsActive(unit))
            {
                return true;
            }

            await Task.Delay(100, cancellationToken);
        }

        await runner.RunAsync(
            new LaunchCommand { FileName = systemctl, Arguments = ["--user", "kill", "--signal=KILL", Named(unit)] },
            timeout: QueryTimeout,
            cancellationToken: cancellationToken
        );

        return !IsActive(unit);
    }
}
