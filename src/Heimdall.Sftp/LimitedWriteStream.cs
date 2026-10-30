/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;

namespace Heimdall.Sftp;

/// <summary>Limits bytes written to a destination without owning it.</summary>
internal sealed class LimitedWriteStream(Stream destination, long limit) : Stream
{
    private readonly Stream _destination = destination ?? throw new ArgumentNullException(nameof(destination));
    private readonly long _limit = limit >= 0 ? limit : throw new ArgumentOutOfRangeException(nameof(limit));
    private long _written;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => _destination.CanWrite;
    public override long Length => _written;
    public override long Position { get => _written; set => throw new NotSupportedException(); }
    public override void Flush() => _destination.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _destination.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count) { throw new ArgumentException("The buffer range is invalid."); }
        EnsureFits(count);
        _destination.Write(buffer, offset, count);
        _written += count;
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        EnsureFits(buffer.Length);
        _destination.Write(buffer);
        _written += buffer.Length;
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        EnsureFits(buffer.Length);
        await _destination.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        _written += buffer.Length;
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count) { throw new ArgumentException("The buffer range is invalid."); }
        EnsureFits(count);
        await _destination.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        _written += count;
    }

    private void EnsureFits(int count)
    {
        if (count > _limit - _written)
        {
            throw new IOException("The download exceeds its permitted size.");
        }
    }
}
