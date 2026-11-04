namespace YuiTushonkaLauncher.Core.Patching;

/// <summary>What a delta promises: how big its input is and how big its output will be.</summary>
public sealed record DeltaHeader
{
    public required long ExpectedInputSize { get; init; }

    public required long ExpectedOutputSize { get; init; }
}

/// <summary>Byte-level progress of one patch, reported often enough for a progress bar.</summary>
public sealed record PatchProgress
{
    /// <summary>File being written, for the log and the UI.</summary>
    public required string FileName { get; init; }

    /// <summary>Percentage of the patch already applied (0–100).</summary>
    public required double Percentage { get; init; }

    public required long BytesWritten { get; init; }

    public required long TotalBytes { get; init; }
}

/// <summary>Applies an HDiffPatch delta. Implemented in <c>YuiTushonkaLauncher.Patching</c>.</summary>
public interface IPatchEngine
{
    /// <summary>Reads the sizes a delta declares, without applying it.</summary>
    /// <exception cref="InvalidDataException">The file is not an HDiffPatch delta.</exception>
    DeltaHeader ReadHeader(string deltaPath);

    /// <summary>
    /// Applies <paramref name="deltaPath" /> to <paramref name="sourcePath" />, writing
    /// <paramref name="destinationPath" />. The destination is created by the engine, never the source.
    /// </summary>
    void Apply(
        string deltaPath,
        string sourcePath,
        string destinationPath,
        IProgress<PatchProgress>? progress = null,
        CancellationToken cancellationToken = default
    );
}
