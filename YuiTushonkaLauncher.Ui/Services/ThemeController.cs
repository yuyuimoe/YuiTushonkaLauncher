using System;
using Avalonia;
using Avalonia.Styling;

namespace YuiTushonkaLauncher.Services;

/// <summary>Applies the theme setting to the running application.</summary>
public interface IThemeController
{
    void Apply(string theme);
}

/// <summary>
/// Sets <see cref="Application.RequestedThemeVariant" />. <c>System</c> is Avalonia's <see cref="ThemeVariant.Default" />,
/// which follows the OS and is the phase's requirement.
/// </summary>
public sealed class ThemeController(Func<Application?> application) : IThemeController
{
    public void Apply(string theme)
    {
        if (application() is not { } app)
        {
            return;
        }

        app.RequestedThemeVariant = theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
