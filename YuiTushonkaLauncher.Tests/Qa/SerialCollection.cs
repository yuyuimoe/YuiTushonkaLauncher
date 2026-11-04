using Xunit;

namespace YuiTushonkaLauncher.Tests.Qa;

/// <summary>
/// Measurements run alone. A test that counts allocations on its own thread is meaningless while another class is
/// allocating on the same pool thread — that is exactly how the log-tail test once measured 31 MB instead of 3 MB.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SerialCollection
{
    public const string Name = "Serial (measures allocations and time)";
}
