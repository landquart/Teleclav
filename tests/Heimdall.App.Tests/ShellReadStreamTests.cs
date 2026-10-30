/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Heimdall.App.Views;

namespace Heimdall.App.Tests;

public sealed class ShellReadStreamTests
{
    [Fact]
    public void CopyTo_PartialWriteReportsActualCountsAndFails()
    {
        using var source = new ShellReadStream(new MemoryStream("hello"u8.ToArray()));
        IntPtr read = Marshal.AllocCoTaskMem(sizeof(long));
        IntPtr written = Marshal.AllocCoTaskMem(sizeof(long));
        try
        {
            Assert.Throws<IOException>(() => source.CopyTo(new PartialStream(), 5, read, written));
            Assert.Equal(5, Marshal.ReadInt64(read));
            Assert.Equal(2, Marshal.ReadInt64(written));
        }
        finally { Marshal.FreeCoTaskMem(read); Marshal.FreeCoTaskMem(written); }
    }

    [Fact]
    public void ConcurrentDispose_ClosesUnderlyingStreamOnlyOnce()
    {
        var inner = new CountingStream();
        var source = new ShellReadStream(inner);
        Parallel.For(0, 1000, _ => source.Dispose());
        Assert.Equal(1, inner.DisposeCount);
    }

    private sealed class CountingStream : MemoryStream
    {
        public int DisposeCount;
        protected override void Dispose(bool disposing)
        {
            Interlocked.Increment(ref DisposeCount);
            base.Dispose(disposing);
        }
    }

    private sealed class PartialStream : IStream
    {
        public void Write(byte[] buffer, int count, IntPtr written) => Marshal.WriteInt32(written, 2);
        public void Read(byte[] buffer, int count, IntPtr read) => throw new NotSupportedException();
        public void Seek(long move, int origin, IntPtr position) => throw new NotSupportedException();
        public void SetSize(long size) => throw new NotSupportedException();
        public void CopyTo(IStream target, long count, IntPtr read, IntPtr written) => throw new NotSupportedException();
        public void Commit(int flags) => throw new NotSupportedException();
        public void Revert() => throw new NotSupportedException();
        public void LockRegion(long offset, long count, int type) => throw new NotSupportedException();
        public void UnlockRegion(long offset, long count, int type) => throw new NotSupportedException();
        public void Stat(out STATSTG stat, int flags) => throw new NotSupportedException();
        public void Clone(out IStream clone) => throw new NotSupportedException();
    }
}
