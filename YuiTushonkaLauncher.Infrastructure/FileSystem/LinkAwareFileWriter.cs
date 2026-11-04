using System.Text;
using YuiTushonkaLauncher.Core.Files;

namespace YuiTushonkaLauncher.Infrastructure.FileSystem;

/// <summary>
/// The single writer for instance files: detaches a shared target before writing, so a hard link can
/// never leak a change into the base install or into another instance.
/// </summary>
public sealed class LinkAwareFileWriter(IFileLinkService fileLinks) : ILinkAwareFileWriter
{
    /// <inheritdoc />
    public async Task WriteAllBytesAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
    {
        fileLinks.Detach(path);
        EnsureDirectory(path);
        await File.WriteAllBytesAsync(path, content.ToArray(), cancellationToken);
    }

    /// <inheritdoc />
    public async Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        fileLinks.Detach(path);
        EnsureDirectory(path);
        await File.WriteAllTextAsync(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
    }

    /// <inheritdoc />
    public async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        bool overwrite,
        CancellationToken cancellationToken = default
    )
    {
        if (overwrite)
        {
            fileLinks.Detach(destinationPath);
        }

        EnsureDirectory(destinationPath);
        await using var source = File.OpenRead(sourcePath);
        await using var destination = new FileStream(
            destinationPath,
            overwrite ? FileMode.Create : FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true
        );
        await source.CopyToAsync(destination, 81920, cancellationToken);
    }

    private static void EnsureDirectory(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }
    }
}
