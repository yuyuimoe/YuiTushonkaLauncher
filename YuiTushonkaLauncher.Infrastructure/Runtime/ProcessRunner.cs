using System.Diagnostics;
using Microsoft.Extensions.Logging;
using YuiTushonkaLauncher.Core.Runtime;

namespace YuiTushonkaLauncher.Infrastructure.Runtime;

/// <summary>
/// Runs a command to completion with its output streamed line by line. No TTY is needed: stdin is closed
/// at once, so a tool that wants to ask a question fails instead of hanging.
/// </summary>
public sealed class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    private static readonly TimeSpan DrainGrace = TimeSpan.FromSeconds(3);

    /// <inheritdoc />
    public async Task<ProcessRunResult> RunAsync(
        LaunchCommand command,
        IProgress<ProcessLine>? output = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        var info = new ProcessStartInfo(command.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        if (command.WorkingDirectory is not null)
        {
            info.WorkingDirectory = command.WorkingDirectory;
        }

        foreach (var argument in command.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in command.Environment)
        {
            info.Environment[name] = value;
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException($"'{command.FileName}' did not start.");
        process.StandardInput.Close();

        var readers = Task.WhenAll(
            Pump(process.StandardOutput, isError: false, output),
            Pump(process.StandardError, isError: true, output)
        );

        using var timeoutSource = new CancellationTokenSource();

        if (timeout is { } limit)
        {
            timeoutSource.CancelAfter(limit);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            // A Wine launch spawns a tree (wineserver, services.exe…); killing only the parent leaves orphans.
            Kill(process);
            await process.WaitForExitAsync(CancellationToken.None);
            await DrainAsync(readers);

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            logger.LogWarning("'{File}' did not finish within {Timeout} and was stopped", command.FileName, timeout);
            return new ProcessRunResult(-1, TimedOut: true);
        }

        await DrainAsync(readers);
        return new ProcessRunResult(process.ExitCode);
    }

    /// <summary>
    /// Waits for the last output after the process ended, but not forever: a daemon the process started
    /// (Wine's <c>wineserver</c> lingers for seconds) inherits the pipes and would keep them open.
    /// </summary>
    private static async Task DrainAsync(Task readers)
    {
        await Task.WhenAny(readers, Task.Delay(DrainGrace));
    }

    private static async Task Pump(StreamReader reader, bool isError, IProgress<ProcessLine>? output)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            output?.Report(new ProcessLine(isError, line));
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }
}
