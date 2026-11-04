using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Server;

/// <summary>
/// A clock where waiting costs nothing: every timer fires at once and moves the clock forward by its due time, so a
/// readiness loop with a two-minute timeout runs in microseconds and still sees two minutes pass.
/// </summary>
public sealed class FastTimeProvider : TimeProvider
{
    private long _ticks = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero).UtcTicks;

    public override long TimestampFrequency
    {
        get { return TimeSpan.TicksPerSecond; }
    }

    public override DateTimeOffset GetUtcNow()
    {
        return new DateTimeOffset(Interlocked.Read(ref _ticks), TimeSpan.Zero);
    }

    public override long GetTimestamp()
    {
        return Interlocked.Read(ref _ticks);
    }

    public void Advance(TimeSpan by)
    {
        Interlocked.Add(ref _ticks, by.Ticks);
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new InstantTimer();

        if (dueTime != Timeout.InfiniteTimeSpan)
        {
            Advance(dueTime);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (!timer.Disposed)
                {
                    callback(state);
                }
            });
        }

        return timer;
    }

    private sealed class InstantTimer : ITimer
    {
        public bool Disposed { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            return true;
        }

        public void Dispose()
        {
            Disposed = true;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>A server that behaves like the real one measured in phase 10: it accepts anything, and answers in the real shapes.</summary>
public sealed class FakeSptClient : ISptServerClient
{
    public Uri BaseUri { get; init; } = new("https://127.0.0.1:6969");

    public bool Up { get; set; } = true;

    public string Version { get; set; } = "4.1.6";

    public Dictionary<string, string> Editions { get; } = new() { ["Standard"] = "Basic stash", ["Left Behind"] = "Bigger stash" };

    public List<SptProfileSummary> Profiles { get; } = [];

    public List<SptServerMod> ServerMods { get; } = [];

    /// <summary>Everything asked of the server, in order, e.g. <c>remove:maria</c>.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Runs before a change is applied, with the call name; lets a test look at the world at that instant.</summary>
    public Action<string>? BeforeChange { get; set; }

    public bool RefuseChanges { get; set; }

    /// <summary>The server finds the profile but will not log into it.</summary>
    public bool RejectLogin { get; set; }

    /// <summary>Replaces the answer to ping, e.g. to fail a few times first or to hang until cancelled.</summary>
    public Func<CancellationToken, Task<bool>>? PingBehavior { get; set; }

    public int PingCount
    {
        get { return Calls.Count(call => call == "ping"); }
    }

    public Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("ping");
        return PingBehavior?.Invoke(cancellationToken) ?? Task.FromResult(Up);
    }

    /// <summary>Answers false <paramref name="failures" /> times, then true.</summary>
    public void FailPingsThenAnswer(int failures)
    {
        var remaining = failures;
        PingBehavior = _ => Task.FromResult(remaining-- <= 0);
    }

    public Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        return Reach(() => Version);
    }

    public Task<IReadOnlyDictionary<string, string>> GetProfileTypesAsync(CancellationToken cancellationToken = default)
    {
        return Reach(() => (IReadOnlyDictionary<string, string>)Editions);
    }

    public Task<IReadOnlyList<SptProfileSummary>> GetProfilesAsync(CancellationToken cancellationToken = default)
    {
        return Reach(() => (IReadOnlyList<SptProfileSummary>)[.. Profiles]);
    }

    public Task<SptProfileSummary?> GetProfileAsync(string username, CancellationToken cancellationToken = default)
    {
        return Reach(() => Profiles.FirstOrDefault(profile => profile.Username == username));
    }

    public Task<bool> LoginAsync(string username, CancellationToken cancellationToken = default)
    {
        return Reach(() =>
        {
            Calls.Add($"login:{username}");
            return !RejectLogin && Profiles.Any(profile => profile.Username == username);
        });
    }

    public Task<SptProfileChange> RegisterAsync(string username, string edition, CancellationToken cancellationToken = default)
    {
        return Reach(() =>
        {
            Calls.Add($"register:{username}:{edition}");
            BeforeChange?.Invoke($"register:{username}");

            // The real server accepts an empty name and an unknown edition (measured); only a duplicate is refused.
            var accepted = !RefuseChanges && Profiles.All(profile => profile.Username != username);

            if (accepted)
            {
                Profiles.Add(NewProfile(username, edition));
            }

            return new SptProfileChange(accepted, [.. Profiles]);
        });
    }

    public Task<SptProfileChange> RemoveAsync(string username, CancellationToken cancellationToken = default)
    {
        return Reach(() =>
        {
            Calls.Add($"remove:{username}");
            BeforeChange?.Invoke($"remove:{username}");
            var accepted = !RefuseChanges && Profiles.RemoveAll(profile => profile.Username == username) > 0;
            return new SptProfileChange(accepted, [.. Profiles]);
        });
    }

    public Task<SptProfileChange> WipeAsync(string username, string edition, CancellationToken cancellationToken = default)
    {
        return Reach(() =>
        {
            Calls.Add($"wipe:{username}:{edition}");
            BeforeChange?.Invoke($"wipe:{username}");
            var index = Profiles.FindIndex(profile => profile.Username == username);
            var accepted = !RefuseChanges && index >= 0;

            if (accepted)
            {
                Profiles[index] = Profiles[index] with { Edition = edition, PendingWipe = true };
            }

            return new SptProfileChange(accepted, [.. Profiles]);
        });
    }

    public Task<IReadOnlyList<SptServerMod>> GetServerModsAsync(CancellationToken cancellationToken = default)
    {
        return Reach(() => (IReadOnlyList<SptServerMod>)[.. ServerMods]);
    }

    public Task<IReadOnlyList<SptModPage>> GetModPagesAsync(CancellationToken cancellationToken = default)
    {
        return Reach(() => (IReadOnlyList<SptModPage>)[]);
    }

    public static SptProfileSummary NewProfile(string username, string edition, string? id = null)
    {
        return new SptProfileSummary
        {
            Username = username,
            ProfileId = id ?? Guid.NewGuid().ToString("n")[..24],
            Edition = edition,
            PendingWipe = true,
        };
    }

    private Task<T> Reach<T>(Func<T> answer)
    {
        if (!Up)
        {
            return Task.FromException<T>(
                new SptServerException(SptServerFailure.Unreachable, $"{BaseUri} cannot be reached: connection refused")
            );
        }

        return Task.FromResult(answer());
    }
}

/// <summary>Hands out the same fake server for every endpoint unless a test registers one for a specific port.</summary>
public sealed class FakeClientFactory(FakeSptClient defaultClient) : ISptServerClientFactory
{
    public Dictionary<int, FakeSptClient> ByPort { get; } = [];

    public List<ServerEndpoint> Created { get; } = [];

    public ISptServerClient Create(ServerEndpoint endpoint)
    {
        Created.Add(endpoint);
        return ByPort.GetValueOrDefault(endpoint.Port) ?? defaultClient;
    }
}

public sealed class FakePortProbe : IPortProbe
{
    public HashSet<int> Busy { get; } = [];

    public bool IsFree(int port)
    {
        return !Busy.Contains(port);
    }
}

public sealed class FakeDotNetProbe : IDotNetRuntimeProbe
{
    /// <summary>What is "installed"; <c>null</c> means <c>dotnet</c> cannot be asked.</summary>
    public List<FrameworkRequirement>? Installed { get; set; } =
    [new("Microsoft.NETCore.App", "10.0.10"), new("Microsoft.AspNetCore.App", "10.0.10")];

    public Task<IReadOnlyList<FrameworkRequirement>?> ListInstalledAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<FrameworkRequirement>?>(Installed);
    }
}

/// <summary>A child process the test controls: it exits when the test (or a stop request) says so.</summary>
public sealed class FakeChild : IChildProcess
{
    private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FakeChild(int processId, DateTimeOffset startedAt)
    {
        ProcessId = processId;
        StartedAt = startedAt;
    }

    public int ProcessId { get; }

    public string? Scope { get; init; }

    public DateTimeOffset StartedAt { get; }

    public bool HasExited { get; private set; }

    public Task<int> Exited
    {
        get { return _exit.Task; }
    }

    public bool StopWasRequested { get; private set; }

    public bool WasKilled { get; private set; }

    /// <summary>What a stop request does: by default the process exits cleanly. Set to a no-op to model a server that ignores it.</summary>
    public Action<FakeChild> OnRequestStop { get; set; } = child => child.Exit(0);

    public void RequestStop()
    {
        StopWasRequested = true;
        OnRequestStop(this);
    }

    public void Kill()
    {
        WasKilled = true;
        Exit(137);
    }

    public void Exit(int code)
    {
        HasExited = true;
        _exit.TrySetResult(code);
    }
}

public sealed class FakeSpawner : IChildProcessSpawner
{
    private int _nextProcessId = 4000;

    public RunMechanism Mechanism { get; set; } = RunMechanism.Setsid;

    public List<ChildProcessStart> Starts { get; } = [];

    public List<FakeChild> Children { get; } = [];

    public TimeProvider Time { get; set; } = TimeProvider.System;

    /// <summary>Builds the child for a start request; the default one stays alive until stopped.</summary>
    public Func<ChildProcessStart, FakeChild>? Factory { get; set; }

    public IChildProcess Start(ChildProcessStart request)
    {
        Starts.Add(request);
        var child = Factory?.Invoke(request) ?? new FakeChild(Interlocked.Increment(ref _nextProcessId), Time.GetUtcNow());
        Children.Add(child);
        return child;
    }

    public FakeChild Last
    {
        get { return Children[^1]; }
    }
}

public sealed class FakeInspector : IProcessInspector
{
    public Dictionary<int, ProcessIdentity> Alive { get; } = [];

    public List<int> StopRequests { get; } = [];

    /// <summary>When false, stopping a process fails and it stays alive.</summary>
    public bool CanStop { get; set; } = true;

    public ProcessIdentity? Find(int processId)
    {
        return Alive.GetValueOrDefault(processId);
    }

    public bool IsAlive(int processId)
    {
        return Alive.ContainsKey(processId);
    }

    public Task<bool> StopAsync(int processId, TimeSpan grace, CancellationToken cancellationToken = default)
    {
        StopRequests.Add(processId);
        GroupStopRequests.RemoveAll(request => request == processId);

        if (CanStop)
        {
            Alive.Remove(processId);
        }

        return Task.FromResult(CanStop);
    }

    /// <summary>Groups that are still alive, by process group id.</summary>
    public HashSet<int> Groups { get; } = [];

    public List<int> GroupStopRequests { get; } = [];

    public bool IsGroupAlive(int processGroupId)
    {
        return Groups.Contains(processGroupId);
    }

    public Task<bool> StopGroupAsync(int processGroupId, TimeSpan grace, CancellationToken cancellationToken = default)
    {
        GroupStopRequests.Add(processGroupId);

        if (CanStop)
        {
            Groups.Remove(processGroupId);
        }

        return Task.FromResult(CanStop);
    }
}

/// <summary>Fake systemd units: a set of active unit names.</summary>
public sealed class FakeScopes : IScopeService
{
    public HashSet<string> Active { get; } = [];

    public List<string> StopRequests { get; } = [];

    public bool IsActive(string unit)
    {
        return Active.Contains(unit);
    }

    public Task<bool> StopAsync(string unit, TimeSpan grace, CancellationToken cancellationToken = default)
    {
        StopRequests.Add(unit);
        Active.Remove(unit);
        return Task.FromResult(true);
    }
}

public sealed class FakeRuntimeLauncher : IRuntimeLauncher
{
    public List<RuntimeLaunchRequest> Requests { get; } = [];

    /// <summary>Problems the runtime layer should report, e.g. an invalid profile.</summary>
    public List<RuntimeProblem> Problems { get; } = [];

    public LaunchPlan Plan(Instance instance, RuntimeProfile? profile, RuntimeLaunchRequest request, RuntimeSearchOptions? search = null)
    {
        Requests.Add(request);

        return new LaunchPlan
        {
            Command = new LaunchCommand
            {
                FileName = request.Program,
                Arguments = request.Arguments,
                WorkingDirectory = request.WorkingDirectory,
            },
            Problems = Problems,
        };
    }

    public LaunchPlan PlanTool(Instance instance, RuntimeProfile? profile, RuntimeToolRequest request, RuntimeSearchOptions? search = null)
    {
        throw new NotSupportedException();
    }

    public string ResolvePrefix(Instance instance, RuntimeProfile? profile)
    {
        throw new NotSupportedException();
    }
}

public sealed class FakeRuntimeProfiles : IRuntimeProfileService
{
    public Task<RuntimeProfile?> ResolveForInstanceAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<RuntimeProfile?>(null);
    }

    public Task<IReadOnlyList<RuntimeProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<RuntimeProfile?> FindAsync(string name, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<RuntimeProfile> CreateAsync(RuntimeProfileDraft draft, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<RuntimeProfile> UpdateAsync(Guid id, RuntimeProfileDraft draft, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task AssignAsync(Guid instanceId, Guid? profileId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}

/// <summary>Lists the instances a test made; nothing else about instances matters to the server layer.</summary>
public sealed class FakeInstanceList : IInstanceService
{
    public List<Instance> Instances { get; } = [];

    public Task<IReadOnlyList<Instance>> ListAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<Instance>>([.. Instances]);
    }

    public Task<Instance?> GetAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<Instance?>(Instances.FirstOrDefault(instance => instance.Id == instanceId));
    }

    public Task<InstancePlan> PlanAsync(InstanceCreationRequest request, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<Instance> CreateAsync(
        InstanceCreationRequest request,
        IProgress<InstanceCreationProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotSupportedException();
    }

    public Task DeleteAsync(Guid instanceId, bool deleteFiles, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task RenameAsync(Guid instanceId, string newName, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<PatchStateInfo> DetectPatchStateAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}

/// <summary>Builds instance folders that look enough like an SPT 4.1 install for the locator.</summary>
public sealed class ServerWorld : IDisposable
{
    public const string RuntimeConfig = """
        {"runtimeOptions":{"tfm":"net10.0","frameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.0"},{"name":"Microsoft.AspNetCore.App","version":"10.0.0"}]}}
        """;

    private readonly TempDirectory _temp = new();

    public string Root
    {
        get { return _temp.Path; }
    }

    public TempDirectory Temp
    {
        get { return _temp; }
    }

    public Instance CreateInstance(string name, int port = 6969, string ip = "127.0.0.1", InstanceLayout layout = InstanceLayout.Spt41Plus)
    {
        var path = _temp.Combine(name);

        // An instance is always SPT's merged shape: the runtime sits at the root, next to the executable.
        var runtime = Path.Combine(path, InstancePaths.RuntimeDirectory(layout));

        Directory.CreateDirectory(Path.Combine(runtime, "SPT_Data", "configs"));
        Directory.CreateDirectory(Path.Combine(runtime, "user", "profiles"));
        File.WriteAllText(Path.Combine(path, "EscapeFromTarkov.exe"), "MZ");
        File.WriteAllText(Path.Combine(path, "UnityPlayer.dll"), "unity");
        File.WriteAllText(Path.Combine(path, "winhttp.dll"), "doorstop");
        File.WriteAllText(Path.Combine(path, "doorstop_config.ini"), "[General]");
        Directory.CreateDirectory(Path.Combine(path, "BepInEx", "core"));
        File.WriteAllText(Path.Combine(path, "BepInEx", "core", "BepInEx.Preloader.dll"), "preloader");
        File.WriteAllText(Path.Combine(runtime, "SPT.Server.Linux"), "#!/bin/sh");
        File.WriteAllText(Path.Combine(runtime, "SPT.Server.exe"), "MZ");
        File.WriteAllText(Path.Combine(runtime, "SPT.Server.Linux.runtimeconfig.json"), RuntimeConfig);
        File.WriteAllText(Path.Combine(runtime, "SPT.Server.runtimeconfig.json"), RuntimeConfig);
        WriteHttpConfig(runtime, port, ip);

        return new Instance
        {
            Id = Guid.NewGuid(),
            Name = name,
            Path = path,
            Layout = layout,
            ServerPort = port,
            SptVersion = Core.Domain.SemanticVersion.Parse(layout == InstanceLayout.Spt40 ? "4.0.13" : "4.1.6"),
        };
    }

    public static string RuntimeOf(Instance instance)
    {
        return Path.Combine(instance.Path, Core.Instances.InstancePaths.RuntimeDirectory(instance.Layout));
    }

    public static string HttpConfigOf(Instance instance)
    {
        return Path.Combine(RuntimeOf(instance), "SPT_Data", "configs", "http.json");
    }

    public static void WriteHttpConfig(string runtime, int port, string ip)
    {
        File.WriteAllText(
            Path.Combine(runtime, "SPT_Data", "configs", "http.json"),
            $$"""{"ip":"{{ip}}","port":{{port}},"backendIp":"{{ip}}","backendPort":{{port}},"logRequests":true}"""
        );
    }

    public void Dispose()
    {
        _temp.Dispose();
    }
}
