using Xunit;

namespace YuiTushonkaLauncher.Tests.Ui;

/// <summary>
/// Every UI test runs in this one collection, sequentially: Avalonia's headless platform has one dispatcher per process,
/// and two tests building windows at once is a crash, not a failure.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AvaloniaCollection
{
    public const string Name = "Avalonia headless";
}
