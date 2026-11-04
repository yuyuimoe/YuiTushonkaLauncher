using Microsoft.Extensions.DependencyInjection;
using Serilog;
using YuiTushonkaLauncher.Core.Launch;

namespace YuiTushonkaLauncher;

/// <summary>Headless command for the launch pipeline (phase 11, 11.2).</summary>
internal static partial class HeadlessCommands
{
    /// <summary>
    /// Runs the whole pipeline for an instance (<c>--launch NAME [--profile USER] [--remember] [--no-patch]
    /// [--game-program PATH]</c>). Without <c>--apply</c>-style flags it really starts things.
    /// </summary>
    public const string Launch = "--launch";

    /// <summary>Checks everything a launch needs and starts nothing (<c>--launch-plan NAME [--profile USER]</c>).</summary>
    public const string LaunchPlan = "--launch-plan";

    private static bool LaunchCommandRequested(string[] args)
    {
        return IndexOf(args, Launch) >= 0 || IndexOf(args, LaunchPlan) >= 0;
    }

    private static void RunLaunchCommands(IServiceProvider services, string[] args)
    {
        var planOnly = IndexOf(args, LaunchPlan) >= 0;
        var index = planOnly ? IndexOf(args, LaunchPlan) : IndexOf(args, Launch);
        var name = ValueAfter(args, index);
        var instance = FindInstance(services, name);
        var service = services.GetRequiredService<ILaunchService>();

        var request = new LaunchRequest
        {
            InstanceId = instance.Id,
            ProfileUsername = NullIfEmpty(ValueOf(args, "--profile")),
            RememberProfile = args.Contains("--remember", StringComparer.Ordinal),
            SkipPatch = args.Contains("--no-patch", StringComparer.Ordinal),
            GameProgramOverride = NullIfEmpty(ValueOf(args, "--game-program")),
        };

        var plan = service.PlanAsync(request).GetAwaiter().GetResult();

        Log.Information("Launch plan for '{Instance}':", plan.InstanceName);
        Log.Information("  profile: {Profile}", plan.ProfileUsername ?? "NOT CHOSEN");
        Log.Information("  patched: {Patched}", plan.PatchAlreadyApplied ? "yes" : "no (the pipeline will patch)");
        Log.Information(
            "  server: {Server}",
            plan.ServerAlreadyRunning ? "already running, it will be adopted" : "not running, it will be started"
        );
        Log.Information("  game: {Command}", plan.GameCommand?.Describe() ?? "none");

        foreach (var problem in plan.Problems)
        {
            Log.Error("  {Code}: {Message}", problem.Code, problem.Message);
        }

        if (planOnly)
        {
            if (!plan.CanLaunch)
            {
                throw new LaunchException("The launch cannot proceed; see above.", plan.Problems);
            }

            Log.Information("  ready to launch");
            return;
        }

        var progress = new StepLogger();
        var result = service.LaunchAsync(request, progress).GetAwaiter().GetResult();

        Log.Information(
            "Launched '{Instance}': server pid {Server}, game pid {Game}, profile {Profile}, {Seconds:0.0} s",
            plan.InstanceName,
            result.ServerProcessId?.ToString() ?? "-",
            result.GameProcessId?.ToString() ?? "-",
            result.ProfileId ?? "-",
            result.Took?.TotalSeconds ?? 0
        );
        Log.Information("Both processes are detached: closing the launcher leaves them running (--stale lists them).");
    }

    /// <summary>Writes one line per step, and a line per patching progress tick.</summary>
    private sealed class StepLogger : IProgress<LaunchProgress>
    {
        private LaunchStep? _current;

        public void Report(LaunchProgress value)
        {
            if (value.Outcome == LaunchStepOutcome.Running && value.Progress is null)
            {
                if (_current != value.Step)
                {
                    _current = value.Step;
                    Log.Information("  {Step}...", value.Step);
                }

                return;
            }

            if (value.Outcome == LaunchStepOutcome.Running)
            {
                Log.Debug("  {Step}: {Message}", value.Step, value.Message);
                return;
            }

            Log.Information("  {Step}: {Outcome} {Message}", value.Step, value.Outcome, value.Message);
        }
    }
}
