using YuiTushonkaLauncher.Core.Server;

namespace YuiTushonkaLauncher.Infrastructure.Server;

/// <summary>The newest lines a server wrote. Bounded: a chatty server cannot grow it, the oldest line makes room.</summary>
internal sealed class ServerOutputBuffer(int capacity)
{
    private readonly object _lock = new();

    private readonly Queue<ServerOutputLine> _lines = new();

    public void Add(ServerOutputLine line)
    {
        lock (_lock)
        {
            if (_lines.Count >= capacity)
            {
                _lines.Dequeue();
            }

            _lines.Enqueue(line);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _lines.Clear();
        }
    }

    public IReadOnlyList<ServerOutputLine> Last(int count)
    {
        lock (_lock)
        {
            return [.. _lines.Skip(Math.Max(0, _lines.Count - count))];
        }
    }
}
