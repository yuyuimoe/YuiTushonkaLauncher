using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YuiTushonkaLauncher.Core.Localization;
using YuiTushonkaLauncher.ViewModels;

namespace YuiTushonkaLauncher.ViewModels;

/// <summary>A yes/no question, with an optional checkbox whose value the caller reads after confirming.</summary>
public partial class ConfirmDialogViewModel : ViewModelBase
{
    public ConfirmDialogViewModel(ILocalizer localizer)
        : base(localizer) { }

    /// <summary>Raised by the window's own buttons; the window closes with the boolean.</summary>
    public event EventHandler<bool>? Closed;

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Body { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConfirmLabel { get; set; } = string.Empty;

    /// <summary>True for a message with one Close button: nothing to decide, only to dismiss.</summary>
    [ObservableProperty]
    public partial bool IsInformational { get; set; }

    public bool ShowsCancel
    {
        get { return !IsInformational; }
    }

    [ObservableProperty]
    public partial bool HasCheckbox { get; set; }

    [ObservableProperty]
    public partial string CheckboxLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool CheckboxValue { get; set; }

    public string CancelLabel
    {
        get { return Text("common.cancel"); }
    }

    partial void OnIsInformationalChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowsCancel));
    }

    [RelayCommand]
    private void Accept()
    {
        Closed?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel()
    {
        Closed?.Invoke(this, false);
    }
}
