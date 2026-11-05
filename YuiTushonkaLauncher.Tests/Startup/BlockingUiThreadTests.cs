using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using YuiTushonkaLauncher.Core.Abstractions;
using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Infrastructure.Persistence;
using YuiTushonkaLauncher.Startup;
using YuiTushonkaLauncher.Tests.Infrastructure;
using YuiTushonkaLauncher.Tests.Qa;
using YuiTushonkaLauncher.ViewModels;

namespace YuiTushonkaLauncher.Tests.Startup;

/// <summary>
/// The window is built on the UI thread <em>before</em> its message loop runs. Anything the startup path waits on must
/// therefore never post its continuation back to that thread: the launcher deadlocked on exactly this, opening no window
/// at all, because constructing the process spawner probed <c>systemd-run</c> and blocked on a result that the
/// dispatcher was supposed to deliver.
/// </summary>
[Collection(SerialCollection.Name)]
public sealed class BlockingUiThreadTests
{
    [Fact]
    public void TheShellAndTheSpawnerResolveWithoutARunningMessageLoop()
    {
        Exception? failure = null;
        var finished = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                // A context that queues and never runs: a UI thread that has not started pumping, which is the state the
                // launcher's own startup is in when it builds the window.
                SynchronizationContext.SetSynchronizationContext(new NotPumpingContext());
                using var temp = new TempDirectory();
                var paths = temp.CreateAppPaths();
                paths.EnsureCreated();
                using var services = new ServiceCollection().AddLauncherServices(paths).BuildServiceProvider();
                services.GetRequiredService<DatabaseInitializer>().InitializeAsync().GetAwaiter().GetResult();
                services.GetRequiredService<IChildProcessSpawner>();
                services.GetRequiredService<ISptServerSupervisor>();
                services.GetRequiredService<ShellViewModel>();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                finished.Set();
            }
        })
        {
            IsBackground = true,
            Name = "blocking-ui-thread",
        };

        thread.Start();

        finished.Wait(TimeSpan.FromSeconds(60)).Should().BeTrue("the startup path must not need the message loop to run");
        failure.Should().BeNull();
    }

    [Fact]
    public void ConstructingTheSpawnerTouchesNothingOutside()
    {
        // The strategy asks the machine about systemd; the constructor must not, so a window that is only being built
        // pays nothing and cannot block.
        var runner = new CountingRunner();
        var spawner = new YuiTushonkaLauncher.Infrastructure.Server.ChildProcessSpawner(
            new TestPlatform(),
            new TestFinder(),
            runner,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<YuiTushonkaLauncher.Infrastructure.Server.ChildProcessSpawner>.Instance
        );

        runner.Calls.Should().Be(0, "the constructor only records what it was given");
        _ = spawner.Strategy;
        runner.Calls.Should().BeGreaterThan(0, "deciding the strategy is what asks the machine");
    }

    /// <summary>A context that accepts work and never runs it, like a UI thread whose loop has not started.</summary>
    private sealed class NotPumpingContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _pending = new();

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_pending)
            {
                _pending.Enqueue((d, state));
            }
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            d(state);
        }
    }

    /// <summary>Counts the commands it is asked to run; answers success so strategy selection completes.</summary>
    private sealed class CountingRunner : IProcessRunner
    {
        public int Calls { get; private set; }

        public Task<ProcessRunResult> RunAsync(
            LaunchCommand command,
            IProgress<ProcessLine>? output = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default
        )
        {
            Calls++;
            return Task.FromResult(new ProcessRunResult(0));
        }
    }

    /// <summary>Every executable exists, so the Linux branches are taken.</summary>
    private sealed class TestFinder : IExecutableFinder
    {
        public string? Find(string name)
        {
            return "/usr/bin/" + name;
        }
    }

    /// <summary>Linux, so the systemd branch is the one that runs.</summary>
    private sealed class TestPlatform : IPlatformInfo
    {
        public bool IsWindows
        {
            get { return false; }
        }

        public bool IsLinux
        {
            get { return true; }
        }

        public bool IsMacOs
        {
            get { return true; }
        }

        public string Name
        {
            get { return "linux"; }
        }
    }
}
