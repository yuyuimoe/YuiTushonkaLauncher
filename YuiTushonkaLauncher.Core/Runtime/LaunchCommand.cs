namespace YuiTushonkaLauncher.Core.Runtime;

/// <summary>A fully resolved child process: nothing is left for the launcher to decide.</summary>
public sealed record LaunchCommand
{
    public required string FileName { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>Variables set for the child, on top of the launcher's own environment.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

    public string? WorkingDirectory { get; init; }

    /// <summary>A single readable line, for logs and the "what will run" view. Not meant to be pasted into a shell.</summary>
    public string Describe()
    {
        var parts = new List<string>();

        foreach (var pair in Environment.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            parts.Add($"{pair.Key}={Quote(pair.Value)}");
        }

        parts.Add(Quote(FileName));
        parts.AddRange(Arguments.Select(Quote));

        return string.Join(' ', parts);
    }

    private static string Quote(string value)
    {
        return value.Length > 0 && value.All(character => !char.IsWhiteSpace(character) && character is not ('"' or '\''))
            ? value
            : "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}

/// <summary>The answer to "can this run, and how": a command, or the reasons it cannot.</summary>
public sealed record LaunchPlan
{
    /// <summary>The command to run; <c>null</c> when an error stops the launch.</summary>
    public LaunchCommand? Command { get; init; }

    public IReadOnlyList<RuntimeProblem> Problems { get; init; } = [];

    /// <summary>The Wine prefix the command uses; <c>null</c> for a native run.</summary>
    public string? PrefixPath { get; init; }

    public bool CanRun
    {
        get { return Command is not null && Problems.All(problem => !problem.IsError); }
    }

    public IEnumerable<RuntimeProblem> Errors
    {
        get { return Problems.Where(problem => problem.IsError); }
    }
}
