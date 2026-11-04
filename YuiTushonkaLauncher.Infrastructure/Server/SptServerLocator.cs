using System.Text.Json;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Instances;
using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Core.Server;

namespace YuiTushonkaLauncher.Infrastructure.Server;

/// <summary>Finds the server of an instance from its files: the executable, <c>http.json</c> and the framework the server needs.</summary>
public sealed class SptServerLocator(IPlatformInfo platform) : ISptServerLocator
{
    /// <inheritdoc />
    public SptServerLocation Locate(Instance instance)
    {
        var runtime = Path.Combine(instance.Path, LinkPolicy.RuntimeDirectory(instance.Layout).Replace('/', Path.DirectorySeparatorChar));
        var configPath = Path.Combine(runtime, "SPT_Data", "configs", "http.json");
        var profiles = Path.Combine(runtime, "user", "profiles");
        var problems = new List<ServerProblem>();

        // The Linux file is named "SPT.Server.Linux": Path.ChangeExtension would take ".Linux" for an extension and read the Windows runtimeconfig.
        var baseName = platform.IsWindows ? "SPT.Server" : "SPT.Server.Linux";
        var executable = Path.Combine(runtime, platform.IsWindows ? baseName + ".exe" : baseName);

        if (!Directory.Exists(runtime))
        {
            problems.Add(Error(ServerProblemCode.WorkingDirectoryMissing, $"The server folder '{runtime}' does not exist.", runtime));
        }
        else if (!File.Exists(executable))
        {
            problems.Add(Error(ServerProblemCode.ExecutableMissing, $"The server executable '{executable}' does not exist.", executable));
        }

        var endpoint = new ServerEndpoint(ServerHttpConfig.DefaultHost, ServerEndpoint.DefaultPort);

        if (!File.Exists(configPath))
        {
            problems.Add(Error(ServerProblemCode.ConfigMissing, $"The server configuration '{configPath}' does not exist.", configPath));
        }
        else
        {
            var (read, error) = ServerHttpConfig.Read(configPath);

            if (read is null)
            {
                problems.Add(Error(ServerProblemCode.ConfigInvalid, error!, configPath));
            }
            else
            {
                endpoint = read;
            }
        }

        if (endpoint.ListensOnAllInterfaces)
        {
            problems.Add(
                new ServerProblem(
                    RuntimeProblemSeverity.Warning,
                    ServerProblemCode.ListensOnAllInterfaces,
                    $"The server listens on every network interface ('ip' is '{endpoint.Host}' in http.json): other machines on the network can reach it. "
                        + "Set 'ip' to 127.0.0.1 if you play alone.",
                    configPath
                )
            );
        }

        return new SptServerLocation
        {
            ExecutablePath = File.Exists(executable) ? executable : null,
            WorkingDirectory = runtime,
            ConfigPath = configPath,
            ProfilesDirectory = profiles,
            Endpoint = endpoint,
            Requirements = ReadRequirements(Path.Combine(runtime, baseName + ".runtimeconfig.json")),
            Problems = problems,
        };
    }

    private static List<FrameworkRequirement> ReadRequirements(string path)
    {
        var requirements = new List<FrameworkRequirement>();

        if (!File.Exists(path))
        {
            return requirements;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));

            if (
                !document.RootElement.TryGetProperty("runtimeOptions", out var options)
                || !options.TryGetProperty("frameworks", out var frameworks)
                || frameworks.ValueKind != JsonValueKind.Array
            )
            {
                return requirements;
            }

            foreach (var framework in frameworks.EnumerateArray())
            {
                if (
                    framework.TryGetProperty("name", out var name)
                    && framework.TryGetProperty("version", out var version)
                    && name.GetString() is { Length: > 0 } frameworkName
                    && version.GetString() is { Length: > 0 } frameworkVersion
                )
                {
                    requirements.Add(new FrameworkRequirement(frameworkName, frameworkVersion));
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // An unreadable runtimeconfig only means the framework check has nothing to compare with.
        }

        return requirements;
    }

    private static ServerProblem Error(ServerProblemCode code, string message, string path)
    {
        return new ServerProblem(RuntimeProblemSeverity.Error, code, message, path);
    }
}
