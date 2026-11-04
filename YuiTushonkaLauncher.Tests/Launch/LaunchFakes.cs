using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Patching;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Tests.Server;

namespace YuiTushonkaLauncher.Tests.Launch;

/// <summary>A supervisor whose starts are scripted: one outcome, or an exception.</summary>
public sealed class FakeSupervisor : ISptServerSupervisor
{
    private readonly Dictionary<Guid, ServerStatus> _states = [];

    public int Starts { get; private set; }

    public List<ServerStartOptions?> Options { get; } = [];

    /// <summary>What a start does; the default brings the server up.</summary>
    public Func<Instance, ServerStatus>? OnStart { get; set; }

    public bool ShouldThrow { get; set; }

    public event EventHandler<ServerStatus>? StatusChanged
    {
        add { }
        remove { }
    }

    public void SetStatus(Guid instanceId, ServerState state, int? pid = null)
    {
        _states[instanceId] = new ServerStatus
        {
            InstanceId = instanceId,
            State = state,
            ProcessId = pid,
        };
    }

    public ServerStatus GetStatus(Guid instanceId)
    {
        return _states.GetValueOrDefault(instanceId) ?? new ServerStatus { InstanceId = instanceId, State = ServerState.Stopped };
    }

    public Task<ServerStartPlan> PlanAsync(Instance instance, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<ServerStatus> StartAsync(
        Instance instance,
        ServerStartOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        Starts++;
        Options.Add(options);

        if (ShouldThrow)
        {
            throw new ServerStartException("The server died: bad mod.");
        }

        var status =
            OnStart?.Invoke(instance)
            ?? new ServerStatus
            {
                InstanceId = instance.Id,
                State = ServerState.Running,
                ProcessId = 4321,
                Endpoint = new ServerEndpoint("127.0.0.1", 6969),
                ReadyAfter = TimeSpan.FromSeconds(3),
            };

        _states[instance.Id] = status;
        return Task.FromResult(status);
    }

    public Task<ServerStatus> StopAsync(Guid instanceId, TimeSpan? gracePeriod = null, CancellationToken cancellationToken = default)
    {
        var status = new ServerStatus { InstanceId = instanceId, State = ServerState.Stopped };
        _states[instanceId] = status;
        return Task.FromResult(status);
    }

    public IReadOnlyList<ServerOutputLine> RecentOutput(Guid instanceId, int maxLines = 200)
    {
        return [];
    }
}

/// <summary>Profiles as the pipeline uses them: the remembered one, and the selection.</summary>
public sealed class FakeProfiles : IServerProfileService
{
    public string? Remembered { get; set; }

    public List<(string Username, string? Id)> RememberedWrites { get; } = [];

    public List<string> Selected { get; } = [];

    public Task<string?> GetRememberedUsernameAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Remembered);
    }

    public Task RememberAsync(Guid instanceId, string? username, string? profileId, CancellationToken cancellationToken = default)
    {
        Remembered = username;
        RememberedWrites.Add((username ?? string.Empty, profileId));
        return Task.CompletedTask;
    }

    public Task<SptProfileSummary> SelectAsync(Guid instanceId, string username, CancellationToken cancellationToken = default)
    {
        Selected.Add(username);
        return Task.FromResult(FakeSptClient.NewProfile(username, "Standard"));
    }

    public Task<IReadOnlyList<SptProfileSummary>> ListAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<IReadOnlyList<SptProfileSummary>> CreateAsync(
        Guid instanceId,
        string username,
        string edition,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotSupportedException();
    }

    public Task<ProfileOperationResult> DeleteAsync(
        Guid instanceId,
        string username,
        bool confirmed,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotSupportedException();
    }

    public Task<ProfileOperationResult> WipeAsync(
        Guid instanceId,
        string username,
        bool confirmed,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotSupportedException();
    }

    public Task<SptProfileSummary?> GetLastUsedAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}

/// <summary>Patching as the pipeline uses it: does it need to run, and does it succeed.</summary>
public sealed class FakePatcher : IPatchService
{
    public int Calls { get; private set; }

    public bool Fail { get; set; }

    public Task<PatchPlan> PlanAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<PatchReport> PatchAsync(
        Guid instanceId,
        IProgress<PatchProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        Calls++;
        progress?.Report(
            new PatchProgress
            {
                FileName = "Assembly-CSharp.dll",
                Percentage = 50,
                BytesWritten = 5,
                TotalBytes = 10,
            }
        );

        return Task.FromResult(
            Fail
                ? new PatchReport
                {
                    Files =
                    [
                        new PatchFileResult
                        {
                            RelativePath = "EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll",
                            Outcome = PatchFileOutcome.Failed,
                            Message = "The delta is corrupt.",
                        },
                    ],
                }
                : new PatchReport
                {
                    Files =
                    [
                        new PatchFileResult
                        {
                            RelativePath = "EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll",
                            Outcome = PatchFileOutcome.Patched,
                            Message = "Patched.",
                        },
                    ],
                }
        );
    }

    public Task<PatchReport> RestoreAsync(
        Guid instanceId,
        IProgress<PatchProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotSupportedException();
    }
}

/// <summary>A guard a test can make busy, to prove a step refuses.</summary>
public sealed class FakeBusyGuard : Core.Launch.IInstanceBusyGuard
{
    public List<Core.Launch.InstanceActivity> Activity { get; } = [];

    public Task<IReadOnlyList<Core.Launch.InstanceActivity>> FindActivityAsync(
        Guid instanceId,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult<IReadOnlyList<Core.Launch.InstanceActivity>>([.. Activity]);
    }

    public Task EnsureIdleAsync(Guid instanceId, string operation, CancellationToken cancellationToken = default)
    {
        return Activity.Count == 0
            ? Task.CompletedTask
            : throw new Core.Launch.InstanceBusyException($"{operation} cannot run while '{Activity[0]}' is running.", [.. Activity]);
    }
}
