using System;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using YuiTushonkaLauncher.Core.Localization;

namespace YuiTushonkaLauncher.ViewModels;

/// <summary>
/// Base for every view model that shows text: holds the localizer, refreshes all translated properties when the
/// language changes, and gives long operations one place to report progress and failure.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    protected ViewModelBase(ILocalizer localizer)
    {
        Localizer = localizer;
        Localizer.LanguageChanged += (_, _) => RefreshLocalization();
    }

    protected ILocalizer Localizer { get; }

    /// <summary>How many components hold this view model; used by tests to prove nothing leaks.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>Text for a key, so a subclass property is one line.</summary>
    protected string Text(string key)
    {
        return Localizer[key];
    }

    /// <summary>Re-raises change notifications for every property, which is how a language switch repaints a page.</summary>
    protected void RefreshLocalization()
    {
        foreach (var property in GetType().GetProperties())
        {
            if (property.CanRead && property.GetIndexParameters().Length == 0)
            {
                OnPropertyChanged(new PropertyChangedEventArgs(property.Name));
            }
        }
    }
}

/// <summary>A tiny helper so a view model's long operations all behave the same way.</summary>
public static class ViewModelErrors
{
    /// <summary>The message a failed operation shows: the exception's text, never a stack trace.</summary>
    public static string Describe(Exception exception, ILocalizer localizer)
    {
        var message = exception.Message;
        return localizer.Format("errors.generic", string.IsNullOrWhiteSpace(message) ? exception.GetType().Name : message);
    }
}
