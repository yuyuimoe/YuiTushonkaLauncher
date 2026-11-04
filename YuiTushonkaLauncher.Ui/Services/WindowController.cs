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
