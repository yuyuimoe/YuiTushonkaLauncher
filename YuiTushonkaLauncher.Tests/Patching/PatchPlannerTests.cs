using FluentAssertions;
using Xunit;
using YuiTushonkaLauncher.Core.Patching;
using YuiTushonkaLauncher.Patching;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Patching;

/// <summary>
/// The planner is what stops a mis-detected install from being patched into a corrupted DLL: every
/// size combination resolves to one explicit decision.
/// </summary>
public sealed class PatchPlannerTests : IDisposable
{
    private const long Vanilla = 1000;
    private const long Patched = 1012;

    private readonly TempDirectory _temp = new();
    private readonly PatchPlanner _planner = new();

    private string Root
    {
        get { return _temp.Combine("instance"); }
    }

    private string Target
    {
        get { return Path.Combine(Root, "EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll"); }
    }

    private string Backup
    {
        get { return Target + ".spt-bak"; }
    }

    [Fact]
    public void Plan_WhenTheTargetIsThePrePatchBuild_AppliesTheDelta()
    {
        Write(Target, Vanilla);

        var plan = _planner.Plan(Root, [Delta()]);

        var file = plan.Files.Should().ContainSingle().Subject;
        file.Action.Should().Be(PatchFileAction.Patch);
        file.PatchSourcePath.Should().Be(Target);
        file.CurrentSize.Should().Be(Vanilla);
        plan.CanApply.Should().BeTrue();
        plan.IsFullyPatched.Should().BeFalse();
    }

    [Fact]
    public void Plan_WhenTheTargetIsPatchedAndBackedUp_ReportsNothingToDo()
    {
        Write(Target, Patched);
        Write(Backup, Vanilla);

        var plan = _planner.Plan(Root, [Delta()]);

        plan.Files.Should().ContainSingle().Which.Action.Should().Be(PatchFileAction.AlreadyPatched);
        plan.CanApply.Should().BeFalse();
        plan.IsFullyPatched.Should().BeTrue();
    }

    [Fact]
    public void Plan_WhenTheTargetIsPatchedWithoutABackup_SaysRestoringNeedsAFreshCopy()
    {
        Write(Target, Patched);

        var plan = _planner.Plan(Root, [Delta()]);

        var file = plan.Files.Should().ContainSingle().Subject;
        file.Action.Should().Be(PatchFileAction.AlreadyPatched);
        file.Reason.Should().Contain(".spt-bak");
    }

    [Fact]
    public void Plan_WhenTheTargetIsNeitherBuildAndThereIsNoBackup_Refuses()
    {
        Write(Target, 4321);

        var plan = _planner.Plan(Root, [Delta()]);

        var file = plan.Files.Should().ContainSingle().Subject;
        file.Action.Should().Be(PatchFileAction.Refused);
        file.Reason.Should().Contain("not a build this SPT version supports").And.Contain("damaged");
        plan.CanApply.Should().BeFalse();
    }

    [Fact]
    public void Plan_WhenTheTargetIsNeitherBuildButTheBackupIsThePrePatchBuild_PatchesFromTheBackup()
    {
        Write(Target, 4321);
        Write(Backup, Vanilla);

        var plan = _planner.Plan(Root, [Delta()]);

        var file = plan.Files.Should().ContainSingle().Subject;
        file.Action.Should().Be(PatchFileAction.Patch);
        file.PatchSourcePath.Should().Be(Backup);
        file.Reason.Should().Contain("backup");
    }

    [Fact]
    public void Plan_WhenTheFileIsMissing_ReportsItMissing()
    {
        var plan = _planner.Plan(Root, [Delta()]);

        plan.Files.Should().ContainSingle().Which.Action.Should().Be(PatchFileAction.Missing);
    }

    [Fact]
    public void Plan_WithoutDeltas_ExplainsThereIsNothingToPatch()
    {
        var plan = _planner.Plan(Root, []);

        plan.Files.Should().BeEmpty();
        plan.CanApply.Should().BeFalse();
        plan.Summary.Should().Contain("No deltas");
    }

    [Fact]
    public void Summary_CountsEveryDecision()
    {
        Write(Target, Vanilla);

        var plan = _planner.Plan(Root, [Delta(), Delta("EscapeFromTarkov_Data/Plugins/x86_64/hwecho.dll")]);

        plan.Summary.Should().Contain("1 to patch").And.Contain("1 missing");
    }

    public void Dispose()
    {
        _temp.Dispose();
    }

    private static DeltaDescriptor Delta(string relative = "EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll")
    {
        return new DeltaDescriptor
        {
            RelativePath = relative,
            BackupRelativePath = relative + ".spt-bak",
            DeltaPath = "/deltas/whatever.delta",
            ExpectedInputSize = Vanilla,
            ExpectedOutputSize = Patched,
        };
    }

    private static void Write(string path, long size)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
    }
}
