using System;
using Avalonia.Controls;

namespace YuiTushonkaLauncher.Services;

/// <summary>
/// Holds one object for reuse until it is released. Kept apart from <see cref="WindowSlot{T}" /> so the rule that matters
/// — a closed window is never handed out again — can be tested without a desktop.
/// </summary>
/// <typeparam name="T">What is cached.</typeparam>
public sealed class ReusableSlot<T>
    where T : class
{
    private T? _current;

    /// <summary>The cached instance, created when there is none.</summary>
    public T GetOrCreate(Func<T> create)
    {
        return _current ??= create();
    }

    /// <summary>Forgets <paramref name="item" /> when it is still the cached one, which is what a close event does.</summary>
    public void Release(T item)
    {
        if (ReferenceEquals(_current, item))
        {
            _current = null;
        }
    }

    /// <summary>Drops the reference without touching the object.</summary>
    public void Forget()
    {
        _current = null;
    }
}

/// <summary>
/// Holds one window for reuse and forgets it when it closes. Avalonia refuses to show a window twice
/// (<c>Cannot re-show a closed window</c>), so a cached reference to a closed window is a crash waiting for the second
/// click: closing the manager and opening it again did exactly that.
/// </summary>
/// <typeparam name="T">The window type.</typeparam>
public sealed class WindowSlot<T>
    where T : Window
{
    private readonly ReusableSlot<T> _slot = new();

    /// <summary>The window, created when there is none or the last one was closed.</summary>
    /// <param name="create">Builds a new window; only called when one is needed.</param>
    public T GetOrCreate(Func<T> create)
    {
        var window = _slot.GetOrCreate(create);

        // Idempotent: the handler is attached once per window, even when the slot is asked twice.
        window.Closed -= OnClosed;
        window.Closed += OnClosed;
        return window;
    }

    /// <summary>Drops the reference without closing anything.</summary>
    public void Forget()
    {
        _slot.Forget();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (sender is T window)
        {
            window.Closed -= OnClosed;
            _slot.Release(window);
        }
    }
}
