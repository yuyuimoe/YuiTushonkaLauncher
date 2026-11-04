namespace YuiTushonkaLauncher;

/// <summary>Process exit codes, so a supervisor (or a shell script) can tell the outcomes apart.</summary>
internal static class ExitCodes
{
    /// <summary>Another instance was already running; nothing was done.</summary>
    public const int AlreadyRunning = 2;

    /// <summary>Unhandled exception during startup or run.</summary>
    public const int Crashed = 1;
}
