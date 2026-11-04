using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace YuiTushonkaLauncher.Views;

/// <summary>The Forge browser in its own window, opened from "Install mods" or "Install addons".</summary>
public partial class ForgeWindow : Window
{
    public ForgeWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
