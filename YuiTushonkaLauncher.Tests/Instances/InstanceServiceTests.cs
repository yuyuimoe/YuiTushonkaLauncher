using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Infrastructure.FileSystem;
using YuiTushonkaLauncher.Infrastructure.Instances;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Instances;

public sealed class InstanceServiceTests : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private TempDirectory _locations = null!;
    private FileLinkService _links = null!;
    private InstanceService _service = null!;

    public async Task InitializeAsync()
    {
        _database = new TestDatabase();
        await _database.InitializeAsync();
        _locations = new TempDirectory();
        _links = new FileLinkService(NullLogger<FileLinkService>.Instance);
        _service = new InstanceService(
            _database.UnitOfWork,
            _links,
            FakeBaseInstall.CreateProbe(),
            new PatchStateDetector(NullLogger<PatchStateDetector>.Instance),
            NullLogger<InstanceService>.Instance
        );
    }

    public async Task DisposeAsync()
    {
        await _database.DisposeAsync();
        _locations.Dispose();
    }

    [Fact]
    public async Task PlanAsync_DescribesLinksCopiesAndSkips()
    {
        using var install = FakeBaseInstall.CreateSpt41();

        var plan = await _service.PlanAsync(Request(install, "planned"));

        plan.Layout.Should().Be(InstanceLayout.Spt41Plus);
        plan.LinkCount.Should().BeGreaterThan(0);
        plan.CopyCount.Should().BeGreaterThan(0);
        plan.SkipCount.Should().BeGreaterThan(0);
        plan.LinkBytes.Should().BeGreaterThan(0);

        var assemblyEntry = plan.Entries.Single(entry => entry.RelativePath.EndsWith("Assembly-CSharp.dll", StringComparison.Ordinal));
        assemblyEntry.Action.Should().Be(LinkAction.Copy);
    }

    [Fact]
    public async Task CreateAsync_SharesGameFilesAndKeepsThePatchedBinaryPrivate()
    {
        using var install = FakeBaseInstall.CreateSpt41();

        var instance = await _service.CreateAsync(Request(install, "vanilla"));

        instance.Name.Should().Be("vanilla");
        instance.SptVersion.Should().Be(SemanticVersion.Parse("4.1.6"));
        instance.Layout.Should().Be(InstanceLayout.Spt41Plus);
        instance.Patch.IsPatched.Should().BeTrue();

        var baseAssembly = Path.Combine(install.Root, "gamefiles", "EscapeFromTarkov_Data", "Managed", "Assembly-CSharp.dll");
        var instanceAssembly = Path.Combine(instance.Path, "EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll");
        _links.GetLinkCount(instanceAssembly).Should().Be(1, "the patched binary must never be shared");

        var baseBundle = Path.Combine(install.Root, "gamefiles", "EscapeFromTarkov_Data", "Resources", "bundle.bundle");
        var instanceBundle = Path.Combine(instance.Path, "EscapeFromTarkov_Data/Resources/bundle.bundle");
        _links.GetLinkCount(instanceBundle).Should().Be(2, "unchanged game files are hard-linked");
        File.ReadAllText(baseBundle).Should().Be(File.ReadAllText(instanceBundle));
        _links.GetLinkCount(baseAssembly).Should().Be(1, "creating an instance must not touch the base install");
    }

    [Fact]
    public async Task CreateAsync_CreatesPerInstanceDirectoriesAndSkipsBaseUserData()
    {
        using var install = FakeBaseInstall.CreateSpt41();

        var instance = await _service.CreateAsync(Request(install, "private"));

        Directory.Exists(Path.Combine(instance.Path, "BepInEx/plugins")).Should().BeTrue();
        Directory.Exists(Path.Combine(instance.Path, "SPT_Runtime/user/profiles")).Should().BeTrue();
        Directory.Exists(Path.Combine(instance.Path, "SPT_Runtime/user/mods")).Should().BeTrue();

        File.Exists(Path.Combine(instance.Path, "BepInEx/plugins/UserMod/UserMod.dll")).Should().BeFalse("user mods are per instance");
        File.Exists(Path.Combine(instance.Path, "SPT_Runtime/user/profiles/base.json")).Should().BeFalse("profiles are per instance");
        File.Exists(Path.Combine(instance.Path, "BepInEx/cache/chainloader.dat")).Should().BeFalse("caches are regenerated");
        File.Exists(Path.Combine(instance.Path, "BepInEx/plugins/spt/spt-core.dll")).Should().BeTrue("SPT's own plugins are required");
    }

    [Fact]
    public async Task CreateAsync_PersistsTheInstanceAndItsServerProfile()
    {
        using var install = FakeBaseInstall.CreateSpt41();

        var instance = await _service.CreateAsync(Request(install, "stored"));

        var list = await _service.ListAsync();
        list.Should().ContainSingle();
        list[0].Id.Should().Be(instance.Id);
        list[0].TarkovVersion.Should().Be("0.16.9.40743");

        await using var context = _database.CreateContext();
        var profile = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(context.ServerProfiles);
        profile.InstanceId.Should().Be(instance.Id);
        profile.BackendUrl.Should().Contain("6969");
    }

    [Fact]
    public async Task CreateAsync_RejectsAnInvalidName()
    {
        using var install = FakeBaseInstall.CreateSpt41();

        var act = async () => await _service.CreateAsync(Request(install, "bad name/with spaces"));

        await act.Should().ThrowAsync<InstanceValidationException>();
    }

    [Fact]
    public async Task CreateAsync_RejectsAnExistingDestination()
    {
        using var install = FakeBaseInstall.CreateSpt41();
        Directory.CreateDirectory(Path.Combine(_locations.Path, "taken"));

        var act = async () => await _service.CreateAsync(Request(install, "taken"));

        var exception = await act.Should().ThrowAsync<InstanceValidationException>();
        exception.Which.Issues.Should().Contain(issue => issue.Contains("already exists"));
    }

    [Fact]
    public async Task CreateAsync_RollsBackWhenALinkFailsHalfway()
    {
        using var install = FakeBaseInstall.CreateSpt41();
        var failing = new FailingFileLinkService(_links, failAfter: 3);
        var service = new InstanceService(
            _database.UnitOfWork,
            failing,
            FakeBaseInstall.CreateProbe(),
            new PatchStateDetector(NullLogger<PatchStateDetector>.Instance),
            NullLogger<InstanceService>.Instance
        );

        var act = async () =>
            await service.CreateAsync(
                new InstanceCreationRequest
                {
                    Name = "broken",
                    Location = _locations.Path,
                    BaseInstallPath = install.Root,
                }
            );

        await act.Should().ThrowAsync<InvalidOperationException>();
        Directory.Exists(Path.Combine(_locations.Path, "broken")).Should().BeFalse("a half-built instance must be removed");
        (await _service.ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task RenameAsync_MovesTheFolderAndUpdatesTheRow()
    {
        using var install = FakeBaseInstall.CreateSpt41();
        var instance = await _service.CreateAsync(Request(install, "before"));

        await _service.RenameAsync(instance.Id, "after");

        Directory.Exists(Path.Combine(_locations.Path, "before")).Should().BeFalse();
        Directory.Exists(Path.Combine(_locations.Path, "after")).Should().BeTrue();
        var list = await _service.ListAsync();
        list[0].Name.Should().Be("after");
        list[0].Path.Should().EndWith("after");
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheFolderOnlyWhenAsked()
    {
        using var install = FakeBaseInstall.CreateSpt41();
        var keep = await _service.CreateAsync(Request(install, "keep"));
        var remove = await _service.CreateAsync(Request(install, "remove"));

        await _service.DeleteAsync(keep.Id, deleteFiles: false);
        await _service.DeleteAsync(remove.Id, deleteFiles: true);

        Directory.Exists(keep.Path).Should().BeTrue();
        Directory.Exists(remove.Path).Should().BeFalse();
        (await _service.ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task DetectPatchStateAsync_UpdatesTheStoredPatchInfo()
    {
        using var install = FakeBaseInstall.CreateSpt41();
        var instance = await _service.CreateAsync(Request(install, "patchy"));

        // Restore the "original" to simulate a client that was never patched.
        var target = Path.Combine(instance.Path, "EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll");
        await File.WriteAllTextAsync(target, "original-assembly");

        var state = await _service.DetectPatchStateAsync(instance.Id);

        state.State.Should().Be(PatchState.NotPatched);
        (await _service.ListAsync())[0].Patch.IsPatched.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_RejectsAnUnknownInstance()
    {
        var act = async () => await _service.DeleteAsync(Guid.NewGuid(), deleteFiles: false);

        await act.Should().ThrowAsync<InstanceValidationException>();
    }

    [Fact]
    public async Task RenameAsync_RejectsAnInvalidOrTakenName()
    {
        using var install = FakeBaseInstall.CreateSpt41();
        var instance = await _service.CreateAsync(Request(install, "first"));
        Directory.CreateDirectory(Path.Combine(_locations.Path, "taken"));

        var invalid = async () => await _service.RenameAsync(instance.Id, "no spaces please");
        var taken = async () => await _service.RenameAsync(instance.Id, "taken");

        await invalid.Should().ThrowAsync<InstanceValidationException>();
        await taken.Should().ThrowAsync<InstanceValidationException>();
    }

    [Fact]
    public async Task RenameAsync_RejectsAnUnknownInstance()
    {
        var act = async () => await _service.RenameAsync(Guid.NewGuid(), "whatever");

        await act.Should().ThrowAsync<InstanceValidationException>();
    }

    [Fact]
    public async Task DetectPatchStateAsync_RejectsAnUnknownInstance()
    {
        var act = async () => await _service.DetectPatchStateAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InstanceValidationException>();
    }

    [Fact]
    public async Task PlanAsync_RejectsAMissingLocation()
    {
        using var install = FakeBaseInstall.CreateSpt41();
        var request = new InstanceCreationRequest
        {
            Name = "nowhere",
            Location = "/does/not/exist",
            BaseInstallPath = install.Root,
        };

        var act = async () => await _service.PlanAsync(request);

        await act.Should().ThrowAsync<InstanceValidationException>();
    }

    [Fact]
    public async Task PlanAsync_WarnsWhenTheBaseIsMissingDoorstopFiles()
    {
        using var install = FakeBaseInstall.CreateSpt41();
        install.DeleteFile("spt/doorstop_config.ini");

        var plan = await _service.PlanAsync(Request(install, "warned"));

        plan.Warnings.Should().Contain(warning => warning.Contains("doorstop"));
    }

    /// <summary>Wraps a real link service and fails after a number of successful links.</summary>
    private sealed class FailingFileLinkService(Core.Files.IFileLinkService inner, int failAfter) : Core.Files.IFileLinkService
    {
        private int _links;

        public void CreateHardLink(string linkPath, string existingPath)
        {
            if (++_links > failAfter)
            {
                throw new InvalidOperationException("simulated link failure");
            }

            inner.CreateHardLink(linkPath, existingPath);
        }

        public void CreateSymbolicLink(string linkPath, string existingPath, bool isDirectory)
        {
            inner.CreateSymbolicLink(linkPath, existingPath, isDirectory);
        }

        public int GetLinkCount(string path)
        {
            return inner.GetLinkCount(path);
        }

        public bool IsSameVolume(string pathA, string pathB)
        {
            return inner.IsSameVolume(pathA, pathB);
        }

        public long? GetAvailableFreeSpace(string path)
        {
            return inner.GetAvailableFreeSpace(path);
        }

        public void Detach(string path)
        {
            inner.Detach(path);
        }

        public void CopyFile(string sourcePath, string destinationPath, bool overwrite)
        {
            inner.CopyFile(sourcePath, destinationPath, overwrite);
        }

        public Task CopyDirectoryAsync(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            return inner.CopyDirectoryAsync(sourceDirectory, destinationDirectory, cancellationToken);
        }
    }

    private InstanceCreationRequest Request(FakeBaseInstall install, string name)
    {
        return new InstanceCreationRequest
        {
            Name = name,
            Location = _locations.Path,
            BaseInstallPath = install.Root,
        };
    }
}
