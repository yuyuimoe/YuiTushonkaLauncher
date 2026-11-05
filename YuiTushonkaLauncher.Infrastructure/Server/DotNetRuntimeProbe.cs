using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Core.Server;

namespace YuiTushonkaLauncher.Infrastructure.Server;

/// <summary>Lists the installed .NET frameworks by asking <c>dotnet --list-runtimes</c>.</summary>
public sealed class DotNetRuntimeProbe(IExecutableFinder finder, IProcessRunner runner) : IDotNetRuntimeProbe
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<FrameworkRequirement>?> ListInstalledAsync(CancellationToken cancellationToken = default)
    {
        var dotnet = finder.Find("dotnet");

        if (dotnet is null)
        {
            return null;
        }

        var lines = new List<string>();
        var progress = new SynchronousProgress<ProcessLine>(line =>
        {
            if (!line.IsError)
            {
                lines.Add(line.Text);
            }
        });

        var result = await runner.RunAsync(
            new LaunchCommand { FileName = dotnet, Arguments = ["--list-runtimes"] },
            progress,
            TimeSpan.FromSeconds(20),
            cancellationToken
        );

        return result.ExitCode == 0 && !result.TimedOut ? Parse(lines) : null;
    }

    /// <summary>Parses <c>Microsoft.NETCore.App 10.0.10 [/usr/share/dotnet/shared/Microsoft.NETCore.App]</c> lines.</summary>
    public static List<FrameworkRequirement> Parse(IEnumerable<string> lines)
    {
        var frameworks = new List<FrameworkRequirement>();

        foreach (var line in lines)
        {
            var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 2 && parts[1].Length > 0 && char.IsDigit(parts[1][0]))
            {
                frameworks.Add(new FrameworkRequirement(parts[0], parts[1]));
            }
        }

        return frameworks;
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value)
        {
            handler(value);
        }
    }
}
