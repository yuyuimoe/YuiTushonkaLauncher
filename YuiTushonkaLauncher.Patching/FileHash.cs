using System.Security.Cryptography;

namespace YuiTushonkaLauncher.Patching;

/// <summary>SHA-256 over a file, streamed so a 16 MB assembly is never held in memory.</summary>
public static class FileHash
{
    /// <summary>Hashes a file, or returns <c>null</c> when it cannot be read.</summary>
    public static string? TryCompute(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);

            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Hashes a file, throwing when it cannot be read.</summary>
    public static async Task<string> ComputeAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);

        return Convert.ToHexStringLower(hash);
    }
}
