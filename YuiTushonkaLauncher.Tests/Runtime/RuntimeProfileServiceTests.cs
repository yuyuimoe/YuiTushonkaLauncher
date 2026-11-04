using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Infrastructure.Runtime;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Runtime;

public sealed class RuntimeProfileServiceTests
{
    private static RuntimeProfileDraft Draft(string name = "GE", Action<RuntimeProfileDraftBuilder>? configure = null)
    {
        var builder = new RuntimeProfileDraftBuilder { Name = name };
        configure?.Invoke(builder);
        return builder.Build();
    }

    private static async Task<(TestDatabase Database, RuntimeProfileService Service)> CreateAsync()
    {
        var database = new TestDatabase();
        await database.InitializeAsync();
        return (database, new RuntimeProfileService(database.UnitOfWork));
    }

    private static async Task<Instance> AddInstanceAsync(TestDatabase database, string name = "main", Guid? profileId = null)
    {
        var instance = new Instance
        {
            Id = Guid.NewGuid(),
            Name = name,
            Path = "/games/" + name,
            BaseGamePath = "/games/base",
            SptVersion = SemanticVersion.Parse("4.1.6"),
            TarkovVersion = "1.0.0.0",
            Layout = InstanceLayout.Spt41Plus,
            CreatedAt = DateTimeOffset.UtcNow,
            RuntimeProfileId = profileId,
        };

        await using var context = database.CreateContext();
        context.Instances.Add(instance);
        await context.SaveChangesAsync();
        return instance;
    }

    [Fact]
    public async Task CreateAsync_StoresEveryFieldAndTheyOutliveTheServiceAndTheContext()
    {
        var (database, service) = await CreateAsync();
        await using var _ = database;

        await service.CreateAsync(
            Draft(
                "GE-Proton",
                draft =>
                {
                    draft.Kind = RuntimeKind.Proton;
                    draft.Executable = "/steam/compatibilitytools.d/GE-Proton11-1";
                    draft.PrefixPath = "/games/prefix";
                    draft.Environment = new Dictionary<string, string> { ["DXVK_HUD"] = "fps", ["WINEDLLOVERRIDES"] = "winhttp=n,b" };
                    draft.ExtraArguments = "MANGOHUD=1 --disable-software-renderer";
                    draft.UseGameMode = true;
                }
            )
        );

        // A brand-new service over the same database: what an app restart does.
        var reloaded = await new RuntimeProfileService(database.UnitOfWork).FindAsync("GE-Proton");

        reloaded.Should().NotBeNull();
        reloaded!.Kind.Should().Be(RuntimeKind.Proton);
        reloaded.Executable.Should().Be("/steam/compatibilitytools.d/GE-Proton11-1");
        reloaded.PrefixPath.Should().Be("/games/prefix");
        reloaded
            .Environment.Should()
            .BeEquivalentTo(new Dictionary<string, string> { ["DXVK_HUD"] = "fps", ["WINEDLLOVERRIDES"] = "winhttp=n,b" });
        reloaded.ExtraArguments.Should().Be("MANGOHUD=1 --disable-software-renderer");
        reloaded.UseGameMode.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_TheFirstProfileBecomesTheDefaultAndALaterOneDoesNot()
    {
        var (database, service) = await CreateAsync();
        await using var _ = database;

        var first = await service.CreateAsync(Draft("one"));
        var second = await service.CreateAsync(Draft("two"));

        first.IsDefault.Should().BeTrue();
        second.IsDefault.Should().BeFalse();
    }

    [Fact]
    public async Task CreateAsync_MakeDefaultTakesTheFlagFromTheOldDefault()
    {
        var (database, service) = await CreateAsync();
        await using var _ = database;
        await service.CreateAsync(Draft("one"));

        await service.CreateAsync(Draft("two", draft => draft.MakeDefault = true));

        (await service.ListAsync()).Where(profile => profile.IsDefault).Select(profile => profile.Name).Should().Equal("two");
    }

    [Fact]
    public async Task CreateAsync_RejectsADuplicateNameAndTrimsTheName()
    {
        var (database, service) = await CreateAsync();
        await using var _ = database;
        await service.CreateAsync(Draft("same"));

        var action = () => service.CreateAsync(Draft("  same  "));

        (await action.Should().ThrowAsync<RuntimeProfileException>()).Which.Message.Should().Contain("already exists");
        (await service.CreateAsync(Draft("  padded "))).Name.Should().Be("padded");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_RejectsAnEmptyName(string name)
    {
        var (database, service) = await CreateAsync();
        await using var _ = database;

        var action = () => service.CreateAsync(Draft(name));

        (await action.Should().ThrowAsync<RuntimeProfileException>()).Which.Message.Should().Contain("name");
    }

    [Fact]
    public async Task CreateAsync_RejectsAProtonProfileWithoutABuild()
    {
        var (database, service) = await CreateAsync();
        await using var _ = database;

        var action = () => service.CreateAsync(Draft("p", draft => draft.Kind = RuntimeKind.Proton));

        (await action.Should().ThrowAsync<RuntimeProfileException>()).Which.Message.Should().Contain("Proton build");
    }

    [Theory]
    [InlineData("1BAD", "x")]
    [InlineData("WINEPREFIX", "/other")]
    [InlineData("OK", "line\nbreak")]
    public async Task CreateAsync_RejectsAnInvalidEnvironment(string name, string value)
    {
        var (database, service) = await CreateAsync();
        await using var _ = database;

        var action = () =>
            service.CreateAsync(Draft("env", draft => draft.Environment = new Dictionary<string, string> { [name] = value }));

        await action.Should().ThrowAsync<RuntimeProfileException>();
    }

    [Fact]
    public async Task CreateAsync_ValidatesVariablesHiddenInsideTheExtraArgumentsLineToo()
    {
        var (database, service) = await CreateAsync();
        await using var _ = database;

        var action = () => service.CreateAsync(Draft("env", draft => draft.ExtraArguments = "PROTONPATH=/x --flag"));

        (await action.Should().ThrowAsync<RuntimeProfileException>()).Which.Message.Should().Contain("PROTONPATH");
    }

    [Fact]
    public async Task CreateAsync_RejectsOverlongOrNulPaths()
    {
        var (database, service) = await CreateAsync();
        await using var _ = database;

        var tooLong = () => service.CreateAsync(Draft("a", draft => draft.PrefixPath = new string('x', 2000)));
        var withNul = () => service.CreateAsync(Draft("b", draft => draft.Executable = "/x\0y"));
        var longArguments = () => service.CreateAsync(Draft("c", draft => draft.ExtraArguments = new string('x', 2000)));

        await tooLong.Should().ThrowAsync<RuntimeProfileException>();
        await withNul.Should().ThrowAsync<RuntimeProfileException>();
        await longArguments.Should().ThrowAsync<RuntimeProfileException>();
    }

    [Fact]
    public async Task CreateAsync_NormalisesBlankOptionalFieldsToNull()
    {
        var (database, service) = await CreateAsync();
        await using var _ = database;

        var saved = await service.CreateAsync(
            Draft(
                "w",
                draft =>
                {
                    draft.Kind = RuntimeKind.Wine;
                    draft.Executable = "  ";
                    draft.PrefixPath = "";
                    draft.ExtraArguments = " ";
                }
            )
        );

        saved.Executable.Should().BeNull();
        saved.PrefixPath.Should().BeNull();
        saved.ExtraArguments.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_ChangesFieldsAndKeepsTheDefaultFlag()
    {
        var (database, service) = await CreateAsync();
        await using var _ = database;
        var profile = await service.CreateAsync(Draft("old"));

        var updated = await service.UpdateAsync(profile.Id, Draft("new", draft => draft.PrefixPath = "/elsewhere"));

        updated.Name.Should().Be("new");
        updated.PrefixPath.Should().Be("/elsewhere");
        updated.IsDefault.Should().BeTrue("an update that does not ask for default must not take it away");
    }

    [Fact]
    public async Task UpdateAsync_CanMoveTheDefaultFlagButKeepsExactlyOne()
    {
        var (database, service) = await CreateAsync();
        await using var _ = database;
        await service.CreateAsync(Draft("one"));
        var second = await service.CreateAsync(Draft("two"));

        await service.UpdateAsync(second.Id, Draft("two", draft => draft.MakeDefault = true));

        (await service.ListAsync()).Where(profile => profile.IsDefault).Select(profile => profile.Name).Should().Equal("two");
    }

    [Fact]
    public async Task UpdateAsync_RejectsAnotherProfilesNameButAllowsItsOwn()
    {
        var (database, service) = await CreateAsync();
        await using var _ = database;
        await service.CreateAsync(Draft("one"));
        var second = await service.CreateAsync(Draft("two"));

        var clash = () => service.UpdateAsync(second.Id, Draft("one"));

        await clash.Should().ThrowAsync<RuntimeProfileException>();
        (await service.UpdateAsync(second.Id, Draft("two", draft => draft.UseGameMode = true))).UseGameMode.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_OfAVanishedProfileSaysSo()
    {
        var (database, service) = await CreateAsync();
        await using var _ = database;

        var action = () => service.UpdateAsync(Guid.NewGuid(), Draft());

        (await action.Should().ThrowAsync<RuntimeProfileException>()).Which.Message.Should().Contain("no longer exists");
    }

    [Fact]
    public async Task DeleteAsync_ReleasesInstancesAndHandsTheDefaultToTheNextProfile()
    {
        var (database, service) = await CreateAsync();
        await using var _ = database;
        var one = await service.CreateAsync(Draft("alpha"));
        var two = await service.CreateAsync(Draft("beta"));
        var instance = await AddInstanceAsync(database, profileId: one.Id);

        await service.DeleteAsync(one.Id);

        await using var context = database.CreateContext();
        (await context.Instances.SingleAsync(row => row.Id == instance.Id)).RuntimeProfileId.Should().BeNull();
        (await service.ListAsync())
            .Should()
            .ContainSingle()
            .Which.Should()
            .Match<RuntimeProfile>(profile => profile.Id == two.Id && profile.IsDefault);
    }

    [Fact]
    public async Task DeleteAsync_OfANonDefaultProfileLeavesTheDefaultAlone()
    {
        var (database, service) = await CreateAsync();
        await using var _ = database;
        await service.CreateAsync(Draft("keep"));
        var other = await service.CreateAsync(Draft("drop"));

        await service.DeleteAsync(other.Id);

        (await service.ListAsync()).Should().ContainSingle().Which.IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_OfAnUnknownIdIsANoOp()
    {
        var (database, service) = await CreateAsync();
        await using var _ = database;

        var action = () => service.DeleteAsync(Guid.NewGuid());

        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ResolveForInstanceAsync_PrefersTheAssignedProfileThenTheDefaultThenNothing()
    {
        var (database, service) = await CreateAsync();
        await using var _ = database;
        var instance = await AddInstanceAsync(database);

        (await service.ResolveForInstanceAsync(instance.Id)).Should().BeNull("no profile exists yet");

        var defaultProfile = await service.CreateAsync(Draft("default"));
        var own = await service.CreateAsync(Draft("own"));

        (await service.ResolveForInstanceAsync(instance.Id))!.Id.Should().Be(defaultProfile.Id);

        await service.AssignAsync(instance.Id, own.Id);
        (await service.ResolveForInstanceAsync(instance.Id))!.Id.Should().Be(own.Id);

        await service.AssignAsync(instance.Id, null);
        (await service.ResolveForInstanceAsync(instance.Id))!.Id.Should().Be(defaultProfile.Id);
    }

    [Fact]
    public async Task AssignAsync_RejectsAGhostInstanceAndAGhostProfile()
    {
        var (database, service) = await CreateAsync();
        await using var _ = database;
        var instance = await AddInstanceAsync(database);

        var ghostInstance = () => service.AssignAsync(Guid.NewGuid(), null);
        var ghostProfile = () => service.AssignAsync(instance.Id, Guid.NewGuid());

        await ghostInstance.Should().ThrowAsync<RuntimeProfileException>();
        await ghostProfile.Should().ThrowAsync<RuntimeProfileException>();
    }

    [Fact]
    public async Task ListAsync_IsSortedByName()
    {
        var (database, service) = await CreateAsync();
        await using var _ = database;
        await service.CreateAsync(Draft("b"));
        await service.CreateAsync(Draft("a"));

        (await service.ListAsync()).Select(profile => profile.Name).Should().Equal("a", "b");
    }

    /// <summary>The draft is an immutable record with required members; this keeps the tests short.</summary>
    public sealed class RuntimeProfileDraftBuilder
    {
        public string Name { get; set; } = "x";

        public RuntimeKind Kind { get; set; } = RuntimeKind.Wine;

        public string? Executable { get; set; }

        public string? PrefixPath { get; set; }

        public IReadOnlyDictionary<string, string> Environment { get; set; } = new Dictionary<string, string>();

        public string? ExtraArguments { get; set; }

        public bool UseGameMode { get; set; }

        public bool MakeDefault { get; set; }

        public RuntimeProfileDraft Build()
        {
            return new RuntimeProfileDraft
            {
                Name = Name,
                Kind = Kind,
                Executable = Executable,
                PrefixPath = PrefixPath,
                Environment = Environment,
                ExtraArguments = ExtraArguments,
                UseGameMode = UseGameMode,
                MakeDefault = MakeDefault,
            };
        }
    }
}
