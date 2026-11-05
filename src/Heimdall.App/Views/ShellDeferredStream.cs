/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;

namespace Heimdall.App.Views;

/// <summary>Exposes a stream immediately but never reads bytes before verified staging finishes.</summary>
internal sealed class ShellDeferredStream : Stream
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task<Stream> _open;
    private readonly long _length;
    private readonly Action<Exception> _failure;
    private readonly Action _release;
    private int _disposed;
    private readonly Func<bool>? _enter;
    private readonly Action? _exit;

    internal ShellDeferredStream(Func<CancellationToken, Task<Stream>> open, long length,
        Action<Exception> failure, Action release, Func<bool>? enter = null, Action? exit = null)
    {
        _enter = enter;
        _exit = exit;
        _length = length;
        _failure = failure;
        _release = release;
        _open = open(_stop.Token);
    }

    public override bool CanRead => Volatile.Read(ref _disposed) == 0;
    public override bool CanSeek => Volatile.Read(ref _disposed) == 0;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position { get => Current.Position; set => Current.Position = value; }

    private Stream Current
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_enter is not null && !_enter()) { throw new OperationCanceledException(); }
            try { return _open.WaitAsync(_stop.Token).GetAwaiter().GetResult(); }
            catch (Exception ex)
            {
                _failure(ex);
                throw ShellTransferFailure.ToComException(ex);
            }
            finally { _exit?.Invoke(); }
        }
    }

    internal void CancelPending() => _ = ObserveCancellationAsync(_stop.CancelAsync());
    public override int Read(byte[] buffer, int offset, int count) => Current.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => Current.Seek(offset, origin);
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            CancelPending();
            _ = ReleaseOpenAsync(_open);
            _release();
        }
        base.Dispose(disposing);
    }

    private static async Task ReleaseOpenAsync(Task<Stream> open)
    {
        try { (await open.ConfigureAwait(false)).Dispose(); }
        catch (OperationCanceledException) { }
        catch (Exception) { Heimdall.Core.Logging.FileLogger.Warn("A deferred Shell stream failed while releasing its pending file."); }
    }

    private static async Task ObserveCancellationAsync(Task cancellation)
    {
        try { await cancellation.ConfigureAwait(false); }
        catch (Exception) { Heimdall.Core.Logging.FileLogger.Warn("A deferred Shell stream cancellation callback failed."); }
    }
}
