using YuiTushonkaLauncher.Core.Localization;

namespace YuiTushonkaLauncher.ViewModels;

/// <summary>
/// The second-instance window's strings. It exists before the container does, so it builds its own localizer from the
/// <c>--lang</c> argument; falling back to English is what that window always did.
/// </summary>
public sealed class AlreadyRunningViewModel : ViewModelBase
{
    public AlreadyRunningViewModel(ILocalizer localizer)
        : base(localizer) { }

    public string Title
    {
        get { return Text("alreadyRunning.title"); }
    }

    public string Body
    {
        get { return Text("alreadyRunning.body"); }
    }

    public string CloseLabel
    {
        get { return Text("alreadyRunning.close"); }
    }
}
