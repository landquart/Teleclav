/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;

namespace Heimdall.App.Views;

/// <summary>Shares completed staged downloads while keeping each reader's file position independent.</summary>
internal sealed class ShellDownloadCache : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<int, Entry> _entries = [];
    private readonly Func<int, string, CancellationToken, Task> _download;
    private readonly CancellationTokenSource _stop = new();
    private readonly long _limit;
    private readonly TimeSpan _waitLimit;
    private long _reserved;
    private int _readers;
    private int _downloads;
    private bool _disposed;
    private bool _cleaned;

    internal ShellDownloadCache(long limit, Func<int, string, CancellationToken, Task> download, TimeSpan? waitLimit = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        _download = download ?? throw new ArgumentNullException(nameof(download));
        _limit = limit;
        _waitLimit = waitLimit ?? TimeSpan.FromMinutes(30);
        if (_waitLimit <= TimeSpan.Zero || _waitLimit > TimeSpan.FromMilliseconds(uint.MaxValue - 1))
            throw new ArgumentOutOfRangeException(nameof(waitLimit));
        DirectoryPath = ShellStagingDirectory.Create();
    }

    internal string DirectoryPath { get; }
    internal CancellationToken StopToken => _stop.Token;

    internal async Task<Stream> OpenAsync(int index, long size, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        Entry entry;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_entries.TryGetValue(index, out entry!))
            {
                if (size > _limit - _reserved)
                {
                    throw new IOException("The temporary transfer space limit would be exceeded.");
                }

                _reserved += size;
                _downloads++;
                string path = Path.Combine(DirectoryPath, index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                entry = new Entry(size, Task.Run(() => DownloadAsync(index, path, size)));
                _entries.Add(index, entry);
            }
            else if (entry.Size != size)
            {
                throw new IOException("The declared file size changed for this transfer.");
            }
        }

        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        string savedPath;
        try { savedPath = await entry.Download.WaitAsync(_waitLimit, waitCancellation.Token).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            Heimdall.Core.Logging.FileLogger.Warn("A Shell download exceeded its wait limit; cancellation was requested.");
            Dispose();
            throw;
        }
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            FileStream file = new(savedPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                81920, FileOptions.SequentialScan);
            _readers++;
            return new Lease(file, ReleaseReader);
        }
    }

    private async Task<string> DownloadAsync(int index, string path, long expectedSize)
    {
        try
        {
            await _download(index, path, _stop.Token).ConfigureAwait(false);
            if (new FileInfo(path).Length != expectedSize)
            {
                throw new IOException("The remote file changed size during the transfer.");
            }

            return path;
        }
        finally
        {
            lock (_gate)
            {
                _downloads--;
                CleanupIfFinished();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) { return; }
            _disposed = true;
        }

        // Cancellation callbacks can block. Signal immediately, but do not run them on the caller.
        try { _ = ObserveCancellationAsync(_stop.CancelAsync()); }
        finally
        {
            lock (_gate) { CleanupIfFinished(); }
        }
    }

    private static async Task ObserveCancellationAsync(Task cancellation)
    {
        try { await cancellation.ConfigureAwait(false); }
        catch (Exception) { Heimdall.Core.Logging.FileLogger.Warn("A Shell cancellation callback failed."); }
    }

    private void ReleaseReader()
    {
        lock (_gate)
        {
            _readers--;
            CleanupIfFinished();
        }
    }

    private void CleanupIfFinished()
    {
        if (!_disposed || _downloads != 0 || _readers != 0 || _cleaned) { return; }
        try { Directory.Delete(DirectoryPath, recursive: true); _cleaned = true; }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record Entry(long Size, Task<string> Download);

    private sealed class Lease(Stream inner, Action release) : Stream
    {
        private Stream? _inner = inner;
        ~Lease() => Dispose(false);
        public override bool CanRead => _inner?.CanRead == true;
        public override bool CanSeek => _inner?.CanSeek == true;
        public override bool CanWrite => false;
        public override long Length => Current.Length;
        public override long Position { get => Current.Position; set => Current.Position = value; }
        private Stream Current => _inner ?? throw new ObjectDisposedException(nameof(Lease));
        public override int Read(byte[] buffer, int offset, int count) => Current.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => Current.Seek(offset, origin);
        public override void Flush() => Current.Flush();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            Stream? stream = Interlocked.Exchange(ref _inner, null);
            if (stream is not null)
            {
                try { stream.Dispose(); }
                catch (IOException) when (!disposing) { }
                finally { release(); }
            }
            base.Dispose(disposing);
        }
    }
}
