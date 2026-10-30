/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
namespace Heimdall.App.Views;

/// <summary>Tracks transfers until completion and cancels them when the session ends.</summary>
internal sealed class ShellTransferRegistry
{
    private readonly object _gate = new();
    private readonly HashSet<Action> _cancellations = [];
    private bool _stopped;

    internal bool Add(Action cancel)
    {
        lock (_gate)
        {
            if (_stopped) { return false; }
            return _cancellations.Add(cancel);
        }
    }

    internal void Remove(Action cancel)
    {
        lock (_gate) { _cancellations.Remove(cancel); }
    }

    internal void Stop()
    {
        Action[] actions;
        lock (_gate)
        {
            if (_stopped) { return; }
            _stopped = true;
            actions = _cancellations.ToArray();
            _cancellations.Clear();
        }
        foreach (Action cancel in actions)
        {
            try { cancel(); }
            catch (AggregateException)
            {
                // A cancellation callback failed; continue cancelling the other transfers.
                Heimdall.Core.Logging.FileLogger.Warn("A Shell transfer cancellation callback failed.");
            }
        }
    }
}
