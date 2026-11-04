using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Runtime;

namespace YuiTushonkaLauncher.Infrastructure.Runtime;

/// <summary>Creates and checks Wine prefixes. Never deletes a prefix, and never touches a folder that is not one.</summary>
public sealed class PrefixService(
    IRuntimeLauncher launcher,
    IProcessRunner runner,
    ILogger<PrefixService> logger,
    TimeSpan? settleTimeout = null
) : IPrefixService
{
    /// <summary><c>wineboot</c> can take minutes the first time: it unpacks a whole Windows tree and registry.</summary>
    private static readonly TimeSpan InitializeTimeout = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan SettleInterval = TimeSpan.FromMilliseconds(250);

    private readonly TimeSpan _settleTimeout = settleTimeout ?? TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public PrefixInspection Inspect(string prefixPath)
    {
        return PrefixInspector.Inspect(prefixPath);
    }

    /// <inheritdoc />
    public async Task<PrefixInitResult> InitializeAsync(
        Instance instance,
        RuntimeProfile profile,
        IProgress<ProcessLine>? output = null,
        RuntimeSearchOptions? search = null,
        CancellationToken cancellationToken = default
    )
    {
        var plan = launcher.PlanTool(instance, profile, new RuntimeToolRequest { Tool = PrefixTool.Initialize }, search);
        var prefix = plan.PrefixPath ?? launcher.ResolvePrefix(instance, profile);

        if (!plan.CanRun || plan.Command is null)
        {
            return new PrefixInitResult
            {
                Succeeded = false,
                Prefix = PrefixInspector.Inspect(prefix),
                Problems = plan.Problems,
            };
        }

        Directory.CreateDirectory(prefix);
        logger.LogInformation("Initializing the Wine prefix {Prefix}: {Command}", prefix, plan.Command.Describe());

        // Wine's output is logged, never echoed to the caller's thread: it is verbose and arrives from other threads.
        var sink = new Progress<ProcessLine>(line =>
        {
            logger.LogInformation("[wine] {Line}", line.Text);
        });
        var forward = new TeeProgress(sink, output);
        ProcessRunResult run;

        try
        {
            run = await runner.RunAsync(plan.Command, forward, InitializeTimeout, cancellationToken);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogError(exception, "The prefix tool did not start");

            return new PrefixInitResult
            {
                Succeeded = false,
                Command = plan.Command,
                Prefix = PrefixInspector.Inspect(prefix),
                Problems =
                [
                    .. plan.Problems,
                    new RuntimeProblem(
                        RuntimeProblemSeverity.Error,
                        RuntimeProblemCode.ToolMissing,
                        exception.Message,
                        plan.Command.FileName
                    ),
                ],
            };
        }

        var inspection = run is { ExitCode: 0, TimedOut: false }
            ? await WaitForRegistryAsync(prefix, cancellationToken)
            : PrefixInspector.Inspect(prefix);

        return new PrefixInitResult
        {
            Succeeded = run is { ExitCode: 0, TimedOut: false } && inspection.State == PrefixState.Initialized,
            Command = plan.Command,
            ExitCode = run.TimedOut ? null : run.ExitCode,
            Prefix = inspection,
            Problems = plan.Problems,
        };
    }

    /// <summary>
    /// <c>wineboot</c> exits before Wine's server has written <c>system.reg</c> and <c>user.reg</c>: the server
    /// saves them when it goes idle, seconds later. Judging the prefix at once would call a good one broken, so
    /// wait (bounded) for the registry to appear.
    /// </summary>
    private async Task<PrefixInspection> WaitForRegistryAsync(string prefix, CancellationToken cancellationToken)
    {
        var inspection = PrefixInspector.Inspect(prefix);
        var deadline = DateTime.UtcNow + _settleTimeout;

        while (inspection.State != PrefixState.Initialized && DateTime.UtcNow < deadline)
        {
            await Task.Delay(SettleInterval, cancellationToken);
            inspection = PrefixInspector.Inspect(prefix);
        }

        return inspection;
    }

    private sealed class TeeProgress(IProgress<ProcessLine> first, IProgress<ProcessLine>? second) : IProgress<ProcessLine>
    {
        public void Report(ProcessLine value)
        {
            first.Report(value);
            second?.Report(value);
        }
    }
}
