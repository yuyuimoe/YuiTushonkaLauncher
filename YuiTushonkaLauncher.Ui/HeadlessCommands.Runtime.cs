using Microsoft.Extensions.DependencyInjection;
using Serilog;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Runtime;

namespace YuiTushonkaLauncher;

/// <summary>Headless commands for the Linux runtime layer (phase 9).</summary>
internal static partial class HeadlessCommands
{
    /// <summary>Lists the Proton builds, <c>umu-run</c>, Wine and GameMode found on this machine.</summary>
    public const string Runtimes = "--runtimes";

    /// <summary>
    /// Creates a runtime profile (<c>--runtime-profile-add NAME --kind proton|wine|native [--exec PATH] [--prefix PATH]
    /// [--options "KEY=value -flag"] [--gamemode] [--default]</c>), or one chosen from what was found (<c>--auto</c>).
    /// </summary>
    public const string RuntimeProfileAdd = "--runtime-profile-add";

    /// <summary>Uses a profile for an instance (<c>--runtime-assign INSTANCE PROFILE</c>; profile <c>default</c> clears it).</summary>
    public const string RuntimeAssign = "--runtime-assign";

    /// <summary>
    /// Shows what would run the game for an instance, and everything wrong with it, without starting anything
    /// (<c>--runtime-plan INSTANCE [--exe PATH]</c>). Exits non-zero when an error would stop the launch.
    /// </summary>
    public const string RuntimePlan = "--runtime-plan";

    /// <summary>Creates or repairs the Wine prefix of an instance (<c>--prefix-init INSTANCE</c>).</summary>
    public const string PrefixInit = "--prefix-init";

    /// <summary>
    /// Runs one program through the instance's runtime and streams its output
    /// (<c>--runtime-run INSTANCE --exe PATH [--args "a b"]</c>). A diagnostic, not the launch pipeline (phase 11).
    /// </summary>
    public const string RuntimeRun = "--runtime-run";

    private static bool RuntimeCommandRequested(string[] args)
    {
        return new[] { Runtimes, RuntimeProfileAdd, RuntimeAssign, RuntimePlan, PrefixInit, RuntimeRun }.Any(flag =>
            IndexOf(args, flag) >= 0
        );
    }

    private static void RunRuntimeCommands(IServiceProvider services, string[] args)
    {
        if (IndexOf(args, Runtimes) >= 0)
        {
            PrintRuntimes(services);
        }

        if (IndexOf(args, RuntimeProfileAdd) is var addIndex and >= 0)
        {
            AddRuntimeProfile(services, args, ValueAfter(args, addIndex));
        }

        if (IndexOf(args, RuntimeAssign) is var assignIndex and >= 0)
        {
            AssignRuntimeProfile(services, ValueAfter(args, assignIndex), args.ElementAtOrDefault(assignIndex + 2) ?? string.Empty);
        }

        if (IndexOf(args, RuntimePlan) is var planIndex and >= 0)
        {
            PrintRuntimePlan(services, args, ValueAfter(args, planIndex));
        }

        if (IndexOf(args, RuntimeRun) is var runIndex and >= 0)
        {
            RunProgram(services, args, ValueAfter(args, runIndex));
        }

        if (IndexOf(args, PrefixInit) is var initIndex and >= 0)
        {
            InitializePrefix(services, ValueAfter(args, initIndex));
        }
    }

    private static RuntimeSearchOptions SearchOptions(IServiceProvider services)
    {
        var settings = services.GetRequiredService<ISettingsService>().LoadAsync().GetAwaiter().GetResult();

        return new RuntimeSearchOptions { ExtraProtonRoots = settings.ExtraProtonRoots, UmuRunPath = settings.UmuRunPath };
    }

    private static void PrintRuntimes(IServiceProvider services)
    {
        var inventory = services.GetRequiredService<IRuntimeLocator>().Locate(SearchOptions(services));

        if (!inventory.IsSupported)
        {
            Log.Information("This platform runs the game natively: there is no Proton/Wine runtime to configure.");
            return;
        }

        Log.Information("Proton builds ({Count}):", inventory.ProtonBuilds.Count);

        foreach (var build in inventory.ProtonBuilds)
        {
            Log.Information(
                "  {State} {Name} [{Version}] {Path}",
                build.IsValid ? "ok     " : "INVALID",
                build.Name,
                build.Version ?? "?",
                build.Path
            );

            if (build.Problem is not null)
            {
                Log.Information("    {Problem}", build.Problem);
            }
        }

        Log.Information("umu-run: {Path}", inventory.UmuRun ?? "not found");
        Log.Information("wine: {Path}", inventory.Wine ?? "not found");
        Log.Information("winetricks: {Path}", inventory.Winetricks ?? "not found");
        Log.Information(
            "GameMode: {Installed}, daemon {Running}",
            inventory.GameMode.Installed ? "installed" : "not installed",
            inventory.GameMode.DaemonRunning ? "running" : "not running"
        );
        Log.Information("Searched: {Roots}", string.Join(", ", inventory.SearchedRoots));

        if (RuntimeDefaults.Suggest(inventory) is { } suggestion)
        {
            Log.Information(
                "Suggested profile: {Kind} {Executable} (create it with {Flag} NAME --auto)",
                suggestion.Kind,
                suggestion.Executable,
                RuntimeProfileAdd
            );
        }
    }

    private static void AddRuntimeProfile(IServiceProvider services, string[] args, string name)
    {
        var profiles = services.GetRequiredService<IRuntimeProfileService>();
        RuntimeProfileDraft draft;

        if (args.Contains("--auto", StringComparer.Ordinal))
        {
            var inventory = services.GetRequiredService<IRuntimeLocator>().Locate(SearchOptions(services));
            var suggestion =
                RuntimeDefaults.Suggest(inventory, name)
                ?? throw new RuntimeProfileException("Nothing usable was found to build a profile from: see --runtimes.");
            draft = suggestion;
        }
        else
        {
            var kind = ValueOf(args, "--kind").ToLowerInvariant() switch
            {
                "proton" => RuntimeKind.Proton,
                "wine" => RuntimeKind.Wine,
                "native" => RuntimeKind.Native,
                _ => throw new RuntimeProfileException("--kind must be proton, wine or native."),
            };
            var options = LaunchOptions.Parse(ValueOf(args, "--options"));
            var environment = args.Contains("--options", StringComparer.Ordinal) ? options.Environment : RuntimeDefaults.NewEnvironment();

            draft = new RuntimeProfileDraft
            {
                Name = name,
                Kind = kind,
                Executable = NullIfEmpty(ValueOf(args, "--exec")),
                PrefixPath = NullIfEmpty(ValueOf(args, "--prefix")),
                Environment = environment,
                ExtraArguments =
                    options.Arguments.Count == 0 ? null : LaunchOptions.Format(new Dictionary<string, string>(), options.Arguments),
                UseGameMode = args.Contains("--gamemode", StringComparer.Ordinal),
                MakeDefault = args.Contains("--default", StringComparer.Ordinal),
            };
        }

        var saved = profiles.CreateAsync(draft).GetAwaiter().GetResult();
        Log.Information(
            "Created runtime profile {Name} ({Kind}){Default}: {Executable}",
            saved.Name,
            saved.Kind,
            saved.IsDefault ? " [default]" : string.Empty,
            saved.Executable ?? "(from PATH)"
        );
    }

    private static void AssignRuntimeProfile(IServiceProvider services, string instanceName, string profileName)
    {
        var instance = FindInstance(services, instanceName);
        var profiles = services.GetRequiredService<IRuntimeProfileService>();
        Guid? profileId = null;

        if (!string.Equals(profileName, "default", StringComparison.Ordinal))
        {
            profileId = (
                profiles.FindAsync(profileName).GetAwaiter().GetResult()
                ?? throw new RuntimeProfileException($"No runtime profile named '{profileName}'.")
            ).Id;
        }

        profiles.AssignAsync(instance.Id, profileId).GetAwaiter().GetResult();
        Log.Information("{Instance} now uses {Profile}", instance.Name, profileId is null ? "the default runtime profile" : profileName);
    }

    private static void PrintRuntimePlan(IServiceProvider services, string[] args, string instanceName)
    {
        var instance = FindInstance(services, instanceName);
        var profile = services.GetRequiredService<IRuntimeProfileService>().ResolveForInstanceAsync(instance.Id).GetAwaiter().GetResult();
        var gameDirectory = Path.Combine(instance.Path, "gamefiles");
        var program = NullIfEmpty(ValueOf(args, "--exe")) ?? Path.Combine(gameDirectory, "EscapeFromTarkov.exe");

        var launcher = services.GetRequiredService<IRuntimeLauncher>();
        var plan = launcher.Plan(
            instance,
            profile,
            new RuntimeLaunchRequest
            {
                Program = program,
                WorkingDirectory = Path.GetDirectoryName(program) ?? gameDirectory,
                LoadsDoorstop = true,
            },
            SearchOptions(services)
        );

        Log.Information("Runtime plan for {Instance} (profile: {Profile})", instance.Name, profile?.Name ?? "none");
        Log.Information("  prefix: {Prefix}", plan.PrefixPath ?? "(native, none)");

        if (plan.PrefixPath is not null)
        {
            var inspection = services.GetRequiredService<IPrefixService>().Inspect(plan.PrefixPath);
            Log.Information(
                "  prefix state: {State}{Reason}",
                inspection.State,
                inspection.Reason is null ? string.Empty : " — " + inspection.Reason
            );
        }

        foreach (var problem in plan.Problems)
        {
            Log.Information(
                "  {Severity} [{Code}] {Message}",
                problem.Severity.ToString().ToUpperInvariant(),
                problem.Code,
                problem.Message
            );
        }

        if (plan.Command is not null)
        {
            Log.Information("  command: {Command}", plan.Command.Describe());
            Log.Information("  in: {Directory}", plan.Command.WorkingDirectory);
        }

        if (!plan.CanRun)
        {
            throw new InstanceValidationException([$"The game cannot be launched: {plan.Errors.Count()} problem(s) above."]);
        }
    }

    private static void RunProgram(IServiceProvider services, string[] args, string instanceName)
    {
        var instance = FindInstance(services, instanceName);
        var profile = services.GetRequiredService<IRuntimeProfileService>().ResolveForInstanceAsync(instance.Id).GetAwaiter().GetResult();
        var program = NullIfEmpty(ValueOf(args, "--exe")) ?? throw new RuntimeProfileException("--exe PATH is required.");
        var plan = services
            .GetRequiredService<IRuntimeLauncher>()
            .Plan(
                instance,
                profile,
                new RuntimeLaunchRequest
                {
                    Program = program,
                    Arguments = LaunchOptions.Tokenize(ValueOf(args, "--args")),
                    WorkingDirectory = Path.GetDirectoryName(program) ?? instance.Path,
                },
                SearchOptions(services)
            );

        foreach (var problem in plan.Problems)
        {
            Log.Information(
                "  {Severity} [{Code}] {Message}",
                problem.Severity.ToString().ToUpperInvariant(),
                problem.Code,
                problem.Message
            );
        }

        if (!plan.CanRun || plan.Command is null)
        {
            throw new InstanceValidationException(["The program was not started: see the problems above."]);
        }

        Log.Information("Running: {Command}", plan.Command.Describe());
        var result = services
            .GetRequiredService<IProcessRunner>()
            .RunAsync(
                plan.Command,
                new Progress<ProcessLine>(line => Log.Information("[{Source}] {Line}", line.IsError ? "stderr" : "stdout", line.Text)),
                TimeSpan.FromMinutes(2)
            )
            .GetAwaiter()
            .GetResult();

        Log.Information(
            "The program exited with code {Code}{TimedOut}",
            result.ExitCode,
            result.TimedOut ? " (timed out and stopped)" : string.Empty
        );
    }

    private static void InitializePrefix(IServiceProvider services, string instanceName)
    {
        var instance = FindInstance(services, instanceName);
        var profile =
            services.GetRequiredService<IRuntimeProfileService>().ResolveForInstanceAsync(instance.Id).GetAwaiter().GetResult()
            ?? throw new RuntimeProfileException("No runtime profile is selected: create one with --runtime-profile-add.");

        var result = services
            .GetRequiredService<IPrefixService>()
            .InitializeAsync(
                instance,
                profile,
                new Progress<ProcessLine>(line => Log.Debug("[wine] {Line}", line.Text)),
                SearchOptions(services)
            )
            .GetAwaiter()
            .GetResult();

        foreach (var problem in result.Problems)
        {
            Log.Information(
                "  {Severity} [{Code}] {Message}",
                problem.Severity.ToString().ToUpperInvariant(),
                problem.Code,
                problem.Message
            );
        }

        Log.Information(
            "Prefix {Path}: {State}, exit code {Exit}",
            result.Prefix.Path,
            result.Prefix.State,
            result.ExitCode?.ToString() ?? "-"
        );

        if (!result.Succeeded)
        {
            throw new InstanceValidationException([
                $"The prefix of {instance.Name} is not ready: {result.Prefix.Reason ?? "see the messages above"}.",
            ]);
        }
    }

    private static string? NullIfEmpty(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
