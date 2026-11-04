using System;
using Avalonia.Controls;
using YuiTushonkaLauncher.Core.Configuration;
using YuiTushonkaLauncher.ViewModels;

namespace YuiTushonkaLauncher.Services;

/// <summary>
/// Applies the "when the game starts" setting to the real window. Kept separate from the view model so the setting can
/// be tested without a desktop: the headless tests use a fake controller and assert which action was requested.
/// </summary>
public sealed class WindowController : IWindowController
{
    private Func<Window?>? _window;

    private Func<Navigation.LauncherSection, Window?>? _manager;

    /// <summary>Binds the manager factory: it returns the window, creating it on first use.</summary>
    public void AttachManager(Func<Navigation.LauncherSection, Window?> manager)
    {
        _manager = manager;
    }

    /// <summary>Shows the manager window on a section, which is how a page-specific window is opened from the home.</summary>
    public void OpenManager(Navigation.LauncherSection section)
    {
        var window = _manager?.Invoke(section);
        if (window is null)
        {
            return;
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        window.Activate();
    }

    /// <summary>Binds the controller to the shell window once it exists.</summary>
    public void Attach(Window window)
    {
        _window = () => window;
    }

    public void Apply(GameStartWindowAction action)
    {
        if (_window?.Invoke() is not { } window)
        {
            return;
        }

        switch (action)
        {
            case GameStartWindowAction.Minimise:
                window.WindowState = WindowState.Minimized;
                break;
            case GameStartWindowAction.Close:
                window.Close();
                break;
            default:
                break;
        }
    }
}
