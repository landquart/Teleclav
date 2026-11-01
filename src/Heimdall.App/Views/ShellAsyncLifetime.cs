/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
namespace Heimdall.App.Views;

/// <summary>Keeps resources alive until both the drag and any asynchronous extraction have ended.</summary>
internal sealed class ShellAsyncLifetime(Action cleanup)
{
    internal ShellAsyncLifetime(Action cleanup, TimeSpan abandonedTimeout) : this(cleanup)
        => ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(abandonedTimeout, TimeSpan.Zero);

    private readonly object _gate = new();
    private bool _inOperation;
    private bool _started;
    private bool _dragReturned;
    private bool _cleaned;
    private bool _cancelled;

    internal bool InOperation { get { lock (_gate) { return _inOperation; } } }

    internal bool Begin()
    {
        lock (_gate)
        {
            if (_dragReturned || _started || _cleaned) { return false; }
            _started = true;
            _inOperation = true;
            return true;
        }
    }

    internal void End()
    {
        bool clean;
        lock (_gate)
        {
            _inOperation = false;
            clean = MarkCleanup();
        }
        if (clean) { cleanup(); }
    }

    internal void DragReturned()
    {
        bool clean;
        lock (_gate)
        {
            _dragReturned = true;
            clean = MarkCleanup();
        }
        if (clean) { cleanup(); }
    }

    internal void Cancel()
    {
        bool clean;
        lock (_gate)
        {
            _cancelled = true;
            clean = MarkCleanup();
        }
        if (clean) { cleanup(); }
    }

    private bool MarkCleanup()
    {
        if (!_dragReturned || (_inOperation && !_cancelled) || _cleaned) { return false; }
        _cleaned = true;
        return true;
    }
}
