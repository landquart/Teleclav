/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Heimdall.App.Views;

/// <summary>Pins the staging path and deletes only recorded files through verified Windows handles.</summary>
[SupportedOSPlatform("windows")]
internal sealed class ShellStagingGuard : IDisposable
{
    private const uint DeleteAccess = 0x00010000;
    private const uint ReadAttributes = 0x00000080;
    private const uint ShareReadWrite = 3;
    private const uint OpenExisting = 3;
    private const uint OpenReparsePoint = 0x00200000;
    private const uint BackupSemantics = 0x02000000;
    private const uint DirectoryAttribute = 0x10;
    private const uint ReparseAttribute = 0x400;
    private readonly List<SafeFileHandle> _ancestors = [];
    private readonly Dictionary<string, FileIdentity> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly string _path;
    private SafeFileHandle? _directory;

    internal ShellStagingGuard(string path)
    {
        _path = Path.GetFullPath(path);
        try
        {
            var parents = new Stack<string>();
            DirectoryInfo? parent = Directory.GetParent(_path);
            while (parent is not null) { parents.Push(parent.FullName); parent = parent.Parent; }
            while (parents.TryPop(out string? item))
            {
                SafeFileHandle handle = Open(item, ReadAttributes);
                _ancestors.Add(handle);
                RequireDirectory(handle);
            }
            _directory = Open(_path, DeleteAccess | ReadAttributes);
            RequireDirectory(_directory);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal void Record(string name)
    {
        using SafeFileHandle file = Open(Path.Combine(_path, name), ReadAttributes);
        FileIdentity identity = Identity(file);
        if ((identity.Attributes & (DirectoryAttribute | ReparseAttribute)) != 0)
            throw new IOException("A staging file is a directory or reparse point.");
        lock (_gate) { _files.Add(name, identity); }
    }

    internal void Delete()
    {
        SafeFileHandle directory = _directory ?? throw new ObjectDisposedException(nameof(ShellStagingGuard));
        string[] paths = Directory.GetFileSystemEntries(_path);
        var handles = new List<SafeFileHandle>();
        try
        {
            foreach (string path in paths)
            {
                string name = Path.GetFileName(path);
                if (!_files.TryGetValue(name, out FileIdentity expected))
                    throw new IOException("Staging contains an unrecognized entry; cleanup refused.");
                SafeFileHandle file = Open(path, DeleteAccess | ReadAttributes);
                handles.Add(file);
                if (Identity(file) != expected)
                    throw new IOException("Staging file identity changed; cleanup refused.");
            }
            foreach (SafeFileHandle file in handles) { MarkDelete(file); }
        }
        finally
        {
            foreach (SafeFileHandle file in handles) { file.Dispose(); }
        }
        MarkDelete(directory);
        Dispose();
    }

    public void Dispose()
    {
        _directory?.Dispose();
        _directory = null;
        foreach (SafeFileHandle handle in _ancestors) { handle.Dispose(); }
        _ancestors.Clear();
    }

    private static SafeFileHandle Open(string path, uint access)
    {
        SafeFileHandle handle = CreateFile(path, access, ShareReadWrite, IntPtr.Zero, OpenExisting,
            BackupSemantics | OpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new IOException("Unable to open a staging object safely.", new Win32Exception(error));
        }
        return handle;
    }

    private static void RequireDirectory(SafeFileHandle handle)
    {
        FileIdentity value = Identity(handle);
        if ((value.Attributes & DirectoryAttribute) == 0 || (value.Attributes & ReparseAttribute) != 0)
            throw new IOException("Staging path contains a reparse point or non-directory.");
    }

    private static FileIdentity Identity(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out FileInformation value))
            throw new IOException("Unable to identify a staging object.", new Win32Exception(Marshal.GetLastWin32Error()));
        return new FileIdentity(value.Attributes, value.VolumeSerialNumber, value.FileIndexHigh, value.FileIndexLow);
    }

    private static void MarkDelete(SafeFileHandle handle)
    {
        byte delete = 1;
        if (!SetFileInformationByHandle(handle, 4, ref delete, 1))
            throw new IOException("Unable to delete a verified staging object.", new Win32Exception(Marshal.GetLastWin32Error()));
    }

    private readonly record struct FileIdentity(uint Attributes, uint Volume, uint IndexHigh, uint IndexLow);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        internal uint Attributes;
        internal System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        internal uint VolumeSerialNumber;
        internal uint FileSizeHigh;
        internal uint FileSizeLow;
        internal uint NumberOfLinks;
        internal uint FileIndexHigh;
        internal uint FileIndexLow;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref byte information, uint size);
}
