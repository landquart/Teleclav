/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Heimdall.App.Views;

/// <summary>A read-only COM stream backed by a temporary file, held until the Shell releases it.</summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class ShellReadStream(FileStream stream, string directory) : IStream, IDisposable
{
    private readonly FileStream _stream = stream;
    private readonly string _directory = directory;
    private bool _disposed;

    ~ShellReadStream() => Dispose();

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        try { _stream.Dispose(); } catch (IOException) { }
        try { Directory.Delete(_directory); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    public void Read(byte[] buffer, int count, IntPtr read)
    {
        int n = _stream.Read(buffer, 0, count);
        if (read != IntPtr.Zero) { Marshal.WriteInt32(read, n); }
    }

    public void Seek(long move, int origin, IntPtr position)
    {
        long n = _stream.Seek(move, (SeekOrigin)origin);
        if (position != IntPtr.Zero) { Marshal.WriteInt64(position, n); }
    }

    public void Stat(out STATSTG stat, int flags)
    {
        stat = new STATSTG { type = 2, cbSize = _stream.Length };
    }

    public void CopyTo(IStream target, long count, IntPtr read, IntPtr written)
    {
        byte[] buffer = new byte[81920];
        long total = 0;
        IntPtr amount = Marshal.AllocCoTaskMem(sizeof(int));
        try
        {
            while (total < count)
            {
                int n = _stream.Read(buffer, 0, (int)Math.Min(buffer.Length, count - total));
                if (n == 0) { break; }
                Marshal.WriteInt32(amount, 0);
                target.Write(buffer, n, amount);
                int actual = Marshal.ReadInt32(amount);
                if (actual != n) { throw new IOException("The destination did not accept all bytes."); }
                total += actual;
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(amount);
            if (read != IntPtr.Zero) { Marshal.WriteInt64(read, total); }
            if (written != IntPtr.Zero) { Marshal.WriteInt64(written, total); }
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
