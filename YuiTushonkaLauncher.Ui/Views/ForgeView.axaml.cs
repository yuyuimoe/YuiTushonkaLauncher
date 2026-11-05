using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using YuiTushonkaLauncher.ViewModels;

namespace YuiTushonkaLauncher.Views;

/// <summary>
/// The Forge browser. Two things here are the view's job rather than the view model's: a double-click on a row opens
/// that mod, because a double-click is an input gesture, and so does a double-click on a dependency.
/// </summary>
public partial class ForgeView : UserControl
{
    public ForgeView()
    {
        InitializeComponent();
    }

    /// <summary>A double-click on a result opens it; a single click already selected it.</summary>
    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ForgePageViewModel page && page.Selected is { } row)
        {
            page.ShowDetailsCommand.Execute(row);
        }
    }

    /// <summary>A double-click on a dependency or dependant navigates to that mod.</summary>
    private void OnReferenceDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not ForgePageViewModel page || e.Source is not Control control)
        {
            return;
        }

        if (control.DataContext is ForgeModReferenceViewModel reference)
        {
            page.NavigateCommand.Execute(reference);
        }
    }
}
