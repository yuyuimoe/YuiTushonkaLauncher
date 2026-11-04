using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace YuiTushonkaLauncher.Views;

/// <summary>
/// The window that manages one instance: mods, server, logs, patching and settings. The launcher opens on the instance
/// list instead, which is the shape every other launcher of this kind uses.
/// </summary>
public partial class InstanceManagerWindow : Window
{
    public InstanceManagerWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
