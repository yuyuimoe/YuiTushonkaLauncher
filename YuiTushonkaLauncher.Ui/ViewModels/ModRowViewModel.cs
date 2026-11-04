using System;
using System.Collections.Generic;
using System.Linq;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Localization;

namespace YuiTushonkaLauncher.ViewModels;

/// <summary>One installed mod as a list row. Reused across filters so typing does not rebuild a thousand objects.</summary>
public sealed class ModRowViewModel : ViewModelBase
{
    public ModRowViewModel(ILocalizer localizer, InstalledMod mod)
        : base(localizer)
    {
        Mod = mod;
        ConfigFiles = [.. mod.Files.Where(file => file.IsConfig).Select(file => file.RelativePath)];
    }

    public InstalledMod Mod { get; }

    public Guid Id
    {
        get { return Mod.Id; }
    }

    public string Name
    {
        get { return Mod.Name; }
    }

    public string Guid
    {
        get { return Mod.Guid; }
    }

    public string Version
    {
        get { return Mod.Version.ToString(); }
    }

    public bool IsPinned
    {
        get { return Mod.IsPinned; }
    }

    public bool IsAddon
    {
        get { return Mod.IsAddon; }
    }

    public string KindText
    {
        get { return Mod.Kind.ToString(); }
    }

    /// <summary>Where the mod's identity came from, which is what a support question asks first.</summary>
    public string RecognitionText
    {
        get { return Mod.Recognition.ToString(); }
    }

    public int FileCount
    {
        get { return Mod.Files.Count; }
    }

    public IReadOnlyList<string> ConfigFiles { get; }

    /// <summary>Case-insensitive match on name or GUID, for the search box.</summary>
    public bool Matches(string needle)
    {
        return Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
            || Guid.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>One config file, with the mod that placed it.</summary>
public sealed class ConfigRowViewModel(ILocalizer localizer, string modName, string path) : ViewModelBase(localizer)
{
    public string ModName { get; } = modName;

    public string Path { get; } = path;
}
