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

    internal ShellDeferredStream(Func<CancellationToken, Task<Stream>> open, long length,
        Action<Exception> failure, Action release)
    {
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
            try { return _open.GetAwaiter().GetResult(); }
            catch (Exception ex)
            {
                _failure(ex);
                throw ShellTransferFailure.ToComException(ex);
            }
        }
    }

    internal void CancelPending() => _ = _stop.CancelAsync();
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
        catch (Exception) { /* The consumer observes failures when it reads. */ }
    }
}
