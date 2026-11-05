using System;

namespace YuiTushonkaLauncher.Navigation;

/// <summary>
/// The rail's request channel. Pages ask to go somewhere through this, and the shell listens; going through a small
/// leaf service instead of the shell itself keeps the container acyclic (the shell owns every page).
/// </summary>
public sealed class ShellNavigation : INavigationService
{
    /// <summary>Raised when a page asks to show a section.</summary>
    public event EventHandler<LauncherSection>? Requested;

    public void Navigate(LauncherSection section)
    {
        Requested?.Invoke(this, section);
    }
}
