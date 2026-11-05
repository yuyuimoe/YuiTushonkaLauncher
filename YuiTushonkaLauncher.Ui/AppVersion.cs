using System.Reflection;

namespace YuiTushonkaLauncher;

/// <summary>Reads the version the build stamped into the assembly (<c>X.Y.Z-pre-sha[.dirty]</c>).</summary>
internal static class AppVersion
{
    /// <summary>The informational version, or <c>unknown</c> when the assembly carries none.</summary>
    public static string Current
    {
        get
        {
            var assembly = typeof(AppVersion).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return string.IsNullOrWhiteSpace(informational) ? "unknown" : informational;
        }
    }
}
