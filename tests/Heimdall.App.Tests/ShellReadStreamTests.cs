/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Heimdall.App.Views;

namespace Heimdall.App.Tests;

public sealed class ShellReadStreamTests
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int NativeRead(IntPtr self, IntPtr buffer, uint count, IntPtr read);

    [Fact]
    public async Task Clones_AllowConcurrentIndependentReads()
    {
        using var original = new ShellReadStream(new MemoryStream("hello"u8.ToArray()));
        original.Clone(out var native);
        using var clone = Assert.IsType<ShellReadStream>(native);
        async Task ReadAll(ShellReadStream reader)
        {
            await Task.Run(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    reader.Seek(0, 0, IntPtr.Zero);
                    byte[] bytes = new byte[5];
                    reader.Read(bytes, 5, IntPtr.Zero);
                    Assert.Equal("hello"u8.ToArray(), bytes);
                }
            });
        }
        await Task.WhenAll(ReadAll(original), ReadAll(clone));
    }

    [Fact]
    public void DisposedStream_RejectsZeroByteRead()
    {
        using var stream = new ShellReadStream(new MemoryStream());
        stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => stream.Read([], 0, IntPtr.Zero));
    }

    [Fact]
    public void Clone_KeepsIndependentPositionAndContentAfterOriginalDisposal()
    {
        using var original = new ShellReadStream(new MemoryStream("hello"u8.ToArray()));
        original.Seek(1, 0, IntPtr.Zero);
        ((IShellNativeStream)original).Clone(out var nativeClone);
        using var clone = Assert.IsType<ShellReadStream>(nativeClone);
        byte[] buffer = new byte[1];
        clone.Read(buffer, 1, IntPtr.Zero);
        Assert.Equal((byte)'e', buffer[0]);
        original.Read(buffer, 1, IntPtr.Zero);
        Assert.Equal((byte)'e', buffer[0]);
        original.Dispose();
        clone.Read(buffer, 1, IntPtr.Zero);
        Assert.Equal((byte)'l', buffer[0]);
    }

    [Fact]
    public void Read_FillsTheRequestAcrossShortUnderlyingReads()
    {
        using var stream = new ShellReadStream(new ShortReadStream());
        byte[] bytes = new byte[5];
        IntPtr read = Marshal.AllocCoTaskMem(sizeof(int));
        try
        {
            stream.Read(bytes, bytes.Length, read);
            Assert.Equal(5, Marshal.ReadInt32(read));
            Assert.Equal("hello"u8.ToArray(), bytes);
        }
        finally { Marshal.FreeCoTaskMem(read); }
    }

    private sealed class ShortReadStream() : MemoryStream("hello"u8.ToArray())
    {
        public override int Read(byte[] buffer, int offset, int count)
            => base.Read(buffer, offset, Math.Min(count, 1));
    }

    [Fact]
    public void NativeRead_StagingFailureReturnsFailingHResult()
    {
        Exception? reported = null;
        using var inner = new ShellDeferredStream(_ => Task.FromException<Stream>(new IOException("network")),
            5, ex => reported = ex, () => { });
        using var stream = new ShellReadStream(inner);
        IntPtr com = Marshal.GetComInterfaceForObject(stream, typeof(IShellNativeStream));
        IntPtr buffer = Marshal.AllocCoTaskMem(5);
        IntPtr read = Marshal.AllocCoTaskMem(sizeof(int));
        try
        {
            IntPtr vtable = Marshal.ReadIntPtr(com);
            var nativeRead = Marshal.GetDelegateForFunctionPointer<NativeRead>(Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size));
            Assert.True(nativeRead(com, buffer, 5, read) < 0);
            Assert.IsType<IOException>(reported);
        }
        finally
        {
            Marshal.Release(com);
            Marshal.FreeCoTaskMem(buffer);
            Marshal.FreeCoTaskMem(read);
        }
    }

    [Fact]
    public void NativeRead_FullReadSucceedsAndNextReadReportsEndOfStream()
    {
        using var stream = new ShellReadStream(new MemoryStream("hi"u8.ToArray()));
        IntPtr com = Marshal.GetComInterfaceForObject(stream, typeof(IShellNativeStream));
        IntPtr buffer = Marshal.AllocCoTaskMem(2);
        IntPtr read = Marshal.AllocCoTaskMem(sizeof(int));
        try
        {
            Guid iid = typeof(IStream).GUID;
            Assert.Equal(0, Marshal.QueryInterface(com, in iid, out IntPtr queried));
            Marshal.Release(queried);
            IntPtr vtable = Marshal.ReadIntPtr(com);
            var nativeRead = Marshal.GetDelegateForFunctionPointer<NativeRead>(Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size));
            Assert.Equal(0, nativeRead(com, buffer, 2, read));
            Assert.Equal(2, Marshal.ReadInt32(read));
            Assert.Equal(1, nativeRead(com, buffer, 2, read));
            Assert.Equal(0, Marshal.ReadInt32(read));
        }
        finally
        {
            Marshal.Release(com);
            Marshal.FreeCoTaskMem(buffer);
            Marshal.FreeCoTaskMem(read);
        }
    }

    [Fact]
    public void NativeRead_ReturnsSFalseWhenEndOfStreamIsReached()
    {
        using var stream = new ShellReadStream(new MemoryStream("hi"u8.ToArray()));
        IntPtr com = Marshal.GetComInterfaceForObject(stream, typeof(IShellNativeStream));
        IntPtr buffer = Marshal.AllocCoTaskMem(4);
        IntPtr read = Marshal.AllocCoTaskMem(sizeof(int));
        try
        {
            IntPtr vtable = Marshal.ReadIntPtr(com);
            var nativeRead = Marshal.GetDelegateForFunctionPointer<NativeRead>(Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size));
            Assert.Equal(1, nativeRead(com, buffer, 4, read));
            Assert.Equal(2, Marshal.ReadInt32(read));
            Assert.Equal((byte)'h', Marshal.ReadByte(buffer));
            Assert.Equal((byte)'i', Marshal.ReadByte(buffer, 1));
        }
        finally
        {
            Marshal.Release(com);
            Marshal.FreeCoTaskMem(buffer);
            Marshal.FreeCoTaskMem(read);
        }
    }

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
