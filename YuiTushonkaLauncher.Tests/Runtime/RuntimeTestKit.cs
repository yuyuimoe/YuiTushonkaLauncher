using System.Runtime.Versioning;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YuiTushonkaLauncher.Core.Domain;
using YuiTushonkaLauncher.Core.Runtime;
using YuiTushonkaLauncher.Infrastructure.Runtime;
using YuiTushonkaLauncher.Tests.Infrastructure;

namespace YuiTushonkaLauncher.Tests.Runtime;

/// <summary>A fact that only runs where a real Linux shell and file modes exist.</summary>
public sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip = "Linux only";
        }
    }
}

public sealed class FakePlatform(bool linux) : IPlatformInfo
{
    public bool IsLinux { get; } = linux;

    public bool IsWindows { get; } = !linux;
}

/// <summary>Resolves program names from a fixed table, so no test depends on what the host has installed.</summary>
public sealed class FakeFinder(params (string Name, string Path)[] programs) : IExecutableFinder
{
    private readonly Dictionary<string, string> _programs = programs.ToDictionary(pair => pair.Name, pair => pair.Path);

    public string? Find(string name)
    {
        return _programs.GetValueOrDefault(name);
    }
}

public sealed class FakeProcessNames(params string[] names) : IProcessNames
{
    public IReadOnlyCollection<string> Running()
    {
        return names;
    }
}

/// <summary>Records what it was asked to run and lets a test decide what "running" does.</summary>
public sealed class FakeRunner : IProcessRunner
{
    public List<LaunchCommand> Commands { get; } = [];

    public Func<LaunchCommand, Task<ProcessRunResult>> Behavior { get; set; } = _ => Task.FromResult(new ProcessRunResult(0));

    public Task<ProcessRunResult> RunAsync(
        LaunchCommand command,
        IProgress<ProcessLine>? output = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        Commands.Add(command);
        output?.Report(new ProcessLine(false, "fake output"));
        return Behavior(command);
    }
}

/// <summary>A throw-away home directory, game folder, instance and Proton builds.</summary>
public sealed class RuntimeWorld : IDisposable
{
    public const string UmuPath = "/fake/bin/umu-run";

    private readonly TempDirectory _temp = new();

    public RuntimeWorld()
    {
        Home = _temp.Combine("home");
        Directory.CreateDirectory(Home);
        InstanceRoot = _temp.Combine("instance");
        GameDirectory = Path.Combine(InstanceRoot, "gamefiles");
        Directory.CreateDirectory(GameDirectory);
        Game = Path.Combine(GameDirectory, "EscapeFromTarkov.exe");
        File.WriteAllText(Game, "MZ");

        Instance = new Instance
        {
            Id = Guid.NewGuid(),
            Name = "test",
            Path = InstanceRoot,
            BaseGamePath = _temp.Combine("base"),
            SptVersion = SemanticVersion.Parse("4.1.6"),
            TarkovVersion = "1.0.0.0",
            Layout = InstanceLayout.Spt41Plus,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public string Home { get; }

    public string InstanceRoot { get; }

    public string GameDirectory { get; }

    public string Game { get; }

    public Instance Instance { get; }

    public string SteamRoot
    {
        get { return Path.Combine(Home, ".local", "share", "Steam"); }
    }

    public string ToolsRoot
    {
        get { return Path.Combine(SteamRoot, "compatibilitytools.d"); }
    }

    public string Combine(params string[] parts)
    {
        return _temp.Combine(parts);
    }

    /// <summary>Makes a folder read-only for its owner, so writes under it fail with a permission error.</summary>
    [SupportedOSPlatform("linux")]
    public static void Lock(string folder)
    {
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
    }

    /// <summary>Undoes <see cref="Lock" /> so the temp directory can be deleted.</summary>
    [SupportedOSPlatform("linux")]
    public static void Unlock(string folder)
    {
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>Creates a Proton build folder; the flags remove one required piece each.</summary>
    public string CreateProton(
        string name,
        string? root = null,
        bool script = true,
        bool manifest = true,
        bool files = true,
        string? version = null
    )
    {
        var directory = Path.Combine(root ?? ToolsRoot, name);
        Directory.CreateDirectory(directory);

        if (script)
        {
            File.WriteAllText(Path.Combine(directory, "proton"), "#!/usr/bin/env python3\n");
        }

        if (manifest)
        {
            File.WriteAllText(Path.Combine(directory, "compatibilitytool.vdf"), "\"compatibilitytools\" {}");
        }

        if (files)
        {
            Directory.CreateDirectory(Path.Combine(directory, "files", "bin"));
            File.WriteAllText(Path.Combine(directory, "files", "bin", "wine"), "wine");
        }

        if (version is not null)
        {
            File.WriteAllText(Path.Combine(directory, "version"), version);
        }

        return directory;
    }

    /// <summary>A prefix folder holding the registry files Wine writes.</summary>
    public string CreatePrefix(string name = "prefix", bool inPfx = false)
    {
        var prefix = Combine(name);
        var root = inPfx ? Path.Combine(prefix, "pfx") : prefix;
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "system.reg"), "WINE REGISTRY");
        File.WriteAllText(Path.Combine(root, "user.reg"), "WINE REGISTRY");
        return prefix;
    }

    public RuntimeLocator CreateLocator(IPlatformInfo? platform = null, IExecutableFinder? finder = null, IProcessNames? names = null)
    {
        return new RuntimeLocator(
            platform ?? new FakePlatform(true),
            finder ?? new FakeFinder(),
            names ?? new FakeProcessNames(),
            NullLogger<RuntimeLocator>.Instance,
            Home,
            systemRoots: [],
            umuFallbacks: []
        );
    }

    public RuntimeLauncher CreateLauncher(IPlatformInfo? platform = null, IExecutableFinder? finder = null, IProcessNames? names = null)
    {
        finder ??= new FakeFinder(("umu-run", UmuPath), ("python3", "/fake/bin/python3"));
        platform ??= new FakePlatform(true);

        return new RuntimeLauncher(CreateLocator(platform, finder, names), finder, names ?? new FakeProcessNames(), platform);
    }

    public RuntimeLaunchRequest GameRequest(bool doorstop = true)
    {
        return new RuntimeLaunchRequest
        {
            Program = Game,
            Arguments = ["-force-gfx-jobs", "native"],
            WorkingDirectory = GameDirectory,
            LoadsDoorstop = doorstop,
        };
    }

    public RuntimeProfile ProtonProfile(string buildDirectory, Action<RuntimeProfile>? configure = null)
    {
        var profile = new RuntimeProfile
        {
            Id = Guid.NewGuid(),
            Name = "proton",
            Kind = RuntimeKind.Proton,
            Executable = buildDirectory,
            Environment = new Dictionary<string, string> { ["WINEDLLOVERRIDES"] = LaunchOptions.DoorstopOverride },
        };
        configure?.Invoke(profile);
        return profile;
    }

    public void Dispose()
    {
        _temp.Dispose();
    }
}
