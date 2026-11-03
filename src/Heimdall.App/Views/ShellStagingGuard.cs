/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Heimdall.App.Views;

/// <summary>Pins the staging path and deletes only recorded files through verified Windows handles.</summary>
[SupportedOSPlatform("windows")]
internal sealed partial class ShellStagingGuard : IDisposable
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

    internal string DirectoryPath => _path;

    internal static ShellStagingGuard Create()
        => CreateAt(DefaultRootPath);

    internal static ShellStagingGuard CreateAt(string root)
        => new(Path.Combine(root, Guid.NewGuid().ToString("N")), reopen: false);

    private ShellStagingGuard(string path, bool reopen)
    {
        ValidatePath(path);
        _path = Path.GetFullPath(path);
        try
        {
            if (reopen) { RestorePersistent(); }
            else { InitializePersistent(); }
        }
        catch { Dispose(); throw; }
    }

    internal void Record(string name)
    {
        ValidateFileName(name);
        using SafeFileHandle file = Open(Path.Combine(_path, name), ReadAttributes);
        FileIdentity identity = Identity(file);
        if ((identity.Attributes & (DirectoryAttribute | ReparseAttribute)) != 0)
            throw new IOException("A staging file is a directory or reparse point.");
        lock (_gate)
        {
            if (_files.ContainsKey(name)) { throw new IOException("Staging file is already registered."); }
            _journal!.AppendFile(name, identity);
            _files.Add(name, identity);
        }
    }

    internal void Verify(string name, SafeFileHandle file)
    {
        FileIdentity actual = Identity(file);
        lock (_gate)
        {
            if (!_files.TryGetValue(name, out FileIdentity expected) || actual != expected)
                throw new IOException("Staging file identity changed; reading refused.");
        }
    }

    internal void Delete()
    {
        SafeFileHandle directory = _directory ?? throw new ObjectDisposedException(nameof(ShellStagingGuard));
        _journal?.MarkCleanup();
        string[] paths = Directory.GetFileSystemEntries(_path);
        var handles = new List<SafeFileHandle>();
        try
        {
            foreach (string path in paths)
            {
                string name = Path.GetFileName(path);
                if (_journal is not null && name == OwnershipName) { continue; }
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
        _journal?.Delete();
        _journal = null;
        MarkDelete(directory);
        Dispose();
    }

    public void Dispose()
    {
        _journal?.Dispose();
        _journal = null;
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
        if (!GetFileInformationByHandleEx(handle, 18, out FileIdInformation id, (uint)Marshal.SizeOf<FileIdInformation>()))
            throw new IOException("Unable to obtain a persistent staging identity.", new Win32Exception(Marshal.GetLastWin32Error()));
        return new FileIdentity(value.Attributes, id.Volume, id.High, id.Low);
    }

    private static void MarkDelete(SafeFileHandle handle)
    {
        byte delete = 1;
        if (!SetFileInformationByHandle(handle, 4, ref delete, 1))
            throw new IOException("Unable to delete a verified staging object.", new Win32Exception(Marshal.GetLastWin32Error()));
    }

    private static SafeFileHandle CreateRelative(SafeFileHandle parent, string name, byte[] descriptor)
    {
        IntPtr nameBuffer = Marshal.StringToHGlobalUni(name);
        IntPtr unicodePointer = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
        GCHandle security = GCHandle.Alloc(descriptor, GCHandleType.Pinned);
        bool added = false;
        try
        {
            parent.DangerousAddRef(ref added);
        }
        catch
        {
            security.Free();
            Marshal.FreeHGlobal(unicodePointer);
            Marshal.FreeHGlobal(nameBuffer);
            throw;
        }
        try
        {
            var unicode = new UnicodeString
            {
                Length = checked((ushort)(name.Length * 2)),
                MaximumLength = checked((ushort)((name.Length + 1) * 2)),
                Buffer = nameBuffer,
            };
            Marshal.StructureToPtr(unicode, unicodePointer, false);
            var attributes = new ObjectAttributes
            {
                Length = Marshal.SizeOf<ObjectAttributes>(),
                RootDirectory = parent.DangerousGetHandle(),
                ObjectName = unicodePointer,
                Attributes = 0x40,
                SecurityDescriptor = security.AddrOfPinnedObject(),
            };
            int status = NtCreateFile(out SafeFileHandle handle, DeleteAccess | ReadAttributes | ReadControl | 0x00100027,
                ref attributes, out _, IntPtr.Zero, DirectoryAttribute, ShareReadWrite, 2, 0x21, IntPtr.Zero, 0);
            if (status < 0)
            {
                handle?.Dispose();
                throw new IOException("Unable to create staging directory safely.", new Win32Exception((int)RtlNtStatusToDosError(status)));
            }
            return handle;
        }
        finally
        {
            if (added) { parent.DangerousRelease(); }
            security.Free();
            Marshal.FreeHGlobal(unicodePointer);
            Marshal.FreeHGlobal(nameBuffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString { internal ushort Length; internal ushort MaximumLength; internal IntPtr Buffer; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes
    {
        internal int Length;
        internal IntPtr RootDirectory;
        internal IntPtr ObjectName;
        internal uint Attributes;
        internal IntPtr SecurityDescriptor;
        internal IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock { internal IntPtr Status; internal UIntPtr Information; }

    [DllImport("ntdll.dll")]
    private static extern int NtCreateFile(out SafeFileHandle handle, uint access, ref ObjectAttributes attributes,
        out IoStatusBlock ioStatus, IntPtr allocationSize, uint fileAttributes, uint share, uint disposition,
        uint options, IntPtr eaBuffer, uint eaLength);

    [DllImport("ntdll.dll")]
    private static extern uint RtlNtStatusToDosError(int status);

    internal readonly record struct FileIdentity(uint Attributes, ulong Volume, ulong IndexHigh, ulong IndexLow);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInformation { internal ulong Volume; internal ulong Low; internal ulong High; }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass,
        out FileIdInformation information, uint size);

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
