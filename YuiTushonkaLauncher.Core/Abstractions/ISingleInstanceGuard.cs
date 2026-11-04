namespace YuiTushonkaLauncher.Core.Abstractions;

/// <summary>
/// Guarantees a single running launcher, so two processes cannot operate on the same instances and
/// database at the same time.
/// </summary>
public interface ISingleInstanceGuard : IDisposable
{
    /// <summary>
    /// Tries to take ownership. Returns <c>true</c> when this process is the only instance.
    /// Calling it again after success returns <c>true</c> without doing anything.
    /// </summary>
    bool TryAcquire();
}
