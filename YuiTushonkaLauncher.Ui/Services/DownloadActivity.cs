using System;

namespace YuiTushonkaLauncher.Services;

/// <summary>How many downloads the UI itself started and has not finished. The status bar shows it.</summary>
public interface IDownloadActivity
{
    int Running { get; }

    event EventHandler? Changed;

    void Begin();

    void End();
}

/// <summary>Counts downloads; the only thing that matters is that the count returns to zero.</summary>
public sealed class DownloadActivity : IDownloadActivity
{
    private int _running;

    public int Running
    {
        get { return Volatile.Read(ref _running); }
    }

    public event EventHandler? Changed;

    public void Begin()
    {
        Interlocked.Increment(ref _running);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void End()
    {
        if (Interlocked.Decrement(ref _running) < 0)
        {
            Interlocked.Exchange(ref _running, 0);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
