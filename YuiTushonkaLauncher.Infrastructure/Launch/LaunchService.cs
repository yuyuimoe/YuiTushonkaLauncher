using System.Diagnostics;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Launch;
using YuiTushonkaLauncher.Core.Patching;
using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Core.Server;

namespace YuiTushonkaLauncher.Infrastructure.Launch;

/// <summary>
/// The one launch pipeline: check everything, patch if the instance needs it, start or adopt the server, wait for it,
/// resolve the profile, then start the game detached and leave the pair to the run record.
/// </summary>
public sealed class LaunchService(
    IInstanceService instances,
    ISptServerSupervisor supervisor,
    ISptServerLocator locator,
    ISptServerClientFactory clients,
    IServerProfileService profiles,
    IPatchService patching,
    IRuntimeLauncher runtimeLauncher,
    IRuntimeProfileService runtimeProfiles,
    IChildProcessSpawner spawner,
    IInstanceRunStore runs,
    IInstanceBusyGuard busy,
    IProcessInspector inspector,
    TimeProvider time,
    ILogger<LaunchService> logger
) : ILaunchService
{
    /// <summary>The files the game cannot start without.</summary>
    private static readonly string[] RequiredFiles =
    [
        InstancePaths.GameExecutable,
        "UnityPlayer.dll",
        "winhttp.dll",
        "doorstop_config.ini",
        "BepInEx/core/BepInEx.Preloader.dll",
    ];

    /// <inheritdoc />
    public async Task<LaunchPreflight> PlanAsync(LaunchRequest request, CancellationToken cancellationToken = default)
    {
        var preflight = Stopwatch.StartNew();
        logger.LogDebug("Pre-flight for {InstanceId}: starting", request.InstanceId);

        var instance = (await instances.ListAsync(cancellationToken)).FirstOrDefault(row => row.Id == request.InstanceId);

        if (instance is null)
        {
            return Plan(request, "?", [new LaunchProblem(LaunchProblemCode.InstanceMissing, "That instance does not exist.")]);
        }

        var problems = new List<LaunchProblem>();
        var shape = InstancePaths.Detect(instance.Path);

        if (shape != InstallShape.Merged)
        {
            problems.Add(
                new LaunchProblem(
                    LaunchProblemCode.NotMerged,
                    $"'{instance.Name}' is not in SPT's merged layout, so the game cannot start from it. Migrate it first (--migrate-layout).",
                    instance.Path
                )
            );
        }

        var missing = RequiredFiles.Where(file => !File.Exists(Path.Combine(instance.Path, file))).ToList();

        if (missing.Count > 0)
        {
            problems.Add(
                new LaunchProblem(
                    LaunchProblemCode.FilesMissing,
                    $"The instance is missing {missing.Count} file(s) the game needs: {string.Join(", ", missing)}.",
                    Path.Combine(instance.Path, missing[0])
                )
            );
        }

        logger.LogDebug(
            "Pre-flight for {Instance}: layout {Shape}, {Missing} missing files, {Elapsed} ms",
            instance.Name,
            shape,
            missing.Count,
            preflight.ElapsedMilliseconds
        );

        var username = await ResolveUsernameAsync(instance, request, cancellationToken);
        logger.LogDebug(
            "Pre-flight for {Instance}: profile {Username} after {Elapsed} ms",
            instance.Name,
            username ?? "none",
            preflight.ElapsedMilliseconds
        );

        if (username is null)
        {
            problems.Add(
                new LaunchProblem(
                    LaunchProblemCode.ProfileMissing,
                    $"No profile was chosen for '{instance.Name}': pick one, or set 'always use this profile' for this server."
                )
            );
        }

        var patchAlreadyApplied = instance.Patch.IsPatched;
        var program = request.GameProgramOverride ?? Path.Combine(instance.Path, InstancePaths.GameExecutable);
        // The runtime probe is the step that can be slow on a real machine: it looks for Proton and for the game's
        // platform layer, and a proton prefix on a slow disk is not instant. It says so, so a hang has a last line.
        logger.LogDebug("Pre-flight for {Instance}: resolving the runtime profile", instance.Name);
        var runtimeProfile = await runtimeProfiles.ResolveForInstanceAsync(instance.Id, cancellationToken);
        logger.LogDebug(
            "Pre-flight for {Instance}: runtime profile {Profile} after {Elapsed} ms",
            instance.Name,
            runtimeProfile?.Id.ToString() ?? "default",
            preflight.ElapsedMilliseconds
        );
        var launch = runtimeLauncher.Plan(
            instance,
            runtimeProfile,
            new RuntimeLaunchRequest
            {
                Program = program,
                Arguments = request.ExtraGameArguments,
                WorkingDirectory = instance.Path,
                Target = Target(request),
                LoadsDoorstop = true,
            }
        );

        foreach (var problem in launch.Errors)
        {
            problems.Add(new LaunchProblem(LaunchProblemCode.RuntimeUnavailable, problem.Message, problem.Path));
        }

        var status = supervisor.GetStatus(instance.Id);
        logger.LogDebug("Pre-flight for {Instance}: done in {Elapsed} ms", instance.Name, preflight.ElapsedMilliseconds);

        return Plan(
            request,
            instance.Name,
            problems,
            username,
            launch.Command,
            patchAlreadyApplied,
            status.State is ServerState.Running or ServerState.Starting || IsRecordedServerAlive(instance.Id)
        );
    }

    /// <inheritdoc />
    public async Task<LaunchResult> LaunchAsync(
        LaunchRequest request,
        IProgress<LaunchProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var started = time.GetUtcNow();
        var plan = await PlanAsync(request, cancellationToken);

        if (!plan.CanLaunch)
        {
            Report(
                progress,
                LaunchStep.PreFlight,
                LaunchStepOutcome.Failed,
                string.Join(" ", plan.Problems.Select(problem => problem.Message))
            );
            throw new LaunchException(string.Join(" ", plan.Problems.Select(problem => problem.Message)), plan.Problems);
        }

        var instance = (await instances.ListAsync(cancellationToken)).First(row => row.Id == request.InstanceId);
        Report(progress, LaunchStep.PreFlight, LaunchStepOutcome.Succeeded);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!request.SkipPatch && !plan.PatchAlreadyApplied)
            {
                await ApplyPatchAsync(instance, progress, cancellationToken);
            }
            else
            {
                Report(progress, LaunchStep.PatchCheck, LaunchStepOutcome.Succeeded);
            }

            var (serverPid, adopted) = await StartOrAdoptServerAsync(instance, plan, request, progress, cancellationToken);
            var (profileId, profileUsername) = await ResolveProfileAsync(instance, request, plan, progress, cancellationToken);

            if (request.RememberProfile)
            {
                await profiles.RememberAsync(instance.Id, profileUsername, profileId, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var game = await StartGameAsync(instance, request, plan, profileId, progress, cancellationToken);

            return new LaunchResult
            {
                InstanceId = instance.Id,
                Succeeded = true,
                ServerProcessId = serverPid,
                GameProcessId = game.ProcessId,
                ProfileId = profileId,
                GameCommand = plan.GameCommand?.Describe(),
                Took = time.GetUtcNow() - started,
            };
        }
        catch (OperationCanceledException)
        {
            Report(progress, LaunchStep.GameStart, LaunchStepOutcome.Cancelled, "The launch was cancelled before the game started.");
            throw;
        }
    }

    private async Task ApplyPatchAsync(Instance instance, IProgress<LaunchProgress>? progress, CancellationToken cancellationToken)
    {
        // Patching rewrites the binary the game loads: with a detached pair this is a real possibility, not a corner case.
        await busy.EnsureIdleAsync(instance.Id, "Patching", cancellationToken);
        Report(progress, LaunchStep.PatchCheck, LaunchStepOutcome.Running);

        var report = await patching.PatchAsync(
            instance.Id,
            new SynchronousProgress<PatchProgress>(patch =>
                Report(
                    progress,
                    LaunchStep.PatchCheck,
                    LaunchStepOutcome.Running,
                    $"{patch.FileName} {patch.Percentage:0}%",
                    patch.Percentage / 100
                )
            ),
            cancellationToken
        );

        if (!report.Succeeded)
        {
            var reason = string.Join(
                " ",
                report.Files.Where(file => file.Outcome is PatchFileOutcome.Failed or PatchFileOutcome.Refused).Select(file => file.Message)
            );
            Report(progress, LaunchStep.PatchCheck, LaunchStepOutcome.Failed, reason);

            throw new LaunchException(
                $"The patch of '{instance.Name}' failed: {reason}",
                [new LaunchProblem(LaunchProblemCode.PatchFailed, reason)]
            );
        }

        Report(progress, LaunchStep.PatchCheck, LaunchStepOutcome.Succeeded);
    }

    private async Task<(int? ServerPid, bool Adopted)> StartOrAdoptServerAsync(
        Instance instance,
        LaunchPreflight plan,
        LaunchRequest request,
        IProgress<LaunchProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        Report(progress, LaunchStep.ServerStart, LaunchStepOutcome.Running);

        if (request.ServerEndpoint is { } remote && !IsLocalEndpoint(remote))
        {
            // The chosen server runs on another machine: nothing to start, only to reach.
            var client = clients.Create(remote);
            try
            {
                if (!await client.PingAsync(cancellationToken))
                {
                    throw new LaunchException(
                        $"The server at {remote.BaseUri} is not answering, so the game would start against nothing.",
                        [new LaunchProblem(LaunchProblemCode.ServerFailed, $"No answer from {remote.BaseUri}.", remote.BaseUri.ToString())]
                    );
                }
            }
            finally
            {
                (client as IDisposable)?.Dispose();
            }

            Report(progress, LaunchStep.ServerStart, LaunchStepOutcome.Succeeded, $"Using the server at {remote.BaseUri}.");
            Report(progress, LaunchStep.Ready, LaunchStepOutcome.Succeeded, "The server is answering.");
            return (null, true);
        }

        if (plan.ServerAlreadyRunning)
        {
            Report(progress, LaunchStep.ServerStart, LaunchStepOutcome.Succeeded, "The server was already running; adopting it.");
            Report(progress, LaunchStep.Ready, LaunchStepOutcome.Succeeded, "The server is answering.");
            return (supervisor.GetStatus(instance.Id).ProcessId ?? runs.Find(instance.Id)?.Server?.ProcessId, true);
        }

        try
        {
            var status = await supervisor.StartAsync(instance, new ServerStartOptions { RestartOnCrash = false }, cancellationToken);
            Report(progress, LaunchStep.ServerStart, LaunchStepOutcome.Succeeded);
            Report(
                progress,
                LaunchStep.Ready,
                LaunchStepOutcome.Succeeded,
                $"The server answered after {status.ReadyAfter?.TotalSeconds:0.0} s."
            );
            return (status.ProcessId, false);
        }
        catch (ServerStartException exception)
        {
            Report(progress, LaunchStep.ServerStart, LaunchStepOutcome.Failed, exception.Message);

            throw new LaunchException(
                exception.Message,
                [
                    .. exception
                        .Problems.Where(problem => problem.IsError)
                        .Select(problem => new LaunchProblem(LaunchProblemCode.ServerFailed, problem.Message, problem.Path)),
                ]
            );
        }
    }

    private async Task<(string ProfileId, string Username)> ResolveProfileAsync(
        Instance instance,
        LaunchRequest request,
        LaunchPreflight plan,
        IProgress<LaunchProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        var username = plan.ProfileUsername!;
        var client = clients.Create(locator.Locate(instance).Endpoint);
        var available = await client.GetProfilesAsync(cancellationToken);
        var profile = available.FirstOrDefault(row => string.Equals(row.Username, username, StringComparison.Ordinal));

        if (profile is null)
        {
            var problem = new LaunchProblem(
                LaunchProblemCode.ProfileUnknown,
                $"The server has no profile named '{username}'. It has: {(available.Count == 0 ? "none" : string.Join(", ", available.Select(row => row.Username)))}."
            );

            Report(progress, LaunchStep.Ready, LaunchStepOutcome.Failed, problem.Message);
            throw new LaunchException(problem.Message, [problem]);
        }

        if (profile.InvalidOrUnloadable)
        {
            var problem = new LaunchProblem(
                LaunchProblemCode.ProfileUnloadable,
                $"The server could not load the profile '{username}', so it cannot be used."
            );
            Report(progress, LaunchStep.Ready, LaunchStepOutcome.Failed, problem.Message);
            throw new LaunchException(problem.Message, [problem]);
        }

        // Selecting it is what the official launcher does before it starts the game: it logs the profile in.
        await profiles.SelectAsync(instance.Id, username, cancellationToken, request.ServerEndpoint);

        return (profile.ProfileId, username);
    }

    private async Task<IChildProcess> StartGameAsync(
        Instance instance,
        LaunchRequest request,
        LaunchPreflight plan,
        string profileId,
        IProgress<LaunchProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        Report(progress, LaunchStep.GameStart, LaunchStepOutcome.Running);

        var endpoint = request.ServerEndpoint ?? locator.Locate(instance).Endpoint;
        var arguments = new List<string>
        {
            "-force-gfx-jobs",
            "native",
            $"-token={profileId}",
            $"-config={{'BackendUrl':'https://{endpoint.ConnectHost}:{endpoint.Port}','Version':'live','MatchingVersion':'live'}}",
        };
        arguments.AddRange(request.ExtraGameArguments);

        var program = request.GameProgramOverride ?? Path.Combine(instance.Path, InstancePaths.GameExecutable);
        logger.LogDebug("Starting the game for {Instance}: resolving the runtime profile", instance.Name);
        var runtimeProfile = await runtimeProfiles.ResolveForInstanceAsync(instance.Id, cancellationToken);
        var launch = runtimeLauncher.Plan(
            instance,
            runtimeProfile,
            new RuntimeLaunchRequest
            {
                Program = program,
                Arguments = arguments,
                WorkingDirectory = instance.Path,
                Target = Target(request),
                LoadsDoorstop = true,
            }
        );

        if (!launch.CanRun)
        {
            var reason = string.Join(" ", launch.Errors.Select(problem => problem.Message));
            Report(progress, LaunchStep.GameStart, LaunchStepOutcome.Failed, reason);
            throw new LaunchException(reason, [new LaunchProblem(LaunchProblemCode.RuntimeUnavailable, reason)]);
        }

        IChildProcess game;

        try
        {
            game = spawner.Start(new ChildProcessStart { Command = launch.Command! });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Report(progress, LaunchStep.GameStart, LaunchStepOutcome.Failed, exception.Message);
            throw new LaunchException(
                $"The game could not be started: {exception.Message}",
                [new LaunchProblem(LaunchProblemCode.GameFailed, exception.Message, program)]
            );
        }

        var previous = runs.Find(instance.Id);
        var run = (previous ?? new InstanceRun { InstanceId = instance.Id, InstanceName = instance.Name }) with
        {
            InstanceName = instance.Name,
            LauncherProcessId = Environment.ProcessId,
        };

        runs.Save(run.WithGame(new RunProcess(game.ProcessId, game.StartedAt, program, spawner.Mechanism, Scope: game.Scope)));

        logger.LogInformation(
            "Launched {Instance} with profile {Profile} (game pid {Game}, detach {Mechanism})",
            instance.Name,
            profileId,
            game.ProcessId,
            spawner.Mechanism
        );

        Report(progress, LaunchStep.GameStart, LaunchStepOutcome.Succeeded);
        Report(progress, LaunchStep.Detached, LaunchStepOutcome.Succeeded, "The game and the server keep running if the launcher closes.");

        return game;
    }

    private async Task<string?> ResolveUsernameAsync(Instance instance, LaunchRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.ProfileUsername))
        {
            return request.ProfileUsername.Trim();
        }

        return await profiles.GetRememberedUsernameAsync(instance.Id, cancellationToken);
    }

    /// <summary>
    /// The game is a Windows program, so on Linux it needs a runtime. A program given with
    /// <see cref="LaunchRequest.GameProgramOverride" /> is a stand-in for this platform, so it runs natively.
    /// </summary>
    private static ProgramTarget Target(LaunchRequest request)
    {
        return request.GameProgramOverride is null ? ProgramTarget.WindowsProgram : ProgramTarget.NativeProgram;
    }

    private bool IsRecordedServerAlive(Guid instanceId)
    {
        var run = runs.Find(instanceId);

        return run?.Server is { } server && inspector.IsAlive(server.ProcessId);
    }

    private static bool IsLocalEndpoint(ServerEndpoint endpoint)
    {
        return endpoint.ConnectHost.Trim('[', ']').ToLowerInvariant() switch
        {
            "127.0.0.1" or "localhost" or "::1" => true,
            _ => false,
        };
    }

    private static LaunchPreflight Plan(
        LaunchRequest request,
        string instanceName,
        List<LaunchProblem> problems,
        string? username = null,
        LaunchCommand? command = null,
        bool patchAlreadyApplied = false,
        bool serverAlreadyRunning = false
    )
    {
        return new LaunchPreflight
        {
            InstanceId = request.InstanceId,
            InstanceName = instanceName,
            Problems = problems,
            ProfileUsername = username,
            GameCommand = command,
            PatchAlreadyApplied = patchAlreadyApplied,
            ServerAlreadyRunning = serverAlreadyRunning,
        };
    }

    private static void Report(
        IProgress<LaunchProgress>? progress,
        LaunchStep step,
        LaunchStepOutcome outcome,
        string message = "",
        double? fraction = null
    )
    {
        progress?.Report(new LaunchProgress(step, outcome, message, fraction));
    }

    /// <summary>Reports progress synchronously: <see cref="Progress{T}" /> would post to a context a test never pumps.</summary>
    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value)
        {
            handler(value);
        }
    }
}
