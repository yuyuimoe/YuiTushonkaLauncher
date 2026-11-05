namespace YuiTushonkaLauncher.Core.Mods;

/// <summary>Which container a mod arrives in. The Forge hosts all three.</summary>
public enum ModArchiveFormat
{
    /// <summary>Not a container this launcher knows.</summary>
    Unknown,

    /// <summary>Zip — also the format of an exported <c>.sptpack</c>.</summary>
    Zip,

    /// <summary>7-Zip, which most of the popular mods use.</summary>
    SevenZip,

    /// <summary>Rar, which a few mods still ship.</summary>
    Rar,
}

/// <summary>An archive could not be read, for a reason a person can act on.</summary>
/// <param name="message">What is wrong, in the user's terms.</param>
/// <param name="path">The file that could not be read.</param>
public sealed class ModArchiveException(string message, string path) : Exception(message)
{
    /// <summary>The archive that could not be read.</summary>
    public string Path { get; } = path;
}
