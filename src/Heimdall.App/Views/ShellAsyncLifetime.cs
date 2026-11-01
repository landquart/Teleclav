/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
namespace Heimdall.App.Views;

/// <summary>Tracks the drag, asynchronous extraction, active callbacks and abandoned operations.</summary>
internal sealed class ShellAsyncLifetime
{
    private readonly Action _cleanup;
    private readonly TimeSpan _idleLimit;
    private readonly object _gate = new();
    private System.Threading.Timer? _timer;
    private int _generation;
    private int _callbacks;
    private bool _inOperation;
    private bool _started;
    private bool _dragReturned;
    private bool _cleaned;
    private bool _cancelled;

    internal ShellAsyncLifetime(Action cleanup) : this(cleanup, TimeSpan.FromMinutes(30)) { }

    internal ShellAsyncLifetime(Action cleanup, TimeSpan idleLimit)
    {
        _cleanup = cleanup ?? throw new ArgumentNullException(nameof(cleanup));
        if (idleLimit <= TimeSpan.Zero || idleLimit > TimeSpan.FromMilliseconds(uint.MaxValue - 1))
            throw new ArgumentOutOfRangeException(nameof(idleLimit));
        _idleLimit = idleLimit;
    }

    internal bool InOperation { get { lock (_gate) { return _inOperation; } } }

    internal bool Begin()
    {
        lock (_gate)
        {
            if (_dragReturned || _started || _cleaned || _cancelled) { return false; }
            _started = true;
            _inOperation = true;
            return true;
        }
    }

    internal bool Enter()
    {
        lock (_gate)
        {
            if (_cleaned || _cancelled) { return false; }
            _callbacks++;
            Disarm();
            return true;
        }
    }

    internal void Exit()
    {
        bool clean;
        lock (_gate)
        {
            _callbacks--;
            clean = MarkCleanup();
            if (!clean) { ArmIfIdle(); }
        }
        if (clean) { _cleanup(); }
    }

    internal void End()
    {
        bool clean;
        lock (_gate)
        {
            _inOperation = false;
            clean = MarkCleanup();
        }
        if (clean) { _cleanup(); }
    }

    internal void DragReturned()
    {
        bool clean;
        lock (_gate)
        {
            _dragReturned = true;
            clean = MarkCleanup();
            if (!clean) { ArmIfIdle(); }
        }
        if (clean) { _cleanup(); }
    }

    internal void Cancel()
    {
        bool clean;
        lock (_gate)
        {
            _cancelled = true;
            clean = MarkCleanup();
        }
        if (clean) { _cleanup(); }
    }

    private bool MarkCleanup()
    {
        if (!_dragReturned || _callbacks != 0 || (_inOperation && !_cancelled) || _cleaned) { return false; }
        _cleaned = true;
        Disarm();
        return true;
    }

    private void ArmIfIdle()
    {
        if (!_dragReturned || !_inOperation || _callbacks != 0 || _cancelled || _cleaned) { return; }
        Disarm();
        var ticket = new Ticket(this, _generation);
        _timer = new System.Threading.Timer(static state =>
        {
            var current = (Ticket)state!;
            current.Owner.Expire(current.Generation);
        }, ticket, _idleLimit, Timeout.InfiniteTimeSpan);
    }

    private void Disarm()
    {
        _generation++;
        _timer?.Dispose();
        _timer = null;
    }

    private void Expire(int generation)
    {
        bool clean;
        lock (_gate)
        {
            if (generation != _generation || !_dragReturned || !_inOperation || _callbacks != 0 || _cleaned) { return; }
            _cancelled = true;
            _inOperation = false;
            clean = MarkCleanup();
        }
        if (clean)
        {
            try
            {
                Heimdall.Core.Logging.FileLogger.Warn("A Shell drag was closed after the receiver stopped requesting data without ending the operation.");
                _cleanup();
            }
            catch (Exception)
            {
                Heimdall.Core.Logging.FileLogger.Warn("Cleanup of an abandoned Shell drag failed.");
            }
        }
    }

    private sealed record Ticket(ShellAsyncLifetime Owner, int Generation);
}
