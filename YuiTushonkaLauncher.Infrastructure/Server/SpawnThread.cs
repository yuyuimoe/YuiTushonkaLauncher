using System.Collections.Concurrent;

namespace YuiTushonkaLauncher.Infrastructure.Server;

/// <summary>
/// One long-lived thread that starts every child process. Linux ties the parent-death signal of a child to
/// the <em>thread</em> that created it, not to the process: a child started from a thread-pool thread gets
/// its signal when the pool retires that thread, about twenty seconds later, with the launcher still alive
/// (measured). A thread that never ends is the only thread that is safe to start children from.
/// </summary>
internal sealed class SpawnThread : IDisposable
{
    private readonly BlockingCollection<Action> _work = [];

    private readonly Thread _thread;

    public SpawnThread()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "child-process-spawner" };
        _thread.Start();
    }

    public Task<T> RunAsync<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        _work.Add(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });

        return completion.Task;
    }

    public void Dispose()
    {
        _work.CompleteAdding();
    }

    private void Loop()
    {
        foreach (var work in _work.GetConsumingEnumerable())
        {
            work();
        }
    }
}
