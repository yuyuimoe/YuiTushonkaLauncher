namespace YuiTushonkaLauncher.Core.Runtime;

/// <summary>How seriously a pre-flight finding should be taken.</summary>
public enum RuntimeProblemSeverity
{
    /// <summary>The launch can proceed, but something will probably not work as the user expects.</summary>
    Warning,

    /// <summary>The launch must not start: running it would fail or damage something.</summary>
    Error,
}

/// <summary>What a pre-flight finding is about; the UI maps each code to advice.</summary>
public enum RuntimeProblemCode
{
    NoProfile,
    ProfileKindUnsupported,
    ProtonNotConfigured,
    ProtonMissing,
    ProtonInvalid,
    UmuMissing,
    WineMissing,
    PythonMissing,
    ProgramMissing,
    WorkingDirectoryMissing,
    PrefixNotWritable,
    PrefixBroken,
    PrefixNotCreated,
    DllOverrideMissing,
    EnvironmentInvalid,
    GameModeMissing,
    GameModeNotRunning,
    ToolMissing,
    SteamRootUnknown,
}

/// <summary>One thing pre-flight found wrong, with the exact path involved when there is one.</summary>
/// <param name="Severity">Whether the launch may proceed.</param>
/// <param name="Code">Machine-readable kind.</param>
/// <param name="Message">Text shown to the user as-is.</param>
/// <param name="Path">The missing or unusable path, when the problem is about one.</param>
public sealed record RuntimeProblem(RuntimeProblemSeverity Severity, RuntimeProblemCode Code, string Message, string? Path = null)
{
    public bool IsError
    {
        get { return Severity == RuntimeProblemSeverity.Error; }
    }
}
