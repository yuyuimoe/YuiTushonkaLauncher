using System;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Localization;

namespace YuiTushonkaLauncher.ViewModels;

/// <summary>One instance in the list: everything a card shows, already formatted.</summary>
public sealed class InstanceCardViewModel : ViewModelBase
{
    public InstanceCardViewModel(ILocalizer localizer, Instance instance, bool isActive)
        : base(localizer)
    {
        Instance = instance;
        IsActive = isActive;
    }

    public Instance Instance { get; }

    public Guid Id
    {
        get { return Instance.Id; }
    }

    public string Name
    {
        get { return Instance.Name; }
    }

    /// <summary>The letter on the card's tile: there is no thumbnail to show, and an initial reads better than nothing.</summary>
    public string Initial
    {
        get { return Instance.Name.Length > 0 ? Instance.Name[..1].ToUpperInvariant() : "?"; }
    }

    /// <summary>One line under the name: the versions, for a card that has no room for four labels.</summary>
    public string VersionLine
    {
        get { return $"SPT {Instance.SptVersion} · EFT {Instance.TarkovVersion}"; }
    }

    public string ModsLine
    {
        get { return Localizer.Format("instances.mods", Instance.InstalledMods.Count(mod => !mod.IsAddon)); }
    }

    public bool IsActive { get; }

    public string SptVersion
    {
        get { return Localizer.Format("instances.sptVersion", Instance.SptVersion.ToString()); }
    }

    public string TarkovVersion
    {
        get { return Localizer.Format("instances.tarkovVersion", Instance.TarkovVersion); }
    }

    public string ModCount
    {
        get { return Localizer.Format("instances.mods", Instance.InstalledMods.Count(mod => !mod.IsAddon)); }
    }

    public string PatchStateText
    {
        get { return Localizer["patch." + PatchKey]; }
    }

    public string LastLaunchText
    {
        get
        {
            return Instance.LastLaunchedAt is { } when
                ? Localizer.Format(
                    "instances.lastLaunch",
                    when.LocalDateTime.ToString("g", System.Globalization.CultureInfo.CurrentCulture)
                )
                : Localizer["instances.never"];
        }
    }

    public string ActiveLabel
    {
        get { return Localizer["instances.active"]; }
    }

    public string PathText
    {
        get { return Instance.Path; }
    }

    /// <summary>Which of the three patch words applies, from the stored state rather than a fresh disk scan.</summary>
    public string PatchKey
    {
        get
        {
            return Instance.Patch.IsPatched switch
            {
                true => "patched",
                false when Instance.Patch.BackupPath is not null => "clean",
                _ => "unknown",
            };
        }
    }
}
