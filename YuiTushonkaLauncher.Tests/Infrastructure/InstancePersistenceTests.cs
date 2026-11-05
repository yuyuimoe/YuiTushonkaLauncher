using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Tests.Infrastructure;

public sealed class InstancePersistenceTests
{
    [Fact]
    public async Task Instance_RoundTripsIncludingOwnedPatchAndValueObjects()
    {
        await using var database = new TestDatabase();
        await database.InitializeAsync();

        var instanceId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 11, 4, 12, 30, 0, TimeSpan.FromHours(-3));

        await using (var context = database.CreateContext())
        {
            context.Instances.Add(
                new Instance
                {
                    Id = instanceId,
                    Name = "vanilla",
                    Path = "/games/spt/instances/vanilla",
                    BaseGamePath = "/games/spt/base_tarkov41",
                    SptVersion = SemanticVersion.Parse("4.1.6"),
                    TarkovVersion = "1.0.0.0",
                    Layout = InstanceLayout.Spt41Plus,
                    ServerPort = 6969,
                    CreatedAt = createdAt,
                    Patch = new PatchInfo
                    {
                        IsPatched = true,
                        PatchSetVersion = "4.1.6",
                        OriginalSha256 = new string('a', 64),
                        PatchedSha256 = new string('b', 64),
                        BackupPath = "/games/spt/instances/vanilla/EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll.spt-bak",
                        AppliedAt = createdAt,
                    },
                }
            );

            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext())
        {
            var loaded = await context.Instances.SingleAsync(row => row.Id == instanceId);

            loaded.Name.Should().Be("vanilla");
            loaded.SptVersion.Should().Be(SemanticVersion.Parse("4.1.6"));
            loaded.Layout.Should().Be(InstanceLayout.Spt41Plus);
            loaded.CreatedAt.Should().Be(createdAt);
            loaded.Patch.IsPatched.Should().BeTrue();
            loaded.Patch.PatchSetVersion.Should().Be("4.1.6");
            loaded.Patch.BackupPath.Should().EndWith("Assembly-CSharp.dll.spt-bak");
        }
    }

    [Fact]
    public async Task InstanceName_IsUnique()
    {
        await using var database = new TestDatabase();
        await database.InitializeAsync();
        await using var context = database.CreateContext();

        context.Instances.AddRange(CreateInstance("duplicate"), CreateInstance("duplicate"));

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task DeletingAnInstance_CascadesToItsModsAndFiles()
    {
        await using var database = new TestDatabase();
        await database.InitializeAsync();

        var instance = CreateInstance("cascade");
        var mod = new InstalledMod
        {
            Id = Guid.NewGuid(),
            InstanceId = instance.Id,
            Guid = "com.example.cascade",
            Name = "Cascade",
            Kind = ModKind.Client,
            Version = SemanticVersion.Parse("1.2.3"),
            Files =
            [
                new InstalledFile
                {
                    Id = Guid.NewGuid(),
                    RelativePath = "BepInEx/plugins/Cascade/Cascade.dll",
                    Sha256 = new string('c', 64),
                    Length = 1024,
                    IsConfig = false,
                },
            ],
        };
        instance.InstalledMods.Add(mod);

        await using (var context = database.CreateContext())
        {
            context.Instances.Add(instance);
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext())
        {
            var tracked = await context.Instances.SingleAsync(row => row.Id == instance.Id);
            context.Instances.Remove(tracked);
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext())
        {
            (await context.InstalledMods.CountAsync()).Should().Be(0);
            (await context.InstalledFiles.CountAsync()).Should().Be(0);
        }
    }

    [Fact]
    public async Task ModVersion_StoresTheConstraintTextLosslessly()
    {
        await using var database = new TestDatabase();
        await database.InitializeAsync();
        var mod = new Mod
        {
            Id = Guid.NewGuid(),
            ForgeId = 31,
            Guid = "com.donut.scavcat",
            Name = "Scav Cat Trader Mod",
            Author = "DonutxLord",
            Kind = ModKind.Server,
            Versions =
            [
                new ModVersion
                {
                    Id = Guid.NewGuid(),
                    Version = SemanticVersion.Parse("1.0.8"),
                    // The API really does publish values with stray whitespace.
                    SptConstraint = Core.Domain.VersionRange.Parse(" 4.0.10 "),
                    DownloadUrl = "https://sp-mod.com/mod/download/31/scav-cat-trader-mod/1.0.8",
                    ContentLength = 43815,
                },
            ],
        };

        await using (var context = database.CreateContext())
        {
            context.Mods.Add(mod);
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext())
        {
            var version = await context.ModVersions.SingleAsync();

            version.SptConstraint.Raw.Should().Be("4.0.10");
            version.SptConstraint.Satisfies(SemanticVersion.Parse("4.0.10")).Should().BeTrue();
            version.SptConstraint.Satisfies(SemanticVersion.Parse("4.1.6")).Should().BeFalse();
        }
    }

    [Fact]
    public async Task RuntimeProfile_RoundTripsItsEnvironmentMap()
    {
        await using var database = new TestDatabase();
        await database.InitializeAsync();
        var profile = new RuntimeProfile
        {
            Id = Guid.NewGuid(),
            Name = "GE-Proton",
            Kind = RuntimeKind.Proton,
            Executable = "/home/user/.steam/steam/compatibilitytools.d/GE-Proton11-5/proton",
            PrefixPath = "/games/spt40",
            Environment = new Dictionary<string, string> { ["DXVK_HUD"] = "fps", ["MANGOHUD"] = "1" },
            IsDefault = true,
        };

        await using (var context = database.CreateContext())
        {
            context.RuntimeProfiles.Add(profile);
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext())
        {
            var loaded = await context.RuntimeProfiles.SingleAsync();

            loaded.Kind.Should().Be(RuntimeKind.Proton);
            loaded.Environment.Should().BeEquivalentTo(profile.Environment);
        }
    }

    private static Instance CreateInstance(string name)
    {
        return new Instance
        {
            Id = Guid.NewGuid(),
            Name = name,
            Path = $"/games/spt/instances/{name}",
            BaseGamePath = "/games/spt/base_tarkov41",
            SptVersion = SemanticVersion.Parse("4.1.6"),
            TarkovVersion = "1.0.0.0",
            Layout = InstanceLayout.Spt41Plus,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
