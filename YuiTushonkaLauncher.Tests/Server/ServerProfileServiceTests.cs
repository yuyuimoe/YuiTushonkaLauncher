using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Server;
using YuiTushonkaLauncher.Infrastructure.Server;
using YuiTushonkaLauncher.Tests.Infrastructure;
using YuiTushonkaLauncher.Tests.Runtime;

namespace YuiTushonkaLauncher.Tests.Server;

public sealed class ServerProfileServiceTests : IAsyncLifetime
{
    private readonly SupervisorHarness _h = new();

    private readonly TestDatabase _database = new();

    private Instance _instance = null!;

    private ServerProfileService _service = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _instance = _h.AddInstance("one");
        await Persist(_instance);
        _service = Service();
    }

    public async Task DisposeAsync()
    {
        _h.Dispose();
        await _database.DisposeAsync();
    }

    private ServerProfileService Service()
    {
        return new ServerProfileService(
            _h.Instances,
            new SptServerLocator(new FakePlatform(linux: true)),
            _h.Clients,
            _h.Supervisor,
            _database.UnitOfWork,
            _h.World.Temp.CreateAppPaths(),
            _h.Time
        );
    }

    private async Task Persist(Instance instance)
    {
        await _database.UnitOfWork.ExecuteAsync(
            async (context, token) =>
            {
                context.Instances.Add(
                    new Instance
                    {
                        Id = instance.Id,
                        Name = instance.Name,
                        Path = instance.Path,
                        BaseGamePath = "/base",
                        SptVersion = SemanticVersion.Parse("4.1.6"),
                        TarkovVersion = "1.0.0.0",
                        Layout = instance.Layout,
                        ServerPort = instance.ServerPort,
                        CreatedAt = DateTimeOffset.UtcNow,
                        ServerProfile = new ServerProfile
                        {
                            Id = Guid.NewGuid(),
                            Port = instance.ServerPort,
                            BackendUrl = "https://127.0.0.1:6969",
                            UpdatedAt = DateTimeOffset.UtcNow,
                        },
                    }
                );
                await Task.CompletedTask;
            }
        );
    }

    private SptProfileSummary AddProfile(
        string name,
        string edition = "Standard",
        string? id = null,
        bool withFile = true,
        string fileText = "{\"info\":{}}"
    )
    {
        var profile = FakeSptClient.NewProfile(name, edition, id) with { PendingWipe = false };
        _h.Client.Profiles.Add(profile);

        if (withFile)
        {
            File.WriteAllText(Path.Combine(ServerWorld.RuntimeOf(_instance), "user", "profiles", profile.ProfileId + ".json"), fileText);
        }

        return profile;
    }

    private string BackupDirectory()
    {
        return Path.Combine(_h.World.Temp.Combine("data"), "backups", "profiles", _instance.Name);
    }

    private async Task<string?> LastProfileIdAsync()
    {
        await using var context = _database.CreateContext();
        return (await context.ServerProfiles.AsNoTracking().SingleAsync()).LastProfileId;
    }

    // ---- listing ----

    [Fact]
    public async Task List_ReturnsWhatTheServerHas()
    {
        AddProfile("yui");
        AddProfile("maria", "Left Behind");

        var profiles = await _service.ListAsync(_instance.Id);

        profiles.Select(profile => profile.Username).Should().Equal("yui", "maria");
    }

    [Fact]
    public async Task EveryOperation_SaysSoClearly_WhenTheServerIsNotRunning()
    {
        _h.Client.Up = false;

        var act = () => _service.ListAsync(_instance.Id);

        var failure = (await act.Should().ThrowAsync<ServerNotRunningException>()).Which;
        failure.Message.Should().Contain("'one'").And.Contain("https://127.0.0.1:6969").And.Contain("Start it first");
    }

    [Fact]
    public async Task EveryOperation_RefusesAnInstanceThatDoesNotExist()
    {
        var act = () => _service.ListAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<ProfileOperationException>().WithMessage("*does not exist*");
    }

    [Fact]
    public async Task AnotherInstancesServerOnTheSamePort_IsNeverMistakenForThisOne()
    {
        // 'other' is running on the port 'one' is configured for; talking to it would change the wrong profiles.
        var other = _h.AddInstance("other", port: 6969);
        await _h.Supervisor.StartAsync(other);

        var act = () => _service.ListAsync(_instance.Id);

        (await act.Should().ThrowAsync<ServerNotRunningException>()).Which.Message.Should().Contain("belongs to instance 'other'");
    }

    // ---- creating ----

    [Fact]
    public async Task Create_RegistersTheProfile_AndReturnsTheListAfterwards()
    {
        var profiles = await _service.CreateAsync(_instance.Id, "maria", "Left Behind");

        profiles.Should().ContainSingle().Which.Username.Should().Be("maria");
        _h.Client.Calls.Should().Contain("register:maria:Left Behind");
    }

    [Fact]
    public async Task Create_RefusesAnEditionTheServerDoesNotOffer_EvenThoughTheServerWouldAcceptIt()
    {
        var act = () => _service.CreateAsync(_instance.Id, "maria", "NoSuchEdition");

        (await act.Should().ThrowAsync<ProfileOperationException>())
            .Which.Message.Should()
            .Contain("'NoSuchEdition'")
            .And.Contain("Left Behind, Standard");
        _h.Client.Calls.Should().NotContain(call => call.StartsWith("register", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\nname")]
    [InlineData("tab\tname")]
    public async Task Create_RefusesABlankNameOrOneWithControlCharacters(string name)
    {
        var act = () => _service.CreateAsync(_instance.Id, name, "Standard");

        await act.Should().ThrowAsync<ProfileOperationException>().WithMessage("*1 to 64 characters*");
        _h.Client.Calls.Should().NotContain(call => call.StartsWith("register", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Create_RefusesAVeryLongName_ButAcceptsTheLongestAllowed()
    {
        await _service.CreateAsync(_instance.Id, new string('a', 64), "Standard");

        var act = () => _service.CreateAsync(_instance.Id, new string('b', 65), "Standard");

        await act.Should().ThrowAsync<ProfileOperationException>();
    }

    [Fact]
    public async Task Create_TrimsTheName()
    {
        await _service.CreateAsync(_instance.Id, "  maria  ", "Standard");

        _h.Client.Profiles.Should().ContainSingle().Which.Username.Should().Be("maria");
    }

    [Fact]
    public async Task Create_RefusesADuplicateName_IgnoringCase()
    {
        AddProfile("Maria");

        var act = () => _service.CreateAsync(_instance.Id, "maria", "Standard");

        await act.Should().ThrowAsync<ProfileOperationException>().WithMessage("*already exists*");
    }

    [Fact]
    public async Task Create_SaysSo_WhenTheServerRefuses()
    {
        _h.Client.RefuseChanges = true;

        var act = () => _service.CreateAsync(_instance.Id, "maria", "Standard");

        await act.Should().ThrowAsync<ProfileOperationException>().WithMessage("*refused*");
    }

    // ---- deleting and wiping ----

    [Theory]
    [InlineData("delete")]
    [InlineData("wipe")]
    public async Task Destructive_Operations_RefuseToRunWithoutConfirmation(string operation)
    {
        AddProfile("maria");

        var act = () =>
            operation == "delete"
                ? _service.DeleteAsync(_instance.Id, "maria", confirmed: false)
                : _service.WipeAsync(_instance.Id, "maria", confirmed: false);

        await act.Should().ThrowAsync<ProfileOperationException>().WithMessage("*was not confirmed*");
        _h.Client.Calls.Should()
            .NotContain(call => call.StartsWith("remove", StringComparison.Ordinal) || call.StartsWith("wipe", StringComparison.Ordinal));
        Directory.Exists(BackupDirectory()).Should().BeFalse("nothing happened, so there is nothing to back up");
    }

    [Fact]
    public async Task Delete_CopiesTheProfileFileFirst_AndTheCopyIsIdentical()
    {
        var profile = AddProfile("maria", fileText: "{\"info\":{\"username\":\"maria\"},\"progress\":\"lots\"}");

        var result = await _service.DeleteAsync(_instance.Id, "maria", confirmed: true);

        result.Accepted.Should().BeTrue();
        result.Profiles.Should().BeEmpty();
        result.BackupPath.Should().StartWith(BackupDirectory()).And.Contain(profile.ProfileId).And.EndWith("Z.json");
        File.ReadAllText(result.BackupPath!).Should().Be("{\"info\":{\"username\":\"maria\"},\"progress\":\"lots\"}");
    }

    [Fact]
    public async Task Delete_TakesTheBackupBeforeTheServerIsAsked()
    {
        AddProfile("maria");
        var backupExisted = false;
        _h.Client.BeforeChange = call =>
            backupExisted =
                call == "remove:maria" && Directory.Exists(BackupDirectory()) && Directory.GetFiles(BackupDirectory()).Length == 1;

        await _service.DeleteAsync(_instance.Id, "maria", confirmed: true);

        backupExisted.Should().BeTrue("a copy made after the deletion would copy nothing");
    }

    [Fact]
    public async Task Delete_ChangesNothing_WhenTheBackupCannotBeMade()
    {
        var profile = AddProfile("maria");
        Directory.CreateDirectory(Path.GetDirectoryName(BackupDirectory())!);
        File.WriteAllText(BackupDirectory(), "a file where the backup folder should be");

        var act = () => _service.DeleteAsync(_instance.Id, "maria", confirmed: true);

        await act.Should().ThrowAsync<IOException>();
        _h.Client.Calls.Should().NotContain("remove:maria");
        _h.Client.Profiles.Should().ContainSingle(item => item.ProfileId == profile.ProfileId);
    }

    [Fact]
    public async Task Delete_HasNothingToBackUp_ForAProfileThatWasNeverSaved()
    {
        AddProfile("maria", withFile: false);

        var result = await _service.DeleteAsync(_instance.Id, "maria", confirmed: true);

        result.BackupPath.Should().BeNull();
        result.Accepted.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_NamesTheProfilesThereAre_WhenTheNameIsWrong()
    {
        AddProfile("yui");

        var act = () => _service.DeleteAsync(_instance.Id, "ghost", confirmed: true);

        (await act.Should().ThrowAsync<ProfileOperationException>()).Which.Message.Should().Contain("'ghost'").And.Contain("It has: yui");
    }

    [Fact]
    public async Task Delete_MatchesTheNameExactly_NotIgnoringCase()
    {
        AddProfile("Maria");

        var act = () => _service.DeleteAsync(_instance.Id, "maria", confirmed: true);

        await act.Should().ThrowAsync<ProfileOperationException>("deleting 'maria' must never delete 'Maria'");
        _h.Client.Profiles.Should().HaveCount(1);
    }

    [Fact]
    public async Task Delete_TellsWhereTheBackupIs_WhenTheServerRefuses()
    {
        AddProfile("maria");
        _h.Client.RefuseChanges = true;

        var act = () => _service.DeleteAsync(_instance.Id, "maria", confirmed: true);

        (await act.Should().ThrowAsync<ProfileOperationException>())
            .Which.Message.Should()
            .Contain("refused")
            .And.Contain(BackupDirectory());
    }

    [Fact]
    public async Task Delete_ForgetsItAsTheLastProfileUsed()
    {
        var profile = AddProfile("maria");
        await _service.SelectAsync(_instance.Id, "maria");
        (await LastProfileIdAsync()).Should().Be(profile.ProfileId);

        await _service.DeleteAsync(_instance.Id, "maria", confirmed: true);

        (await LastProfileIdAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Delete_LeavesTheLastProfile_WhenAnotherOneWasDeleted()
    {
        var kept = AddProfile("yui");
        AddProfile("maria");
        await _service.SelectAsync(_instance.Id, "yui");

        await _service.DeleteAsync(_instance.Id, "maria", confirmed: true);

        (await LastProfileIdAsync()).Should().Be(kept.ProfileId);
    }

    [Fact]
    public async Task Wipe_BacksUpTheFile_AndKeepsTheProfilesEdition()
    {
        AddProfile("maria", "Left Behind", fileText: "{\"progress\":\"lots\"}");

        var result = await _service.WipeAsync(_instance.Id, "maria", confirmed: true);

        _h.Client.Calls.Should().Contain("wipe:maria:Left Behind");
        File.ReadAllText(result.BackupPath!).Should().Be("{\"progress\":\"lots\"}");
        result.Profiles.Should().ContainSingle().Which.PendingWipe.Should().BeTrue();
    }

    [Fact]
    public async Task Wipe_TellsWhereTheBackupIs_WhenTheServerRefuses()
    {
        AddProfile("maria");
        _h.Client.RefuseChanges = true;

        var act = () => _service.WipeAsync(_instance.Id, "maria", confirmed: true);

        (await act.Should().ThrowAsync<ProfileOperationException>())
            .Which.Message.Should()
            .Contain("refused to wipe")
            .And.Contain(BackupDirectory());
    }

    [Fact]
    public async Task TwoBackupsOfTheSameProfile_NeverOverwriteEachOther()
    {
        AddProfile("maria", fileText: "first");
        var first = await _service.WipeAsync(_instance.Id, "maria", confirmed: true);

        _h.Time.Advance(TimeSpan.FromSeconds(5));
        File.WriteAllText(
            Path.Combine(ServerWorld.RuntimeOf(_instance), "user", "profiles", _h.Client.Profiles[0].ProfileId + ".json"),
            "second"
        );
        var second = await _service.WipeAsync(_instance.Id, "maria", confirmed: true);

        first.BackupPath.Should().NotBe(second.BackupPath);
        File.ReadAllText(first.BackupPath!).Should().Be("first");
        File.ReadAllText(second.BackupPath!).Should().Be("second");
    }

    // ---- selecting ----

    [Fact]
    public async Task Select_LogsInOnTheServer_AndRemembersTheProfile()
    {
        var profile = AddProfile("maria");

        var selected = await _service.SelectAsync(_instance.Id, "maria");

        selected.ProfileId.Should().Be(profile.ProfileId);
        _h.Client.Calls.Should().Contain("login:maria");
        (await LastProfileIdAsync()).Should().Be(profile.ProfileId);
        (await _service.GetLastUsedAsync(_instance.Id))!.Username.Should().Be("maria");
    }

    [Fact]
    public async Task Select_RefusesAProfileTheServerCouldNotLoad()
    {
        _h.Client.Profiles.Add(FakeSptClient.NewProfile("broken", "Standard") with { InvalidOrUnloadable = true });

        var act = () => _service.SelectAsync(_instance.Id, "broken");

        await act.Should().ThrowAsync<ProfileOperationException>().WithMessage("*could not load*");
        (await LastProfileIdAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Select_RemembersNothing_WhenTheServerRejectsTheLogin()
    {
        AddProfile("maria");
        _h.Client.RejectLogin = true;

        var act = () => _service.SelectAsync(_instance.Id, "maria");

        await act.Should().ThrowAsync<ProfileOperationException>().WithMessage("*did not accept*");
        (await LastProfileIdAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetLastUsed_IsNull_WhenNothingWasSelected_OrTheProfileIsGone()
    {
        (await _service.GetLastUsedAsync(_instance.Id)).Should().BeNull();

        AddProfile("maria");
        await _service.SelectAsync(_instance.Id, "maria");
        _h.Client.Profiles.Clear();

        (await _service.GetLastUsedAsync(_instance.Id)).Should().BeNull();
    }
}
