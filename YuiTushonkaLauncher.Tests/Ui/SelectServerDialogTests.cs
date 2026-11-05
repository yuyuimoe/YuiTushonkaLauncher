using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Localization;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Localization;
using YuiTushonkaLauncher.ViewModels;

namespace YuiTushonkaLauncher.Tests.Ui;

/// <summary>
/// The picker before a launch: it opens on the server the launcher already uses, says what the choice means (started
/// here, or checked elsewhere), and answers with the chosen entry — or nothing, which cancels the launch.
/// </summary>
[Collection(AvaloniaCollection.Name)]
public sealed class SelectServerDialogTests
{
    private static ServerEntry Remote()
    {
        return new ServerEntry
        {
            Name = "Friend",
            Host = "10.0.0.5",
            Port = 6969,
        };
    }

    [Fact]
    public void ItOpensOnTheServerTheLauncherAlreadyUses()
    {
        var dialog = new SelectServerDialogViewModel(new JsonLocalizer("en"), [ServerEntry.Local, Remote()], preselected: "Friend");

        dialog.Servers.Should().HaveCount(2);
        dialog.Selected!.Name.Should().Be("Friend");
        dialog.CanConfirm.Should().BeTrue();
        dialog.SelectedHint.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TheHintSaysWhetherTheServerIsStartedOrOnlyChecked()
    {
        var dialog = new SelectServerDialogViewModel(
            new JsonLocalizer("en"),
            [ServerEntry.Local, new ServerEntry { Name = "Friend", Host = "10.0.0.5" }]
        );
        var local = dialog.Servers.First(row => row.IsLocal);
        var remote = dialog.Servers.First(row => !row.IsLocal);

        dialog.Selected = local;
        var localHint = dialog.SelectedHint;
        dialog.Selected = remote;
        var remoteHint = dialog.SelectedHint;

        localHint.Should().NotBe(remoteHint);
        dialog.Selected!.Address.Should().Be("10.0.0.5:6969");
    }

    [Fact]
    public void AcceptingClosesWithTheChosenEntryAndCancellingWithNothing()
    {
        var servers = new[]
        {
            ServerEntry.Local,
            new ServerEntry { Name = "Friend", Host = "10.0.0.5" },
        };
        var accepted = new SelectServerDialogViewModel(new JsonLocalizer("en"), servers);
        Core.Server.ServerEntry? chosen = null;
        accepted.Closed += (_, entry) => chosen = entry;

        accepted.Selected = accepted.Servers.First(row => row.Name == "Friend");
        accepted.AcceptCommand.Execute(null);

        chosen.Should().NotBeNull();
        chosen!.Address.Should().Be("10.0.0.5:6969");

        var cancelled = new SelectServerDialogViewModel(new JsonLocalizer("en"), servers);
        Core.Server.ServerEntry? cancelledChoice = ServerEntry.Local;
        cancelled.Closed += (_, entry) => cancelledChoice = entry;

        cancelled.CancelCommand.Execute(null);

        cancelledChoice.Should().BeNull("cancelling the picker must cancel the launch, not pick for the user");
    }
}
