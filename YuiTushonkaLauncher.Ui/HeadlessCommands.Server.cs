using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Server;

namespace YuiTushonkaLauncher;

/// <summary>Headless commands for the local SPT server layer (phase 10).</summary>
internal static partial class HeadlessCommands
{
    /// <summary>Shows where the instance's server is, what it needs, and everything wrong, without starting it (<c>--server-info INSTANCE</c>).</summary>
    public const string ServerInfo = "--server-info";

    /// <summary>
    /// Starts the server, waits for it, and stops it again (<c>--server-run INSTANCE [--seconds N] [--restart]</c>).
    /// Without <c>--seconds</c> it runs until SIGINT/SIGTERM.
    /// </summary>
    public const string ServerRun = "--server-run";

    /// <summary>Lists the server's profiles (<c>--server-profiles INSTANCE</c>); a server that is not running is started for the call.</summary>
    public const string ServerProfiles = "--server-profiles";

    /// <summary>Creates a profile (<c>--server-profile-add INSTANCE NAME [--edition EDITION]</c>).</summary>
    public const string ServerProfileAdd = "--server-profile-add";

    /// <summary>Deletes a profile after backing it up (<c>--server-profile-remove INSTANCE NAME --yes</c>).</summary>
    public const string ServerProfileRemove = "--server-profile-remove";

    /// <summary>Wipes a profile after backing it up (<c>--server-profile-wipe INSTANCE NAME --yes</c>).</summary>
    public const string ServerProfileWipe = "--server-profile-wipe";

    /// <summary>Selects a profile and remembers it (<c>--server-profile-select INSTANCE NAME</c>).</summary>
    public const string ServerProfileSelect = "--server-profile-select";

    /// <summary>Shows or sets the server port (<c>--server-port INSTANCE [PORT]</c>); without a port it shows the current one and the next free one.</summary>
    public const string ServerPort = "--server-port";

    /// <summary>
    /// Compares the instance with a server's mod list (<c>--server-sync INSTANCE [--server HOST:PORT] [--apply] [--override-pins]</c>).
    /// </summary>
    public const string ServerSync = "--server-sync";

    /// <summary>
    /// Lists what a crashed launcher left running — the server and the game of each instance
    /// (<c>--stale [--reap]</c>). <c>--server-stale</c> is kept as an alias.
    /// </summary>
    public const string Stale = "--stale";

    private static readonly string[] ServerFlags =
    [
        ServerInfo,
        ServerRun,
        ServerProfiles,
        ServerProfileAdd,
        ServerProfileRemove,
        ServerProfileWipe,
        ServerProfileSelect,
        ServerPort,
        ServerSync,
        Stale,
    ];

    private static bool ServerCommandRequested(string[] args)
    {
        return ServerFlags.Any(flag => IndexOf(args, flag) >= 0) || IndexOf(args, ServerStaleAlias) >= 0;
    }

    /// <summary>The name this command had in phase 10; kept so a script does not break.</summary>
    private const string ServerStaleAlias = "--server-stale";

    private static void RunServerCommands(IServiceProvider services, string[] args)
    {
        if (IndexOf(args, Stale) < 0 && IndexOf(args, ServerStaleAlias) >= 0)
        {
            args = [.. args, Stale];
        }

        foreach (var flag in ServerFlags)
        {
            var index = IndexOf(args, flag);

            if (index < 0)
            {
                continue;
            }

            var first = ValueAfter(args, index);
            var second = args.ElementAtOrDefault(index + 2) ?? string.Empty;

            switch (flag)
            {
                case ServerInfo:
                    PrintServerInfo(services, first);
                    break;
                case ServerRun:
                    RunServer(services, args, first);
                    break;
                case ServerProfiles:
                    WithServer(services, first, (instance, _) => ListProfilesAsync(services, instance));
                    break;
                case ServerProfileAdd:
                    WithServer(services, first, (instance, _) => AddProfileAsync(services, instance, second, ValueOf(args, "--edition")));
                    break;
                case ServerProfileRemove:
                    WithServer(
                        services,
                        first,
                        (instance, _) => RemoveProfileAsync(services, instance, second, args.Contains("--yes", StringComparer.Ordinal))
                    );
                    break;
                case ServerProfileWipe:
                    WithServer(
                        services,
                        first,
                        (instance, _) => WipeProfileAsync(services, instance, second, args.Contains("--yes", StringComparer.Ordinal))
                    );
                    break;
                case ServerProfileSelect:
                    WithServer(services, first, (instance, _) => SelectProfileAsync(services, instance, second));
                    break;
                case ServerPort:
                    ShowOrSetPort(services, first, second);
                    break;
                case ServerSync:
                    SyncWithServer(services, args, first);
                    break;
                case Stale:
                    ListLeftovers(services, args.Contains("--reap", StringComparer.Ordinal));
                    break;
            }
        }
    }

    private static void PrintServerInfo(IServiceProvider services, string instanceName)
    {
        var instance = FindInstance(services, instanceName);
        var plan = services.GetRequiredService<ISptServerSupervisor>().PlanAsync(instance).GetAwaiter().GetResult();
        var location = plan.Location;

        Log.Information("Server of '{Instance}'", instance.Name);
        Log.Information("  executable: {Path}", location.ExecutablePath ?? "MISSING");
        Log.Information("  working directory: {Path}", location.WorkingDirectory);
        Log.Information("  endpoint: {Url} (bind address '{Host}')", location.Endpoint.BaseUri, location.Endpoint.Host);
        Log.Information(
            "  needs: {Needs}",
            location.Requirements.Count == 0
                ? "unknown"
                : string.Join(", ", location.Requirements.Select(item => $"{item.Name} {item.Version}"))
        );
        Log.Information("  command: {Command}", plan.Command?.Describe() ?? "none");

        PrintProblems(plan.Problems);

        if (!plan.CanStart)
        {
            throw new InvalidOperationException($"The server of '{instance.Name}' cannot start.");
        }

        Log.Information("  ready to start");
    }

    private static void RunServer(IServiceProvider services, string[] args, string instanceName)
    {
        var instance = FindInstance(services, instanceName);
        var supervisor = services.GetRequiredService<ISptServerSupervisor>();
        var seconds = int.TryParse(ValueOf(args, "--seconds"), out var parsed) ? parsed : 0;

        using var cancellation = new CancellationTokenSource();
        using var interrupt = PosixSignalRegistration.Create(
            PosixSignal.SIGINT,
            context =>
            {
                context.Cancel = true;
                cancellation.Cancel();
            }
        );
        using var terminate = PosixSignalRegistration.Create(
            PosixSignal.SIGTERM,
            context =>
            {
                context.Cancel = true;
                cancellation.Cancel();
            }
        );

        supervisor.StatusChanged += (_, status) =>
            Log.Information("  [{State}] pid {Pid} {Reason}", status.State, status.ProcessId?.ToString() ?? "-", status.FailureReason);

        try
        {
            var status = supervisor
                .StartAsync(
                    instance,
                    new ServerStartOptions { RestartOnCrash = args.Contains("--restart", StringComparer.Ordinal) },
                    cancellation.Token
                )
                .GetAwaiter()
                .GetResult();

            Log.Information(
                "Server of '{Instance}' ready at {Url} after {Seconds:0.0} s (pid {Pid})",
                instance.Name,
                status.Endpoint?.BaseUri,
                status.ReadyAfter?.TotalSeconds,
                status.ProcessId
            );

            try
            {
                Task.Delay(seconds > 0 ? TimeSpan.FromSeconds(seconds) : Timeout.InfiniteTimeSpan, cancellation.Token)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (OperationCanceledException)
            {
                Log.Information("Interrupted: stopping the server");
            }
        }
        catch (OperationCanceledException)
        {
            Log.Information("Interrupted before the server was ready");
        }
        finally
        {
            var started = DateTimeOffset.UtcNow;
            var stopped = supervisor.StopAsync(instance.Id).GetAwaiter().GetResult();
            Log.Information(
                "Stopped after {Ms:0} ms (exit code {Code})",
                (DateTimeOffset.UtcNow - started).TotalMilliseconds,
                stopped.ExitCode?.ToString() ?? "-"
            );

            foreach (var line in supervisor.RecentOutput(instance.Id, 12))
            {
                Log.Information("  | {Line}", line.Text);
            }
        }
    }

    /// <summary>Runs <paramref name="work" /> against the instance's server, starting a temporary one when none answers and stopping it after.</summary>
    private static void WithServer(IServiceProvider services, string instanceName, Func<Core.Domain.Instance, CancellationToken, Task> work)
    {
        var instance = FindInstance(services, instanceName);
        var supervisor = services.GetRequiredService<ISptServerSupervisor>();
        var endpoint = services.GetRequiredService<ISptServerLocator>().Locate(instance).Endpoint;
        var alreadyUp = services.GetRequiredService<ISptServerClientFactory>().Create(endpoint).PingAsync().GetAwaiter().GetResult();
        var startedHere = false;

        try
        {
            if (!alreadyUp)
            {
                var status = supervisor.StartAsync(instance).GetAwaiter().GetResult();
                startedHere = true;
                Log.Information(
                    "Started a temporary server for this command (ready after {Seconds:0.0} s)",
                    status.ReadyAfter?.TotalSeconds
                );
            }

            work(instance, CancellationToken.None).GetAwaiter().GetResult();
        }
        finally
        {
            if (startedHere)
            {
                supervisor.StopAsync(instance.Id).GetAwaiter().GetResult();
                Log.Information("Stopped the temporary server");
            }
        }
    }

    private static async Task ListProfilesAsync(IServiceProvider services, Core.Domain.Instance instance)
    {
        var profiles = await services.GetRequiredService<IServerProfileService>().ListAsync(instance.Id);
        PrintProfiles(profiles);
    }

    private static async Task AddProfileAsync(IServiceProvider services, Core.Domain.Instance instance, string name, string edition)
    {
        var profiles = await services
            .GetRequiredService<IServerProfileService>()
            .CreateAsync(instance.Id, name, string.IsNullOrEmpty(edition) ? "Standard" : edition);
        Log.Information("Created '{Name}'", name);
        PrintProfiles(profiles);
    }

    private static async Task RemoveProfileAsync(IServiceProvider services, Core.Domain.Instance instance, string name, bool confirmed)
    {
        var result = await services.GetRequiredService<IServerProfileService>().DeleteAsync(instance.Id, name, confirmed);
        Log.Information("Deleted '{Name}'; backup: {Backup}", name, result.BackupPath ?? "none (the profile had no file yet)");
        PrintProfiles(result.Profiles);
    }

    private static async Task WipeProfileAsync(IServiceProvider services, Core.Domain.Instance instance, string name, bool confirmed)
    {
        var result = await services.GetRequiredService<IServerProfileService>().WipeAsync(instance.Id, name, confirmed);
        Log.Information(
            "Wipe of '{Name}' accepted (the server applies it the next time the game starts); backup: {Backup}",
            name,
            result.BackupPath ?? "none"
        );
        PrintProfiles(result.Profiles);
    }

    private static async Task SelectProfileAsync(IServiceProvider services, Core.Domain.Instance instance, string name)
    {
        var service = services.GetRequiredService<IServerProfileService>();
        var profile = await service.SelectAsync(instance.Id, name);
        var last = await service.GetLastUsedAsync(instance.Id);
        Log.Information(
            "Selected '{Name}' ({Id}); remembered as the last profile: {Last}",
            profile.Username,
            profile.ProfileId,
            last?.Username ?? "NOT REMEMBERED"
        );
    }

    private static void PrintProfiles(IReadOnlyList<SptProfileSummary> profiles)
    {
        Log.Information("Profiles ({Count}):", profiles.Count);

        foreach (var profile in profiles)
        {
            Log.Information(
                "  {Name} [{Edition}] id {Id} level {Level} {State}; client mods recorded: {Mods}",
                profile.Username,
                profile.Edition,
                profile.ProfileId,
                profile.Level,
                profile.InvalidOrUnloadable ? "UNLOADABLE"
                    : profile.PendingWipe ? "not played yet"
                    : "played",
                profile.ClientMods.Count
            );
        }
    }

    private static void ShowOrSetPort(IServiceProvider services, string instanceName, string portText)
    {
        var instance = FindInstance(services, instanceName);
        var ports = services.GetRequiredService<IServerPortService>();

        if (string.IsNullOrEmpty(portText))
        {
            var current = services.GetRequiredService<ISptServerLocator>().Locate(instance).Endpoint;
            Log.Information(
                "'{Instance}' listens on {Host}:{Port}; next free port: {Free}",
                instance.Name,
                current.Host,
                current.Port,
                ports.SuggestFreePortAsync().GetAwaiter().GetResult()
            );
            return;
        }

        if (!int.TryParse(portText, out var port))
        {
            throw new ServerPortException($"'{portText}' is not a port number.");
        }

        ports.SetPortAsync(instance.Id, port).GetAwaiter().GetResult();
        Log.Information("'{Instance}' will listen on port {Port} the next time it starts", instance.Name, port);
    }

    private static void SyncWithServer(IServiceProvider services, string[] args, string instanceName)
    {
        var instance = FindInstance(services, instanceName);
        var sync = services.GetRequiredService<IServerSyncService>();
        var remote = ValueOf(args, "--server");
        ServerEndpoint? endpoint = null;

        if (remote.Length > 0 && !ServerEndpoint.TryParse(remote, out endpoint!))
        {
            throw new InvalidOperationException($"'{remote}' is not a server address (use host:port).");
        }

        Task Run(Core.Domain.Instance target, CancellationToken token)
        {
            return SyncAsync(services, sync, target, endpoint, args);
        }

        if (endpoint is null)
        {
            WithServer(services, instanceName, Run);
        }
        else
        {
            Run(instance, CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    private static async Task SyncAsync(
        IServiceProvider services,
        IServerSyncService sync,
        Core.Domain.Instance instance,
        ServerEndpoint? endpoint,
        string[] args
    )
    {
        var plan = await sync.PlanAsync(instance.Id, endpoint);

        Log.Information(
            "Server {Url} (SPT {Version}): {Verdict}",
            plan.Server.BaseUri,
            plan.ServerSptVersion,
            plan.IsInSync ? "in sync" : "differs"
        );

        foreach (var entry in plan.ServerMods)
        {
            Log.Information(
                "  {Status,-14} {Name} ({Guid}) server {Server} / here {Local} {Detail}",
                entry.Status,
                entry.Name,
                entry.Guid,
                entry.ServerVersion ?? "-",
                entry.LocalVersion ?? "-",
                entry.Detail
            );
        }

        foreach (var entry in plan.ClientMods)
        {
            Log.Information(
                "  client {Status,-14} {Name} recorded {Recorded} / here {Local}",
                entry.Status,
                entry.Name,
                entry.RecordedVersion ?? "-",
                entry.LocalVersion ?? "-"
            );
        }

        if (!args.Contains("--apply", StringComparer.Ordinal))
        {
            if (plan.Actionable.Count > 0)
            {
                Log.Information("Nothing changed. Run again with --apply to install the {Count} difference(s).", plan.Actionable.Count);
            }

            return;
        }

        var result = await sync.ApplyAsync(
            instance.Id,
            plan,
            new ServerSyncOptions { OverridePins = args.Contains("--override-pins", StringComparer.Ordinal) }
        );

        foreach (var outcome in result.Outcomes)
        {
            Log.Information(
                "  {Result} {Name} {Error}",
                outcome.Applied ? "installed" : "not installed",
                outcome.Entry.Name,
                outcome.Error
            );
        }

        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Some mods were not installed; see above.");
        }
    }

    private static void ListLeftovers(IServiceProvider services, bool reap)
    {
        var adoption = services.GetRequiredService<Core.Runtime.IRunAdoptionService>();
        var leftovers = adoption.FindLeftoversAsync().GetAwaiter().GetResult();

        Log.Information("Processes left over by a launcher that died: {Count} instance(s)", leftovers.Count);

        foreach (var run in leftovers)
        {
            Log.Information("  {Instance}:", run.Run.InstanceName);

            foreach (var (kind, member) in new[] { ("server", run.Server), ("game", run.Game) })
            {
                if (member is null)
                {
                    continue;
                }

                Log.Information(
                    "    {Kind} pid {Pid} since {Since:u} {State} [{Mechanism}] {Path}",
                    kind,
                    member.Record.ProcessId,
                    member.Record.StartedAt,
                    member.State,
                    member.Record.Mechanism,
                    member.Record.ExecutablePath
                );
            }
        }

        if (!reap || leftovers.Count == 0)
        {
            if (leftovers.Count > 0)
            {
                Log.Information("Nothing changed. Add --reap to stop them.");
            }

            return;
        }

        var stopped = 0;

        foreach (var run in leftovers)
        {
            stopped += adoption.StopAsync(run).GetAwaiter().GetResult();
        }

        Log.Information("Stopped {Count} process(es)", stopped);
    }

    private static void PrintProblems(IEnumerable<ServerProblem> problems)
    {
        foreach (var problem in problems)
        {
            if (problem.IsError)
            {
                Log.Error("  {Code}: {Message}", problem.Code, problem.Message);
            }
            else
            {
                Log.Warning("  {Code}: {Message}", problem.Code, problem.Message);
            }
        }
    }
}
