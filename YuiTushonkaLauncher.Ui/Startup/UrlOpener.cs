using System.Diagnostics;

namespace YuiTushonkaLauncher.Startup;

/// <summary>Hands a link to the desktop's default browser.</summary>
public interface IUrlOpener
{
    void Open(string url);
}

/// <summary>
/// Opens a URL through the shell (xdg-open on Linux, ShellExecute on Windows). Only absolute http(s) URLs reach it: a
/// link that came from the network must not be able to name a local program.
/// </summary>
public sealed class UrlOpener : IUrlOpener
{
    public void Open(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}
