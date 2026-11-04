using System.Collections.Generic;

namespace YuiTushonkaLauncher.Core.Forge;

/// <summary>
/// Back and forward through what the user selected. Selection in the browser is navigation — a dependency opens
/// another mod's details — so it needs the same two buttons a browser has, and the same rule that a new selection
/// clears the forward stack.
/// </summary>
/// <typeparam name="T">What is being navigated between.</typeparam>
public sealed class NavigationHistory<T>
    where T : notnull
{
    private readonly List<T> _back = [];

    private readonly List<T> _forward = [];

    /// <summary>The entry currently shown.</summary>
    public T? Current { get; private set; }

    public bool CanGoBack
    {
        get { return _back.Count > 0; }
    }

    public bool CanGoForward
    {
        get { return _forward.Count > 0; }
    }

    /// <summary>How many entries the history holds, including the current one.</summary>
    public int Count
    {
        get { return _back.Count + (Current is null ? 0 : 1) + _forward.Count; }
    }

    /// <summary>Shows a new entry; the current one becomes the way back and the forward stack is dropped.</summary>
    /// <returns>False when the entry is the one already shown, which would otherwise grow the history with no-ops.</returns>
    public bool Push(T entry)
    {
        if (EqualityComparer<T>.Default.Equals(entry, Current) && Current is not null)
        {
            return false;
        }

        if (Current is not null)
        {
            _back.Add(Current);
        }

        _forward.Clear();
        Current = entry;
        return true;
    }

    /// <summary>Goes back one entry, or returns <c>null</c> when there is none.</summary>
    public T? Back()
    {
        if (_back.Count == 0)
        {
            return default;
        }

        if (Current is not null)
        {
            _forward.Add(Current);
        }

        Current = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        return Current;
    }

    /// <summary>Goes forward again, or returns <c>null</c> when the user has not been back.</summary>
    public T? Forward()
    {
        if (_forward.Count == 0)
        {
            return default;
        }

        if (Current is not null)
        {
            _back.Add(Current);
        }

        Current = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        return Current;
    }

    /// <summary>Forgets everything, which is what changing the search results does.</summary>
    public void Clear()
    {
        _back.Clear();
        _forward.Clear();
        Current = default;
    }
}
