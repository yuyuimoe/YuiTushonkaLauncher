using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using YuiTushonkaLauncher.Core.Localization;

namespace YuiTushonkaLauncher.ViewModels;

/// <summary>
/// A screen: it carries a busy flag, an error line and a status line, and the base class keeps a failed operation from
/// reaching the window as an exception. Every user-visible sentence a page builds goes through the localizer.
/// </summary>
public abstract partial class PageViewModel : ViewModelBase
{
    protected PageViewModel(ILocalizer localizer)
        : base(localizer) { }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>The last failure, already phrased for a person; empty when nothing failed.</summary>
    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    /// <summary>The last good outcome ("Instance deleted.", "Wrote …").</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    public bool HasError
    {
        get { return !string.IsNullOrEmpty(Error); }
    }

    public bool HasStatus
    {
        get { return !string.IsNullOrEmpty(Status); }
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnBusyChanged(value);
    }

    /// <summary>Hook for a page whose commands depend on the busy flag.</summary>
    protected virtual void OnBusyChanged(bool isBusy) { }

    partial void OnErrorChanged(string value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    partial void OnStatusChanged(string value)
    {
        OnPropertyChanged(nameof(HasStatus));
    }

    /// <summary>Runs an operation with the busy flag set, turning a failure into the error line.</summary>
    protected async Task RunAsync(Func<Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        Error = string.Empty;
        IsBusy = true;
        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            Error = ViewModelErrors.Describe(exception, Localizer);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Clears both lines before a new attempt.</summary>
    protected void ClearMessages()
    {
        Error = string.Empty;
        Status = string.Empty;
    }
}
