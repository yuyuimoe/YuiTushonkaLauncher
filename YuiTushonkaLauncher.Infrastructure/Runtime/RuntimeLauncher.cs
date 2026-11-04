using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Runtime;

namespace YuiTushonkaLauncher.Infrastructure.Runtime;

/// <summary>
/// Builds the command that runs a program through Proton, Wine or natively, and checks everything it
/// depends on first. Pure planning: nothing is started and nothing is created.
/// </summary>
/// <remarks>
/// <para>
/// The order of preference is the one the plan sets: <c>umu-run</c> with a Proton build, then Proton's own
/// <c>proton</c> script, then plain <c>wine</c>. A Proton profile uses umu whenever it is installed.
/// </para>
/// <para>
/// On Windows every request resolves to a direct, native command and no Linux check runs at all.
/// </para>
/// </remarks>
public sealed class RuntimeLauncher(IRuntimeLocator locator, IExecutableFinder finder, IProcessNames processNames, IPlatformInfo platform)
    : IRuntimeLauncher
{
    private const string GameId = "umu-default";

    private const string PrefixFolderName = "wineprefix";

    /// <inheritdoc />
    public string ResolvePrefix(Instance instance, RuntimeProfile? profile)
    {
        return string.IsNullOrWhiteSpace(profile?.PrefixPath) ? Path.Combine(instance.Path, PrefixFolderName) : profile.PrefixPath;
    }

    /// <inheritdoc />
    public LaunchPlan Plan(Instance instance, RuntimeProfile? profile, RuntimeLaunchRequest request, RuntimeSearchOptions? search = null)
    {
        var problems = new List<RuntimeProblem>();

        if (!File.Exists(request.Program))
        {
            problems.Add(Error(RuntimeProblemCode.ProgramMissing, $"The program '{request.Program}' does not exist.", request.Program));
        }

        if (!Directory.Exists(request.WorkingDirectory))
        {
            problems.Add(
                Error(
                    RuntimeProblemCode.WorkingDirectoryMissing,
                    $"The working directory '{request.WorkingDirectory}' does not exist.",
                    request.WorkingDirectory
                )
            );
        }

        var (environment, extraArguments) = Compose(profile, problems);
        var arguments = request.Arguments.Concat(extraArguments).ToList();

        if (request.Target == ProgramTarget.NativeProgram || !platform.IsLinux)
        {
            return Finish(
                problems,
                Wrap(profile, problems, request.Program, arguments, environment, request.WorkingDirectory),
                prefix: null
            );
        }

        var setup = Resolve(instance, profile, search, problems, environment);

        if (setup is null)
        {
            return new LaunchPlan { Problems = problems };
        }

        if (request.LoadsDoorstop && !LaunchOptions.OverridesWinhttp(setup.Environment.GetValueOrDefault("WINEDLLOVERRIDES")))
        {
            problems.Add(
                Warning(
                    RuntimeProblemCode.DllOverrideMissing,
                    $"WINEDLLOVERRIDES does not contain {LaunchOptions.DoorstopOverride}: BepInEx (and so every client mod) will not load under Wine."
                )
            );
        }

        CheckPrefix(setup.Prefix, problems, tolerateMissing: false);

        var command = setup.Mode switch
        {
            RunMode.Umu => Wrap(
                profile,
                problems,
                setup.Umu!,
                [request.Program, .. arguments],
                setup.Environment,
                request.WorkingDirectory
            ),
            RunMode.ProtonDirect => Wrap(
                profile,
                problems,
                setup.ProtonScript!,
                ["run", request.Program, .. arguments],
                setup.Environment,
                request.WorkingDirectory
            ),
            _ => Wrap(profile, problems, setup.Wine!, [request.Program, .. arguments], setup.Environment, request.WorkingDirectory),
        };

        return Finish(problems, command, setup.Prefix);
    }

    /// <inheritdoc />
    public LaunchPlan PlanTool(Instance instance, RuntimeProfile? profile, RuntimeToolRequest request, RuntimeSearchOptions? search = null)
    {
        var problems = new List<RuntimeProblem>();

        if (!platform.IsLinux)
        {
            problems.Add(Error(RuntimeProblemCode.ProfileKindUnsupported, "Wine prefix tools exist only on Linux."));
            return new LaunchPlan { Problems = problems };
        }

        var (environment, _) = Compose(profile, problems);
        var setup = Resolve(instance, profile, search, problems, environment);

        if (setup is null)
        {
            return new LaunchPlan { Problems = problems };
        }

        CheckPrefix(setup.Prefix, problems, tolerateMissing: request.Tool == PrefixTool.Initialize);

        var verb = request.Tool switch
        {
            PrefixTool.Initialize => new[] { "wineboot", "-u" },
            PrefixTool.WineConfig => ["winecfg"],
            PrefixTool.RegistryEditor => ["regedit"],
            _ => ["winetricks", .. request.Arguments],
        };

        LaunchCommand? command = setup.Mode switch
        {
            RunMode.Umu => new LaunchCommand
            {
                FileName = setup.Umu!,
                Arguments = verb,
                Environment = setup.Environment,
            },
            RunMode.ProtonDirect when request.Tool == PrefixTool.Winetricks => Winetricks(
                setup,
                Path.Combine(setup.Prefix, "pfx"),
                FindProtonWine(setup.ProtonDirectory!),
                problems
            ),
            RunMode.ProtonDirect => new LaunchCommand
            {
                FileName = setup.ProtonScript!,
                Arguments = ["run", .. verb],
                Environment = setup.Environment,
            },
            _ when request.Tool == PrefixTool.Winetricks => Winetricks(setup, setup.Prefix, setup.Wine, problems),
            _ => new LaunchCommand
            {
                FileName = setup.Wine!,
                Arguments = verb,
                Environment = setup.Environment,
            },
        };

        return Finish(problems, command, setup.Prefix);
    }

    private LaunchCommand? Winetricks(RuntimeSetup setup, string prefix, string? wine, List<RuntimeProblem> problems)
    {
        var winetricks = finder.Find("winetricks");

        if (winetricks is null)
        {
            problems.Add(Error(RuntimeProblemCode.ToolMissing, "winetricks was not found on PATH; install it from your package manager."));
            return null;
        }

        var environment = new Dictionary<string, string>(setup.Environment) { ["WINEPREFIX"] = prefix };

        if (wine is not null)
        {
            environment["WINE"] = wine;
        }

        return new LaunchCommand
        {
            FileName = winetricks,
            Arguments = ["winetricks"],
            Environment = environment,
        };
    }

    private RuntimeSetup? Resolve(
        Instance instance,
        RuntimeProfile? profile,
        RuntimeSearchOptions? search,
        List<RuntimeProblem> problems,
        Dictionary<string, string> environment
    )
    {
        if (profile is null)
        {
            problems.Add(
                Error(
                    RuntimeProblemCode.NoProfile,
                    "No runtime profile is selected. On Linux the game needs a Proton or Wine profile; create one first."
                )
            );
            return null;
        }

        var prefix = ResolvePrefix(instance, profile);

        switch (profile.Kind)
        {
            case RuntimeKind.Proton:
                return ResolveProton(profile, search, problems, environment, prefix);
            case RuntimeKind.Wine:
                return ResolveWine(profile, problems, environment, prefix);
            default:
                problems.Add(
                    Error(
                        RuntimeProblemCode.ProfileKindUnsupported,
                        $"The runtime profile '{profile.Name}' is Native, which cannot run a Windows program on Linux. Choose a Proton or Wine profile."
                    )
                );
                return null;
        }
    }

    private RuntimeSetup? ResolveProton(
        RuntimeProfile profile,
        RuntimeSearchOptions? search,
        List<RuntimeProblem> problems,
        Dictionary<string, string> environment,
        string prefix
    )
    {
        if (string.IsNullOrWhiteSpace(profile.Executable))
        {
            problems.Add(
                Error(RuntimeProblemCode.ProtonNotConfigured, $"The runtime profile '{profile.Name}' does not name a Proton build.")
            );
            return null;
        }

        var directory = ProtonDirectoryOf(profile.Executable);

        if (!Directory.Exists(directory))
        {
            problems.Add(Error(RuntimeProblemCode.ProtonMissing, $"The Proton build '{directory}' does not exist.", directory));
            return null;
        }

        if (RuntimeLocator.CheckBuild(directory) is { } invalid)
        {
            problems.Add(Error(RuntimeProblemCode.ProtonInvalid, invalid, directory));
            return null;
        }

        var umu = locator.FindUmuRun(search);

        if (umu is not null)
        {
            environment["WINEPREFIX"] = prefix;
            environment["PROTONPATH"] = directory;
            environment["GAMEID"] = GameId;
            environment["STORE"] = "none";

            return new RuntimeSetup(RunMode.Umu, prefix, environment) { Umu = umu, ProtonDirectory = directory };
        }

        problems.Add(Warning(RuntimeProblemCode.UmuMissing, "umu-run was not found; Proton will run directly, without the umu runtime."));

        if (finder.Find("python3") is null)
        {
            problems.Add(Error(RuntimeProblemCode.PythonMissing, "Proton's 'proton' script needs python3, which was not found on PATH."));
            return null;
        }

        environment["STEAM_COMPAT_DATA_PATH"] = prefix;

        if (SteamRootOf(directory) is { } steamRoot)
        {
            environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = steamRoot;
        }
        else
        {
            problems.Add(
                Warning(
                    RuntimeProblemCode.SteamRootUnknown,
                    $"Cannot tell which Steam folder '{directory}' belongs to; Proton may refuse to start."
                )
            );
        }

        return new RuntimeSetup(RunMode.ProtonDirect, prefix, environment)
        {
            ProtonDirectory = directory,
            ProtonScript = Path.Combine(directory, "proton"),
        };
    }

    private RuntimeSetup? ResolveWine(
        RuntimeProfile profile,
        List<RuntimeProblem> problems,
        Dictionary<string, string> environment,
        string prefix
    )
    {
        string? wine;

        if (string.IsNullOrWhiteSpace(profile.Executable))
        {
            wine = finder.Find("wine");

            if (wine is null)
            {
                problems.Add(
                    Error(RuntimeProblemCode.WineMissing, "wine was not found on PATH; install it or name a wine binary in the profile.")
                );
                return null;
            }
        }
        else
        {
            wine = profile.Executable;

            if (!File.Exists(wine))
            {
                problems.Add(Error(RuntimeProblemCode.WineMissing, $"The wine binary '{wine}' does not exist.", wine));
                return null;
            }
        }

        environment["WINEPREFIX"] = prefix;
        return new RuntimeSetup(RunMode.Wine, prefix, environment) { Wine = wine };
    }

    private void CheckPrefix(string prefix, List<RuntimeProblem> problems, bool tolerateMissing)
    {
        var inspection = PrefixInspector.Inspect(prefix);

        if (inspection.State == PrefixState.Broken && !(tolerateMissing && inspection.LooksLikePrefix))
        {
            problems.Add(Error(RuntimeProblemCode.PrefixBroken, inspection.Reason ?? "The prefix is not usable.", prefix));
            return;
        }

        if (PrefixInspector.FindUnwritableFolder(prefix) is { } folder)
        {
            problems.Add(
                Error(RuntimeProblemCode.PrefixNotWritable, $"Cannot write to '{folder}', which the prefix '{prefix}' needs.", folder)
            );
            return;
        }

        if (inspection.State == PrefixState.Missing && !tolerateMissing)
        {
            problems.Add(
                Warning(
                    RuntimeProblemCode.PrefixNotCreated,
                    $"The prefix '{prefix}' does not exist yet; it is created on the first run, which takes a while.",
                    prefix
                )
            );
        }
    }

    /// <summary>Wraps a command in <c>gamemoderun</c> when the profile asks for it and it is installed.</summary>
    private LaunchCommand? Wrap(
        RuntimeProfile? profile,
        List<RuntimeProblem> problems,
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        string? workingDirectory
    )
    {
        if (profile is { UseGameMode: true } && platform.IsLinux)
        {
            var gamemoderun = finder.Find("gamemoderun");

            if (gamemoderun is null)
            {
                problems.Add(
                    Warning(
                        RuntimeProblemCode.GameModeMissing,
                        "GameMode is turned on but 'gamemoderun' was not found; running without it."
                    )
                );
            }
            else
            {
                if (!processNames.Running().Contains("gamemoded", StringComparer.Ordinal))
                {
                    problems.Add(
                        Warning(
                            RuntimeProblemCode.GameModeNotRunning,
                            "GameMode is installed but its daemon (gamemoded) is not running; it will be started on demand or have no effect."
                        )
                    );
                }

                return new LaunchCommand
                {
                    FileName = gamemoderun,
                    Arguments = [fileName, .. arguments],
                    Environment = environment,
                    WorkingDirectory = workingDirectory,
                };
            }
        }

        return new LaunchCommand
        {
            FileName = fileName,
            Arguments = arguments,
            Environment = environment,
            WorkingDirectory = workingDirectory,
        };
    }

    /// <summary>The profile's variables, then the ones in its extra-arguments line, plus that line's plain arguments.</summary>
    private static (Dictionary<string, string> Environment, IReadOnlyList<string> Arguments) Compose(
        RuntimeProfile? profile,
        List<RuntimeProblem> problems
    )
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);

        if (profile is null)
        {
            return (environment, []);
        }

        var extra = LaunchOptions.Parse(profile.ExtraArguments);

        foreach (var pair in profile.Environment)
        {
            environment[pair.Key] = pair.Value;
        }

        foreach (var pair in extra.Environment)
        {
            environment[pair.Key] = pair.Value;
        }

        foreach (var message in LaunchOptions.ValidateEnvironment(environment).Concat(LaunchOptions.ValidateArguments(extra.Arguments)))
        {
            problems.Add(Error(RuntimeProblemCode.EnvironmentInvalid, message));
        }

        return (environment, extra.Arguments);
    }

    private static LaunchPlan Finish(List<RuntimeProblem> problems, LaunchCommand? command, string? prefix)
    {
        return new LaunchPlan
        {
            Command = problems.Any(problem => problem.IsError) ? null : command,
            Problems = problems,
            PrefixPath = prefix,
        };
    }

    private static string ProtonDirectoryOf(string executable)
    {
        return string.Equals(Path.GetFileName(executable), "proton", StringComparison.Ordinal)
            ? Path.GetDirectoryName(executable) ?? executable
            : executable;
    }

    private static string? FindProtonWine(string directory)
    {
        return new[] { "files", "dist" }.Select(folder => Path.Combine(directory, folder, "bin", "wine")).FirstOrDefault(File.Exists);
    }

    /// <summary>The Steam folder a build belongs to, which Proton wants as its client install path.</summary>
    private static string? SteamRootOf(string protonDirectory)
    {
        var parent = Directory.GetParent(protonDirectory);

        if (parent is null)
        {
            return null;
        }

        if (string.Equals(parent.Name, "compatibilitytools.d", StringComparison.Ordinal))
        {
            return parent.Parent?.FullName;
        }

        // .../Steam/steamapps/common/Proton X → .../Steam
        if (string.Equals(parent.Name, "common", StringComparison.Ordinal) && parent.Parent?.Name == "steamapps")
        {
            return parent.Parent.Parent?.FullName;
        }

        return null;
    }

    private static RuntimeProblem Error(RuntimeProblemCode code, string message, string? path = null)
    {
        return new RuntimeProblem(RuntimeProblemSeverity.Error, code, message, path);
    }

    private static RuntimeProblem Warning(RuntimeProblemCode code, string message, string? path = null)
    {
        return new RuntimeProblem(RuntimeProblemSeverity.Warning, code, message, path);
    }

    private enum RunMode
    {
        Umu,
        ProtonDirect,
        Wine,
    }

    private sealed record RuntimeSetup(RunMode Mode, string Prefix, Dictionary<string, string> Environment)
    {
        public string? Umu { get; init; }

        public string? ProtonDirectory { get; init; }

        public string? ProtonScript { get; init; }

        public string? Wine { get; init; }
    }
}
