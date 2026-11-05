using YuiTushonkaLauncher.Core.Runtime;

namespace YuiTushonkaLauncher.Infrastructure.Runtime;

/// <summary>The real platform.</summary>
public sealed class PlatformInfo : IPlatformInfo
{
    public bool IsLinux
    {
        get { return OperatingSystem.IsLinux(); }
    }

    public bool IsWindows
    {
        get { return OperatingSystem.IsWindows(); }
    }
}
