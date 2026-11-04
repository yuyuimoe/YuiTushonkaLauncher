using YuiTushonkaLauncher.Core.Domain;

namespace YuiTushonkaLauncher.Core.Runtime;

/// <summary>What a new Linux profile starts with, so a first run needs no research.</summary>
public static class RuntimeDefaults
{
    /// <summary>Starting variables: the DLL override BepInEx needs under Wine. The user can edit or remove it.</summary>
    public static IReadOnlyDictionary<string, string> NewEnvironment()
    {
        return new Dictionary<string, string> { ["WINEDLLOVERRIDES"] = LaunchOptions.DoorstopOverride };
    }

    /// <summary>
    /// A ready-to-save profile built from what was found, or <c>null</c> when nothing usable exists. Prefers a
    /// GE-Proton build (what SPT players use), then UMU-Proton, then any valid build; the newest of each.
    /// </summary>
    public static RuntimeProfileDraft? Suggest(RuntimeInventory inventory, string name = "Default")
    {
        if (!inventory.IsSupported)
        {
            return null;
        }

        var build = inventory
            .ProtonBuilds.Where(candidate => candidate.IsValid)
            .OrderBy(candidate =>
                candidate.Name.StartsWith("GE-Proton", StringComparison.Ordinal) ? 0
                : candidate.Name.StartsWith("UMU-Proton", StringComparison.Ordinal) ? 1
                : 2
            )
            .FirstOrDefault();

        if (build is not null)
        {
            return new RuntimeProfileDraft
            {
                Name = name,
                Kind = RuntimeKind.Proton,
                Executable = build.Path,
                Environment = NewEnvironment(),
                MakeDefault = true,
            };
        }

        return inventory.Wine is null
            ? null
            : new RuntimeProfileDraft
            {
                Name = name,
                Kind = RuntimeKind.Wine,
                Executable = inventory.Wine,
                Environment = NewEnvironment(),
                MakeDefault = true,
            };
    }
}
