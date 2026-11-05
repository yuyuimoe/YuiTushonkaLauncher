using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using YuiTushonkaLauncher.Core.Launch;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Tests.Server;
using YuiTushonkaLauncher.ViewModels;

namespace YuiTushonkaLauncher.Tests.Ui;

/// <summary>
/// D25 in the launch flow: with one server nothing is asked; with more than one the picker decides, and the choice
/// reaches the launch request as the address the game is given. A cancelled picker cancels the launch.
/// </summary>
[Collection(AvaloniaCollection.Name)]
public sealed class LaunchServerPickTests
{
    [Fact]
    public async Task WithOneServerNothingIsAskedAndTheInstanceServerIsUsed()
    {
        var (world, _, launch) = await WorldAsync();
        await using var _ = world;

        await world.Get<LaunchPanelViewModel>().LaunchCommand.ExecuteAsync(null);

        world.Dialogs.ServerDialogs.Should().BeEmpty();
        launch.Requests.Should().ContainSingle().Which.ServerEndpoint.Should().BeNull("null means the instance's own server");
    }

    [Fact]
    public async Task WithTwoServersThePickerDecidesAndTheChoiceReachesTheRequest()
    {
        var (world, list, launch) = await WorldAsync(withRemote: true);
        await using var _ = world;
        world.Dialogs.OnSelectServer = dialog => dialog.Servers.First(row => !row.IsLocal).Entry;

        await world.Get<LaunchPanelViewModel>().LaunchCommand.ExecuteAsync(null);

        world.Dialogs.ServerDialogs.Should().ContainSingle();
        launch.Requests.Should().ContainSingle();
        launch.Requests[0].ServerEndpoint!.ToStringKey().Should().Be("10.0.0.5:6969");
        list.Activations.Should().Contain("Friend", "the picker's remember box is on by default");
    }

    [Fact]
    public async Task CancellingThePickerCancelsTheLaunch()
    {
        var (world, list, launch) = await WorldAsync(withRemote: true);
        await using var _ = world;
        world.Dialogs.OnSelectServer = _ => null;

        await world.Get<LaunchPanelViewModel>().LaunchCommand.ExecuteAsync(null);

        launch.Requests.Should().BeEmpty("a cancelled picker must not launch against a guessed server");
        list.Activations.Should().BeEmpty();
    }

    private static async Task<(UiWorld World, FakeServerList List, CapturingLaunchService Launch)> WorldAsync(bool withRemote = false)
    {
        var list = new FakeServerList();
        if (withRemote)
        {
            await list.SaveAsync(
                new ServerEntry
                {
                    Name = "Friend",
                    Host = "10.0.0.5",
                    Port = 6969,
                }
            );
        }

        var launch = new CapturingLaunchService();
        var world = await UiWorld.CreateAsync(configure: services =>
        {
            services.AddSingleton<IServerList>(list);
            services.AddSingleton<ILaunchService>(launch);
        });

        await world.SeedInstanceAsync("pick-server");
        return (world, list, launch);
    }

    /// <summary>A launch service that records the request and pretends to have started the pair.</summary>
    private sealed class CapturingLaunchService : ILaunchService
    {
        public List<LaunchRequest> Requests { get; } = [];

        public Task<LaunchPreflight> PlanAsync(LaunchRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new LaunchPreflight
                {
                    InstanceId = request.InstanceId,
                    InstanceName = "pick-server",
                    ProfileUsername = "tester",
                    ServerAlreadyRunning = true,
                }
            );
        }

        public Task<LaunchResult> LaunchAsync(
            LaunchRequest request,
            IProgress<LaunchProgress>? progress = null,
            CancellationToken cancellationToken = default
        )
        {
            Requests.Add(request);
            progress?.Report(new LaunchProgress(LaunchStep.Detached, LaunchStepOutcome.Succeeded, "running"));
            return Task.FromResult(
                new LaunchResult
                {
                    InstanceId = request.InstanceId,
                    Succeeded = true,
                    ProfileId = "profile-id",
                    GameCommand = "game",
                }
            );
        }
    }
}
