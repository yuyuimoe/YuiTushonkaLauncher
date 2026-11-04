using System.Text;
using YuiTushonkaLauncher.Core.Logs;

namespace YuiTushonkaLauncher.Infrastructure.Logs;

/// <summary>
/// Reads the end of a log file by seeking, never by loading it: a game session writes hundreds of megabytes, and a
/// viewer that reads them into memory is the freeze the user is trying to diagnose.
/// </summary>
public sealed class LogReader : ILogReader
{
    /// <summary>How much is read from the end for a tail; enough for hundreds of lines of a verbose log.</summary>
    private const int WindowBytes = 512 * 1024;

    /// <inheritdoc />
    public async Task<LogTail> ReadTailAsync(
        LogFile file,
        int maxLines = LogLimits.DefaultTailLines,
        CancellationToken cancellationToken = default
    )
    {
        if (file.Bytes > LogLimits.MaxFollowedBytes)
        {
            return new LogTail
            {
                File = file,
                TooLarge = true,
                Reason =
                    $"'{file.Label}' is {file.Bytes / (1024.0 * 1024.0):0} MiB, above the {LogLimits.MaxFollowedBytes / (1024 * 1024)} MiB limit. "
                    + "Open it in a text editor; the launcher will not load it.",
            };
        }

        try
        {
            await using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, stream.Length - WindowBytes);
            stream.Seek(start, SeekOrigin.Begin);

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var text = await reader.ReadToEndAsync(cancellationToken);
            var all = text.Split('\n');

            // The first line of a window is usually a fragment, unless the window started at byte 0.
            var skip = start > 0 ? 1 : 0;
            var lines = all.Skip(skip).Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0).ToList();
            var tail = lines.Count > maxLines ? lines[^maxLines..] : lines;

            return new LogTail
            {
                File = file,
                Lines = [.. tail.Select((line, index) => new LogLine(file.Kind, file.Label, index + 1, line))],
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new LogTail
            {
                File = file,
                Missing = true,
                Reason = exception.Message,
            };
        }
    }
}
