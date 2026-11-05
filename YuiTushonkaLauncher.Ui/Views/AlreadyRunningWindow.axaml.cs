using Avalonia.Controls;
using Avalonia.Interactivity;

namespace YuiTushonkaLauncher.Views;

/// <summary>Shown when a second launcher process starts; it then exits instead of duplicating work.</summary>
public partial class AlreadyRunningWindow : Window
{
    public AlreadyRunningWindow()
    {
        InitializeComponent();
        DataContext = new YuiTushonkaLauncher.ViewModels.AlreadyRunningViewModel(
            new YuiTushonkaLauncher.Localization.JsonLocalizer(Program.LanguageOverride ?? "en")
        );
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
