using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Instances;

namespace YuiTushonkaLauncher.Infrastructure.Instances;

/// <summary>
/// Reads an instance's patch state from disk. The official launcher keeps <c>&lt;file&gt;.spt-bak</c>
/// next to every patched file, so the backup's presence and the hashes tell us what happened
/// (see <c>docs/plan/03-verified-facts.md#patching</c>).
/// </summary>
public sealed class PatchStateDetector(ILogger<PatchStateDetector> logger)
{
    private const string BackupsSuffix = ".spt-bak";

    /// <summary>Relative path of the binary the SPT patch rewrites.</summary>
    public static string RelativeTargetPath
    {
        get { return InstancePaths.PatchedAssembly; }
    }

    /// <summary>Inspects the instance and returns what can be proven about its patch state.</summary>
    public PatchStateInfo Detect(string instanceRoot)
    {
        var target = Path.Combine(instanceRoot, RelativeTargetPath);
        if (!File.Exists(target))
        {
            return new PatchStateInfo { State = PatchState.Unknown, Reason = "Assembly-CSharp.dll was not found in this instance." };
        }

        var backup = target + BackupsSuffix;
        if (!File.Exists(backup))
        {
            return new PatchStateInfo
            {
                State = PatchState.Unknown,
                TargetPath = target,
                TargetSha256 = TryHash(target),
                Reason = "No .spt-bak backup: the launcher cannot prove whether the patch is applied.",
            };
        }

        var targetHash = TryHash(target);
        var backupHash = TryHash(backup);
        var identical = string.Equals(targetHash, backupHash, StringComparison.OrdinalIgnoreCase);

        logger.LogDebug("Patch state of {Root}: backup present, hashes identical: {Identical}", instanceRoot, identical);

        return new PatchStateInfo
        {
            State = identical ? PatchState.NotPatched : PatchState.Patched,
            TargetPath = target,
            BackupPath = backup,
            TargetSha256 = targetHash,
            BackupSha256 = backupHash,
            Reason = identical
                ? "The binary matches its backup: the patch is not applied (restored)."
                : "The binary differs from its backup: the SPT patch is applied.",
        };
    }

    private static string? TryHash(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
