/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using Heimdall.Sftp;

namespace Heimdall.Sftp.Tests;

public sealed class GuardedSftpDownloadTests
{
    private static SftpFileInfo FileInfo(RemoteEntryKind kind = RemoteEntryKind.File, long size = 5, int seconds = 0)
        => new("a", "/a", kind, size, DateTime.UnixEpoch.AddSeconds(seconds), "", "", "");

    [Fact]
    public void Transfer_CopiesBytesAndChecksOpenedLength()
    {
        using MemoryStream output = new();
        GuardedSftpDownload.Copy("/a", 5, _ => FileInfo(), _ => new MemoryStream("hello"u8.ToArray()), output, CancellationToken.None);
        Assert.Equal("hello"u8.ToArray(), output.ToArray());
    }

    [Fact]
    public void Transfer_RefusesSymlinkBeforeOpening()
    {
        bool opened = false;
        using MemoryStream output = new();
        Assert.Throws<IOException>(() => GuardedSftpDownload.Copy("/a", 5, _ => FileInfo(RemoteEntryKind.SymbolicLink), _ => { opened = true; return Stream.Null; }, output, CancellationToken.None));
        Assert.False(opened);
    }

    [Fact]
    public void Transfer_RefusesFileThatChangesBeforeOpen()
    {
        int calls = 0;
        using MemoryStream output = new();
        Assert.Throws<IOException>(() => GuardedSftpDownload.Copy("/a", 5, _ => ++calls == 1 ? FileInfo() : FileInfo(seconds: 1), _ => new MemoryStream(), output, CancellationToken.None));
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public void Transfer_RefusesChangedOpenedLength()
    {
        using MemoryStream output = new();
        Assert.Throws<IOException>(() => GuardedSftpDownload.Copy("/a", 5, _ => FileInfo(), _ => new MemoryStream("other!"u8.ToArray()), output, CancellationToken.None));
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public void Transfer_RefusesMetadataChangeAfterRead()
    {
        int calls = 0;
        using MemoryStream output = new();
        Assert.Throws<IOException>(() => GuardedSftpDownload.Copy("/a", 5, _ => ++calls <= 3 ? FileInfo() : FileInfo(seconds: 1), _ => new MemoryStream("hello"u8.ToArray()), output, CancellationToken.None));
    }

    [Fact]
    public void Transfer_PropagatesCancellationAndNetworkFailure()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        using MemoryStream output = new();
        Assert.ThrowsAny<OperationCanceledException>(() => GuardedSftpDownload.Copy("/a", 5, _ => FileInfo(), _ => new MemoryStream(), output, stop.Token));
        Assert.Throws<IOException>(() => GuardedSftpDownload.Copy("/a", 5, _ => FileInfo(), _ => throw new IOException("network"), output, CancellationToken.None));
    }
    [Fact]
    public void Transfer_RefusesLinkDetectedImmediatelyAfterOpen()
    {
        int calls = 0;
        using MemoryStream output = new();
        Assert.Throws<IOException>(() => GuardedSftpDownload.Copy("/a", 5,
            _ => ++calls < 3 ? FileInfo() : FileInfo(RemoteEntryKind.SymbolicLink),
            _ => new MemoryStream("hello"u8.ToArray()), output, CancellationToken.None));
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public void Transfer_RejectsTruncatedReadDespiteClaimedHandleSize()
    {
        using MemoryStream output = new();
        Assert.Throws<IOException>(() => GuardedSftpDownload.Copy("/a", 5,
            _ => FileInfo(), _ => new ReportedLengthStream("abc"u8.ToArray(), 5), output, CancellationToken.None));
    }

    [Fact]
    public void Transfer_RejectsExcessDataDespiteClaimedHandleSize()
    {
        using MemoryStream output = new();
        Assert.Throws<IOException>(() => GuardedSftpDownload.Copy("/a", 5,
            _ => FileInfo(), _ => new ReportedLengthStream("abcdef"u8.ToArray(), 5), output, CancellationToken.None));
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public void Transfer_CancelledAfterOpenDoesNotStartReading()
    {
        using var stop = new CancellationTokenSource();
        using MemoryStream output = new();
        var input = new CountingReadStream("hello"u8.ToArray());
        int calls = 0;
        Assert.ThrowsAny<OperationCanceledException>(() => GuardedSftpDownload.Copy("/a", 5,
            _ => { if (++calls == 3) { stop.Cancel(); } return FileInfo(); },
            _ => input, output, stop.Token));
        Assert.Equal(0, input.Reads);
    }

    [Fact]
    public void Transfer_ReadFailureAfterPartialDataPropagatesAndClosesSource()
    {
        var input = new InterruptedStream();
        using MemoryStream output = new();
        Assert.Throws<IOException>(() => GuardedSftpDownload.Copy("/a", 5, _ => FileInfo(), _ => input, output, CancellationToken.None));
        Assert.True(input.Closed);
        Assert.Equal("he"u8.ToArray(), output.ToArray());
    }

    [Fact]
    public void Transfer_MatchingMetadataCannotDetectSameSizeSubstitution()
    {
        using MemoryStream output = new();
        GuardedSftpDownload.Copy("/a", 5, _ => FileInfo(),
            _ => new MemoryStream("other"u8.ToArray()), output, CancellationToken.None);
        // This characterizes a protocol/API limitation, not an integrity guarantee.
        Assert.Equal("other"u8.ToArray(), output.ToArray());
    }

    [Fact]
    public void Transfer_CancellationDuringReadDoesNotWriteReturnedBytes()
    {
        using var stop = new CancellationTokenSource();
        using MemoryStream output = new();
        var input = new CancellingStream(stop);
        Assert.ThrowsAny<OperationCanceledException>(() => GuardedSftpDownload.Copy("/a", 5,
            _ => FileInfo(), _ => input, output, stop.Token));
        Assert.Empty(output.ToArray());
        Assert.True(input.Closed);
    }

    private sealed class CancellingStream(CancellationTokenSource stop) : MemoryStream("hello"u8.ToArray())
    {
        public bool Closed { get; private set; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = base.Read(buffer, offset, count);
            stop.Cancel();
            return read;
        }
        protected override void Dispose(bool disposing) { Closed = true; base.Dispose(disposing); }
    }

    private sealed class InterruptedStream : MemoryStream
    {
        private int _reads;
        public bool Closed { get; private set; }
        public override long Length => 5;
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_reads++ != 0) { throw new IOException("network read failed"); }
            buffer[offset] = (byte)'h'; buffer[offset + 1] = (byte)'e';
            return 2;
        }
        protected override void Dispose(bool disposing) { Closed = true; base.Dispose(disposing); }
    }

    private sealed class CountingReadStream(byte[] data) : MemoryStream(data)
    {
        public int Reads { get; private set; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            Reads++;
            return base.Read(buffer, offset, count);
        }
    }

    private sealed class ReportedLengthStream(byte[] data, long length) : MemoryStream(data)
    {
        public override long Length => length;
    }
}
