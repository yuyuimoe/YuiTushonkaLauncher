namespace YuiTushonkaLauncher.Navigation;

/// <summary>Switches the rail's section. Implemented by the shell so a page can send the user somewhere without
/// knowing how the window is put together.</summary>
public interface INavigationService
{
    void Navigate(LauncherSection section);
}
