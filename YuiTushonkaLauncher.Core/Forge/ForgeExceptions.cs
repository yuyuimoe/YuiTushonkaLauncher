namespace YuiTushonkaLauncher.Core.Forge;

/// <summary>Base type for everything the Forge client can fail with.</summary>
public class ForgeException : Exception
{
    public ForgeException(string message)
        : base(message) { }

    public ForgeException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>The API answered with an error envelope (<c>success: false</c>) or a 4xx status.</summary>
public sealed class ForgeRequestException : ForgeException
{
    public ForgeRequestException(int statusCode, string? code, string message)
        : base($"Forge API error {(code is null ? string.Empty : code + ": ")}{message} (HTTP {statusCode})")
    {
        StatusCode = statusCode;
        Code = code;
    }

    /// <summary>HTTP status returned.</summary>
    public int StatusCode { get; }

    /// <summary>Machine-readable code from the envelope, e.g. <c>VALIDATION_FAILED</c>.</summary>
    public string? Code { get; }

    /// <summary>True for 404s.</summary>
    public bool IsNotFound
    {
        get { return StatusCode == 404; }
    }
}

/// <summary>The API could not be reached, or kept failing after the retry budget was spent.</summary>
public sealed class ForgeUnavailableException : ForgeException
{
    public ForgeUnavailableException(string message)
        : base(message) { }

    public ForgeUnavailableException(string message, Exception innerException)
        : base(message, innerException) { }
}
