using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Patching;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Patching;

/// <summary>
/// End-to-end patching against real files: backup, apply, verify, persist, and the restore path.
/// </summary>
public sealed class PatchingServiceTests
{
    private const int VanillaSize = 1000;
    private static readonly int PatchedSize = VanillaSize + FakePatchEngine.PatchMarker.Length;

    [Fact]
    public async Task PatchAsync_BacksUpThenReplacesTheFile()
    {
        await using var context = await PatchingTestContext.CreateAsync();
        context.WriteVanilla(VanillaSize);
        context.AddDelta(VanillaSize, PatchedSize);
        var vanillaBytes = await File.ReadAllBytesAsync(context.TargetPath);

        var report = await context.Service.PatchAsync(context.Instance.Id);

        report.Succeeded.Should().BeTrue();
        var result = report.Files.Should().ContainSingle().Subject;
        result.Outcome.Should().Be(PatchFileOutcome.Patched);
        result.SizeAfter.Should().Be(PatchedSize);
        new FileInfo(context.TargetPath).Length.Should().Be(PatchedSize);
        (await File.ReadAllBytesAsync(context.BackupPath)).Should().Equal(vanillaBytes);
        context.Engine.Applied.Should().ContainSingle().Which.Source.Should().Be(context.TargetPath);
    }

    [Fact]
    public async Task PatchAsync_PersistsTheHashesOfBothBuilds()
    {
        await using var context = await PatchingTestContext.CreateAsync();
        context.WriteVanilla(VanillaSize);
        context.AddDelta(VanillaSize, PatchedSize);

        await context.Service.PatchAsync(context.Instance.Id);

        var patch = await context.ReadPatchInfoAsync();
        patch.IsPatched.Should().BeTrue();
        patch.OriginalSha256.Should().NotBeNullOrEmpty();
        patch.PatchedSha256.Should().NotBeNullOrEmpty();
        patch.OriginalSha256.Should().NotBe(patch.PatchedSha256);
        patch.BackupPath.Should().Be(context.BackupPath);
        patch.AppliedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task PatchAsync_Twice_ChangesNothingTheSecondTime()
    {
        await using var context = await PatchingTestContext.CreateAsync();
        context.WriteVanilla(VanillaSize);
        context.AddDelta(VanillaSize, PatchedSize);

        await context.Service.PatchAsync(context.Instance.Id);
        var afterFirst = await File.ReadAllBytesAsync(context.TargetPath);
        var second = await context.Service.PatchAsync(context.Instance.Id);

        second.Files.Should().ContainSingle().Which.Outcome.Should().Be(PatchFileOutcome.AlreadyPatched);
        (await File.ReadAllBytesAsync(context.TargetPath)).Should().Equal(afterFirst);
        context.Engine.Applied.Should().ContainSingle("the second run must not touch the engine");
    }

    [Fact]
    public async Task PatchAsync_WithAnUnsupportedBuild_RefusesAndLeavesTheFileAlone()
    {
        await using var context = await PatchingTestContext.CreateAsync();
        context.WriteVanilla(4321);
        context.AddDelta(VanillaSize, PatchedSize);

        var report = await context.Service.PatchAsync(context.Instance.Id);

        report.Succeeded.Should().BeFalse();
        report.Files.Should().ContainSingle().Which.Outcome.Should().Be(PatchFileOutcome.Refused);
        new FileInfo(context.TargetPath).Length.Should().Be(4321);
        File.Exists(context.BackupPath).Should().BeFalse();
        context.Engine.Applied.Should().BeEmpty();
        (await context.ReadPatchInfoAsync()).IsPatched.Should().BeFalse();
    }

    [Fact]
    public async Task PatchAsync_WhenTheEngineFails_KeepsTheOriginalAndCleansUp()
    {
        await using var context = await PatchingTestContext.CreateAsync();
        context.WriteVanilla(VanillaSize);
        context.AddDelta(VanillaSize, PatchedSize);
        context.Engine.Failure = new InvalidDataException("the delta is bogus");

        var report = await context.Service.PatchAsync(context.Instance.Id);

        report.Succeeded.Should().BeFalse();
        report.Files.Should().ContainSingle().Which.Outcome.Should().Be(PatchFileOutcome.Failed);
        new FileInfo(context.TargetPath).Length.Should().Be(VanillaSize);
        File.Exists(context.TargetPath + ".spt-patch").Should().BeFalse("the temporary file must never survive");
    }

    [Fact]
    public async Task PatchAsync_WhenTheOutputSizeIsNotWhatTheDeltaPromised_DiscardsIt()
    {
        await using var context = await PatchingTestContext.CreateAsync();
        context.WriteVanilla(VanillaSize);
        context.AddDelta(VanillaSize, PatchedSize);

        // The engine writes a different size than the delta declared: verification must reject it.
        context.Engine.ProducedSizeOverride = PatchedSize + 100;

        var report = await context.Service.PatchAsync(context.Instance.Id);

        report.Succeeded.Should().BeFalse();
        report.Files.Should().ContainSingle().Which.Message.Should().Contain("Nothing was replaced");
        new FileInfo(context.TargetPath).Length.Should().Be(VanillaSize);
    }

    [Fact]
    public async Task PatchAsync_WhenTheTargetWasCorrupted_PatchesFromTheBackup()
    {
        await using var context = await PatchingTestContext.CreateAsync();
        context.WriteVanilla(VanillaSize);
        context.AddDelta(VanillaSize, PatchedSize);
        await context.Service.PatchAsync(context.Instance.Id);
        context.WriteTarget(7);

        var report = await context.Service.PatchAsync(context.Instance.Id);

        report.Files.Should().ContainSingle().Which.Outcome.Should().Be(PatchFileOutcome.Patched);
        new FileInfo(context.TargetPath).Length.Should().Be(PatchedSize);
        context.Engine.Applied.Should().HaveCount(2);
        context.Engine.Applied[0].Source.Should().Be(context.TargetPath);
        context.Engine.Applied[1].Source.Should().Be(context.BackupPath);
    }

    [Fact]
    public async Task PatchAsync_WithNoDeltas_SaysThereIsNothingToDo()
    {
        await using var context = await PatchingTestContext.CreateAsync();
        context.WriteVanilla(VanillaSize);

        var report = await context.Service.PatchAsync(context.Instance.Id);

        report.Files.Should().BeEmpty();
        report.Succeeded.Should().BeTrue();
        report.Summary.Should().Be("Nothing to do.");
    }

    [Fact]
    public async Task PlanAsync_DoesNotWriteAnything()
    {
        await using var context = await PatchingTestContext.CreateAsync();
        context.WriteVanilla(VanillaSize);
        context.AddDelta(VanillaSize, PatchedSize);

        var plan = await context.Service.PlanAsync(context.Instance.Id);

        plan.CanApply.Should().BeTrue();
        File.Exists(context.BackupPath).Should().BeFalse();
        new FileInfo(context.TargetPath).Length.Should().Be(VanillaSize);
    }

    [Fact]
    public async Task RestoreAsync_PutsTheOriginalBackAndRemovesTheBackup()
    {
        await using var context = await PatchingTestContext.CreateAsync();
        context.WriteVanilla(VanillaSize);
        context.AddDelta(VanillaSize, PatchedSize);
        var vanillaBytes = await File.ReadAllBytesAsync(context.TargetPath);
        await context.Service.PatchAsync(context.Instance.Id);

        var report = await context.Service.RestoreAsync(context.Instance.Id);

        report.Succeeded.Should().BeTrue();
        report.Files.Should().ContainSingle().Which.Outcome.Should().Be(PatchFileOutcome.Restored);
        (await File.ReadAllBytesAsync(context.TargetPath)).Should().Equal(vanillaBytes);
        File.Exists(context.BackupPath).Should().BeFalse();
        (await context.ReadPatchInfoAsync()).IsPatched.Should().BeFalse();
    }

    [Fact]
    public async Task RestoreAsync_WhenTheFileAlreadyMatches_KeepsTheBackup()
    {
        await using var context = await PatchingTestContext.CreateAsync();
        context.WriteVanilla(VanillaSize);
        File.Copy(context.TargetPath, context.BackupPath);

        var report = await context.Service.RestoreAsync(context.Instance.Id);

        report.Files.Should().ContainSingle().Which.Outcome.Should().Be(PatchFileOutcome.AlreadyPatched);
        File.Exists(context.BackupPath).Should().BeTrue();
    }

    [Fact]
    public async Task RestoreAsync_WhenTheTargetIsGone_SkipsAndKeepsTheBackup()
    {
        await using var context = await PatchingTestContext.CreateAsync();
        context.WriteVanilla(VanillaSize);
        File.Copy(context.TargetPath, context.BackupPath);
        File.Delete(context.TargetPath);

        var report = await context.Service.RestoreAsync(context.Instance.Id);

        report.Files.Should().ContainSingle().Which.Outcome.Should().Be(PatchFileOutcome.Skipped);
        File.Exists(context.BackupPath).Should().BeTrue();
    }

    [Fact]
    public async Task RestoreAsync_WithoutBackups_ReportsNothing()
    {
        await using var context = await PatchingTestContext.CreateAsync();

        var report = await context.Service.RestoreAsync(context.Instance.Id);

        report.Files.Should().BeEmpty();
        report.Summary.Should().Be("Nothing to do.");
    }

    [Fact]
    public async Task PlanAsync_ForAnUnknownInstance_Throws()
    {
        await using var context = await PatchingTestContext.CreateAsync();

        var act = async () => await context.Service.PlanAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<YuiTushonkaLauncher.Core.Instances.InstanceValidationException>();
    }

    [Fact]
    public async Task PatchAsync_ReportsProgress()
    {
        await using var context = await PatchingTestContext.CreateAsync();
        context.WriteVanilla(VanillaSize);
        context.AddDelta(VanillaSize, PatchedSize);
        var reports = new List<PatchProgress>();

        await context.Service.PatchAsync(context.Instance.Id, new Progress<PatchProgress>(reports.Add));

        // Progress<T> posts to the captured context; the engine reports synchronously on a test thread,
        // so allow the queue to drain before asserting.
        await Task.Delay(50);

        reports.Should().Contain(progress => progress.Percentage == 100);
    }

    [Fact]
    public async Task PatchAsync_WithTheRealEngine_ProducesExactlyTheExpectedBytes()
    {
        await using var context = await PatchingTestContext.CreateWithRealEngineAsync();
        var expected = await context.UseFixtureDeltaAsync();
        var original = await File.ReadAllBytesAsync(context.TargetPath);

        var report = await context.Service.PatchAsync(context.Instance.Id);

        report.Succeeded.Should().BeTrue();
        (await File.ReadAllBytesAsync(context.TargetPath)).Should().Equal(expected);
        (await File.ReadAllBytesAsync(context.BackupPath)).Should().Equal(original);
    }

    [Fact]
    public async Task PatchAsync_WithTheRealEngine_IsIdempotentAndRestorable()
    {
        await using var context = await PatchingTestContext.CreateWithRealEngineAsync();
        var expected = await context.UseFixtureDeltaAsync();
        var original = await File.ReadAllBytesAsync(context.TargetPath);

        await context.Service.PatchAsync(context.Instance.Id);
        var second = await context.Service.PatchAsync(context.Instance.Id);
        second.Files.Should().ContainSingle().Which.Outcome.Should().Be(PatchFileOutcome.AlreadyPatched);
        (await File.ReadAllBytesAsync(context.TargetPath)).Should().Equal(expected);

        var restored = await context.Service.RestoreAsync(context.Instance.Id);

        restored.Files.Should().ContainSingle().Which.Outcome.Should().Be(PatchFileOutcome.Restored);
        (await File.ReadAllBytesAsync(context.TargetPath)).Should().Equal(original);
        File.Exists(context.BackupPath).Should().BeFalse();
    }

    [Fact]
    public async Task PatchAsync_WithTheRealEngine_RefusesAFileOfTheWrongSizeBeforeBackingUp()
    {
        await using var context = await PatchingTestContext.CreateWithRealEngineAsync();
        await context.UseFixtureDeltaAsync();
        await File.WriteAllBytesAsync(context.TargetPath, new byte[777]);

        var report = await context.Service.PatchAsync(context.Instance.Id);

        report.Files.Should().ContainSingle().Which.Outcome.Should().Be(PatchFileOutcome.Refused);
        new FileInfo(context.TargetPath).Length.Should().Be(777);
        File.Exists(context.BackupPath).Should().BeFalse();
    }
}
