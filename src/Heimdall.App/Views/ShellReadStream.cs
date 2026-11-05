/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Heimdall.App.Views;

/// <summary>A read-only COM stream backed by a temporary file, held until the Shell releases it.</summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class ShellReadStream : IShellNativeStream, IShellNativeSequentialStream, IDisposable
{
    private readonly SharedSource _source;
    private long _position;
    private int _disposed;

    public ShellReadStream(FileStream stream, string directory) : this(new SharedSource(stream, directory), 0) { }
    internal ShellReadStream(Stream stream) : this(new SharedSource(stream, null), 0) { }
    private ShellReadStream(SharedSource source, long position) { _source = source; _position = position; }

    private sealed class SharedSource(Stream stream, string? directory)
    {
        internal readonly Stream Stream = stream;
        internal readonly string? Directory = directory;
        internal readonly object Gate = new();
        internal int References = 1;
    }

    ~ShellReadStream() => Dispose();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
        if (Interlocked.Decrement(ref _source.References) == 0)
        {
            if (_source.Stream is ShellDeferredStream deferred) { deferred.CancelPending(); }
            lock (_source.Gate)
            {
                try { _source.Stream.Dispose(); } catch (IOException) { }
            }
            if (_source.Directory is not null)
            {
                try { Directory.Delete(_source.Directory); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        GC.SuppressFinalize(this);
    }

    public void Read(byte[] buffer, int count, IntPtr read)
    {
        int n = ReadBytes(buffer, count);
        if (read != IntPtr.Zero) { Marshal.WriteInt32(read, n); }
    }

    int IShellNativeStream.Read(byte[] buffer, int count, IntPtr read)
    {
        int n = ReadBytes(buffer, count);
        if (read != IntPtr.Zero) { Marshal.WriteInt32(read, n); }
        return n == count ? 0 : 1;
    }

    int IShellNativeSequentialStream.Read(byte[] buffer, int count, IntPtr read)
        => ((IShellNativeStream)this).Read(buffer, count, read);

    private int ReadBytes(byte[] buffer, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > buffer.Length) { throw new ArgumentException("The buffer is smaller than the request.", nameof(buffer)); }
        lock (_source.Gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            _source.Stream.Seek(_position, SeekOrigin.Begin);
            int total = 0;
            while (total < count)
            {
                int received = _source.Stream.Read(buffer, total, count - total);
                if (received == 0) { break; }
                total += received;
                _position += received;
            }
            return total;
        }
    }

    public void Seek(long move, int origin, IntPtr position)
    {
        long n;
        lock (_source.Gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            long basis = origin switch
            {
                0 => 0,
                1 => _position,
                2 => _source.Stream.Length,
                _ => throw new COMException("Invalid seek origin", unchecked((int)0x80030001)),
            };
            try { n = checked(basis + move); }
            catch (OverflowException) { throw new COMException("Invalid seek position", unchecked((int)0x80030019)); }
            if (n < 0) { throw new COMException("Invalid seek position", unchecked((int)0x80030019)); }
            _position = n;
        }
        if (position != IntPtr.Zero) { Marshal.WriteInt64(position, n); }
    }

    public void Stat(out STATSTG stat, int flags)
    {
        lock (_source.Gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            stat = new STATSTG { type = 2, cbSize = _source.Stream.Length };
        }
    }

    public void CopyTo(IStream target, long count, IntPtr read, IntPtr written)
        => CopyBytes(target, count, read, written);

    private long CopyBytes(IStream target, long count, IntPtr read, IntPtr written)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        byte[] buffer = new byte[81920];
        long totalRead = 0;
        long totalWritten = 0;
        IntPtr amount = Marshal.AllocCoTaskMem(sizeof(int));
        try
        {
            while (totalRead < count)
            {
                int n = ReadBytes(buffer, (int)Math.Min(buffer.Length, count - totalRead));
                if (n == 0) { break; }
                totalRead += n;
                Marshal.WriteInt32(amount, 0);
                target.Write(buffer, n, amount);
                int actual = Marshal.ReadInt32(amount);
                if (actual < 0 || actual > n) { throw new IOException("The destination reported an invalid byte count."); }
                totalWritten += actual;
                if (actual != n) { throw new IOException("The destination did not accept all bytes."); }
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(amount);
            if (read != IntPtr.Zero) { Marshal.WriteInt64(read, totalRead); }
            if (written != IntPtr.Zero) { Marshal.WriteInt64(written, totalWritten); }
        }
        return totalWritten;
    }

    public void Write(byte[] buffer, int count, IntPtr written) => throw new COMException("Read only", unchecked((int)0x80030005));
    public void SetSize(long size) => throw new COMException("Read only", unchecked((int)0x80030005));
    public void Commit(int flags) { }
    public void Revert() => throw new COMException("Not supported", unchecked((int)0x80030001));
    public void LockRegion(long offset, long count, int type) => throw new COMException("Not supported", unchecked((int)0x80030001));
    public void UnlockRegion(long offset, long count, int type) => throw new COMException("Not supported", unchecked((int)0x80030001));
    public void Clone(out IShellNativeStream clone)
    {
        lock (_source.Gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var copy = new ShellReadStream(_source, _position);
            Interlocked.Increment(ref _source.References);
            clone = copy;
        }
    }
}

// ComTypes.IStream.Read discards successful HRESULTs. Explorer needs the native
// IStream IID and S_FALSE on short reads, so preserve the Read return value.
[ComVisible(true)]
[Guid("0000000C-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellNativeStream
{
    [PreserveSig]
    int Read([Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] buffer, int count, IntPtr read);
    void Write([In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] buffer, int count, IntPtr written);
    void Seek(long move, int origin, IntPtr position);
    void SetSize(long size);
    void CopyTo(IStream target, long count, IntPtr read, IntPtr written);
    void Commit(int flags);
    void Revert();
    void LockRegion(long offset, long count, int type);
    void UnlockRegion(long offset, long count, int type);
    void Stat(out STATSTG stat, int flags);
    void Clone(out IShellNativeStream clone);
}

[ComVisible(true)]
[Guid("0C733A30-2A1C-11CE-ADE5-00AA0044773D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellNativeSequentialStream
{
    [PreserveSig]
    int Read([Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] buffer, int count, IntPtr read);
    void Write([In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] buffer, int count, IntPtr written);
}
