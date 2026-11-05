using Microsoft.Extensions.Logging;
using SharpHDiffPatch.Core;
using SharpHDiffPatch.Core.Event;
using YuiTushonkaLauncher.Core.Patching;

namespace YuiTushonkaLauncher.Patching;

/// <summary>
/// The HDiffPatch engine, wrapped so callers never touch the library's globals.
/// </summary>
/// <remarks>
/// <para>
/// The library reports progress through <em>static</em> events (<c>EventListener.PatchEvent</c>) and a
/// static verbosity, so two patches running at once would mix their progress and their log lines.
/// This wrapper serialises every call and subscribes/unsubscribes per call.
/// </para>
/// <para>
/// The SPT deltas are <c>HDIFF13&amp;zstd</c>; the library also handles bzip2, deflate, LZMA2 and
/// uncompressed diffs, so nothing here depends on the compression of a future delta.
/// </para>
/// </remarks>
public sealed class SharpHDiffPatchEngine(ILogger<SharpHDiffPatchEngine> logger) : IPatchEngine
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <inheritdoc />
    public DeltaHeader ReadHeader(string deltaPath)
    {
        if (!File.Exists(deltaPath))
        {
            throw new FileNotFoundException("The delta file was not found.", deltaPath);
        }

        try
        {
            return new DeltaHeader
            {
                ExpectedInputSize = HDiffPatch.GetHDiffOldSize(deltaPath),
                ExpectedOutputSize = HDiffPatch.GetHDiffNewSize(deltaPath),
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidDataException($"'{deltaPath}' is not a usable HDiffPatch delta: {exception.Message}", exception);
        }
    }

    /// <inheritdoc />
    public void Apply(
        string deltaPath,
        string sourcePath,
        string destinationPath,
        IProgress<PatchProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        Gate.Wait(cancellationToken);

        try
        {
            HDiffPatch.LogVerbosity = Verbosity.Quiet;

            var fileName = Path.GetFileName(sourcePath);

            void OnPatchEvent(object? sender, PatchEvent patchEvent)
            {
                progress?.Report(
                    new PatchProgress
                    {
                        FileName = fileName,
                        Percentage = patchEvent.ProgressPercentage,
                        BytesWritten = patchEvent.CurrentSizePatched,
                        TotalBytes = patchEvent.TotalSizeToBePatched,
                    }
                );
            }

            void OnLogEvent(object? sender, LoggerEvent logEvent)
            {
                logger.LogDebug("HDiffPatch: {Message}", logEvent.Message);
            }

            var patcher = new HDiffPatch();
            patcher.Initialize(deltaPath);

            EventListener.PatchEvent += OnPatchEvent;
            EventListener.LoggerEvent += OnLogEvent;

            try
            {
                logger.LogDebug(
                    "Applying {Delta} to {Source} -> {Destination} ({Old} -> {New} bytes)",
                    Path.GetFileName(deltaPath),
                    sourcePath,
                    destinationPath,
                    HDiffPatch.GetHDiffOldSize(deltaPath),
                    HDiffPatch.GetHDiffNewSize(deltaPath)
                );

                patcher.Patch(
                    sourcePath,
                    destinationPath,
                    useBufferedPatch: true,
                    token: cancellationToken,
                    useFullBuffer: false,
                    useFastBuffer: true
                );
            }
            finally
            {
                EventListener.PatchEvent -= OnPatchEvent;
                EventListener.LoggerEvent -= OnLogEvent;
            }
        }
        finally
        {
            Gate.Release();
        }
    }
}
