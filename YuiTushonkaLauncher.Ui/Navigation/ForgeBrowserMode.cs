namespace YuiTushonkaLauncher.Navigation;

/// <summary>Which job the Forge window is open for: finding a mod, or finding an addon for a mod.</summary>
public enum ForgeBrowserMode
{
    /// <summary>Browse and install mods.</summary>
    Mods,

    /// <summary>Browse and install addons, which need their parent mod installed first.</summary>
    Addons,
}
