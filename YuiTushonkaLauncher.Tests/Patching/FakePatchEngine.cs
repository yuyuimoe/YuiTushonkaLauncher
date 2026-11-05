using YuiTushonkaLauncher.Core.Patching;

namespace YuiTushonkaLauncher.Tests.Patching;

/// <summary>
/// A deterministic stand-in for HDiffPatch: a "delta" is a file whose *name* carries the two sizes, and
/// applying it appends a fixed marker. Enough to drive every decision the service makes without
/// shipping a 4 MB binary fixture.
/// </summary>
/// <remarks>
/// A fixture delta is a real-looking <c>&lt;target&gt;.delta</c> plus a <c>.header</c> sidecar holding the
/// two declared sizes, because in the real tree the delta is named after the file it patches. The engine
/// records every call so a test can prove what was read and what was written.
/// </remarks>
public sealed class FakePatchEngine : IPatchEngine
{
    /// <summary>Marker appended to the source when a delta is applied.</summary>
    public const string PatchMarker = "-PATCHED";

    /// <summary>Every (delta, source, destination) triple the engine was asked to apply.</summary>
    public List<(string Delta, string Source, string Destination)> Applied { get; } = [];

    /// <summary>Set to make the engine throw instead of writing.</summary>
    public Exception? Failure { get; set; }

    /// <summary>Set to make the engine write a different size than its delta declares (verification test).</summary>
    public int? ProducedSizeOverride { get; set; }

    /// <summary>Set to make the engine claim a different input size for a delta (guard test).</summary>
    public long? InputSizeOverride { get; set; }

    /// <summary>Suffix of the sidecar that carries a fixture delta's declared sizes.</summary>
    public const string HeaderSuffix = ".header";

    public DeltaHeader ReadHeader(string deltaPath)
    {
        var headerPath = deltaPath + HeaderSuffix;
        if (!File.Exists(headerPath))
        {
            throw new InvalidDataException($"'{Path.GetFileName(deltaPath)}' is not a fixture delta: no {HeaderSuffix} sidecar.");
        }

        var parts = File.ReadAllText(headerPath).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !long.TryParse(parts[0], out var input) || !long.TryParse(parts[1], out var output))
        {
            throw new InvalidDataException($"'{headerPath}' does not declare two sizes.");
        }

        return new DeltaHeader { ExpectedInputSize = InputSizeOverride ?? input, ExpectedOutputSize = output };
    }

    public void Apply(
        string deltaPath,
        string sourcePath,
        string destinationPath,
        IProgress<PatchProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        var header = ReadHeader(deltaPath);
        var source = File.ReadAllBytes(sourcePath);
        var output = new byte[ProducedSizeOverride ?? header.ExpectedOutputSize];
        source.CopyTo(output, 0);

        var marker = System.Text.Encoding.UTF8.GetBytes(PatchMarker);
        marker.CopyTo(output, source.Length);

        File.WriteAllBytes(destinationPath, output);
        Applied.Add((deltaPath, sourcePath, destinationPath));

        progress?.Report(
            new PatchProgress
            {
                FileName = Path.GetFileName(sourcePath),
                Percentage = 100,
                BytesWritten = output.Length,
                TotalBytes = output.Length,
            }
        );
    }
}
