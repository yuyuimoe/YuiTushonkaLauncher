using Microsoft.Extensions.Logging.Abstractions;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Infrastructure.Server;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Server;

/// <summary>The supervisor wired against fakes for everything that is a process, a socket or a clock.</summary>
public sealed class SupervisorHarness : IDisposable
{
    public SupervisorHarness(bool isWindows = false)
    {
        var platform = new YuiTushonkaLauncher.Tests.Runtime.FakePlatform(linux: !isWindows);
        Time = new FastTimeProvider();
        Spawner = new FakeSpawner { Time = Time };
        Client = new FakeSptClient();
        Clients = new FakeClientFactory(Client);

        var paths = World.Temp.CreateAppPaths();
        paths.EnsureCreated();
        Runs = new InstanceRunStore(paths, NullLogger<InstanceRunStore>.Instance);

        Supervisor = new SptServerSupervisor(
            new SptServerLocator(platform),
            DotNet,
            Spawner,
            Clients,
            Ports,
            RuntimeLauncher,
            new FakeRuntimeProfiles(),
            Instances,
            Inspector,
            Runs,
            platform,
            Time,
            NullLogger<SptServerSupervisor>.Instance
        );
    }

    public ServerWorld World { get; } = new();

    public FastTimeProvider Time { get; }

    public FakeSpawner Spawner { get; }

    public FakeSptClient Client { get; }

    public FakeClientFactory Clients { get; }

    public FakePortProbe Ports { get; } = new();

    public FakeDotNetProbe DotNet { get; } = new();

    public FakeRuntimeLauncher RuntimeLauncher { get; } = new();

    public FakeInstanceList Instances { get; } = new();

    public FakeInspector Inspector { get; } = new();

    public InstanceRunStore Runs { get; }

    public SptServerSupervisor Supervisor { get; }

    public Instance AddInstance(string name, int port = 6969, string ip = "127.0.0.1", InstanceLayout layout = InstanceLayout.Spt41Plus)
    {
        var instance = World.CreateInstance(name, port, ip, layout);
        Instances.Instances.Add(instance);
        return instance;
    }

    /// <summary>Waits (really) until a condition holds; the monitor and restart paths run on their own tasks.</summary>
    public static async Task WaitForAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for: {what}");
            }

            await Task.Delay(5);
        }
    }

    public static void Say(ChildProcessStart start, params string[] lines)
    {
        foreach (var line in lines)
        {
            start.OnOutput!(new ProcessLine(false, line));
        }
    }

    public void Dispose()
    {
        Supervisor.Dispose();
        World.Dispose();
    }
}
