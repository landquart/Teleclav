/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Heimdall.App.Views;

/// <summary>A read-only COM stream backed by a temporary file, held until the Shell releases it.</summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class ShellReadStream : IShellNativeStream, IDisposable
{
    private readonly Stream _stream;
    private readonly string? _directory;
    private readonly object _gate = new();

    public ShellReadStream(FileStream stream, string directory) { _stream = stream; _directory = directory; }
    internal ShellReadStream(Stream stream) { _stream = stream; }
    private bool _disposed;

    ~ShellReadStream() => Dispose();

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) { return; }
            _disposed = true;
            try { _stream.Dispose(); } catch (IOException) { }
        }
        if (_directory is not null)
        {
            try { Directory.Delete(_directory); } catch (IOException) { } catch (UnauthorizedAccessException) { }
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

    private int ReadBytes(byte[] buffer, int count)
    {
        lock (_gate) { return _stream.Read(buffer, 0, count); }
    }

    public void Seek(long move, int origin, IntPtr position)
    {
        long n;
        lock (_gate) { n = _stream.Seek(move, (SeekOrigin)origin); }
        if (position != IntPtr.Zero) { Marshal.WriteInt64(position, n); }
    }

    public void Stat(out STATSTG stat, int flags)
    {
        lock (_gate) { stat = new STATSTG { type = 2, cbSize = _stream.Length }; }
    }

    public void CopyTo(IStream target, long count, IntPtr read, IntPtr written)
    {
        byte[] buffer = new byte[81920];
        long totalRead = 0;
        long totalWritten = 0;
        IntPtr amount = Marshal.AllocCoTaskMem(sizeof(int));
        try
        {
            while (totalRead < count)
            {
                int n;
                lock (_gate) { n = _stream.Read(buffer, 0, (int)Math.Min(buffer.Length, count - totalRead)); }
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
    }

    public void Write(byte[] buffer, int count, IntPtr written) => throw new COMException("Read only", unchecked((int)0x80030005));
    public void SetSize(long size) => throw new COMException("Read only", unchecked((int)0x80030005));
    public void Commit(int flags) { }
    public void Revert() => throw new COMException("Not supported", unchecked((int)0x80030001));
    public void LockRegion(long offset, long count, int type) => throw new COMException("Not supported", unchecked((int)0x80030001));
    public void UnlockRegion(long offset, long count, int type) => throw new COMException("Not supported", unchecked((int)0x80030001));
    public void Clone(out IStream clone) => throw new COMException("Not supported", unchecked((int)0x80030001));
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
    void Clone(out IStream clone);
}
