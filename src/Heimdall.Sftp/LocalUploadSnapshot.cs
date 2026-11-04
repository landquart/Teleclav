/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Heimdall.Sftp;

/// <summary>Windows upload source identity, captured before remote conflict resolution.</summary>
[SupportedOSPlatform("windows")]
public sealed class LocalUploadSnapshot
{
    private readonly Entry[] _entries;

    private LocalUploadSnapshot(string path, Entry[] entries)
    {
        Path = path;
        _entries = entries;
    }

    /// <summary>The source path used to reopen the verified chain.</summary>
    public string Path { get; }
    /// <summary>Whether the source is a directory.</summary>
    public bool IsDirectory => (_entries[^1].Attributes & FileAttributes.Directory) != 0;
    /// <summary>File length at planning time.</summary>
    public long Length => _entries[^1].Length;
    /// <summary>Last modification time observed at planning.</summary>
    public DateTime LastWriteTimeUtc => DateTime.FromFileTimeUtc(_entries[^1].LastWrite);

    /// <summary>Captures the identity of every path component without traversing reparse points.</summary>
    public static LocalUploadSnapshot Capture(string path, LocalUploadSnapshot? parent = null)
    {
        string full = NormalizePath(path);
        if (parent is not null && (!parent.IsDirectory ||
            !string.Equals(System.IO.Path.GetDirectoryName(full), parent.Path, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("The upload entry is outside its planned parent.");
        using var chain = new OpenChain(full, parent?._entries);
        return new LocalUploadSnapshot(full, chain.Entries);
    }

    /// <summary>Pins the verified directory while its immediate children are enumerated.</summary>
    public IDisposable PinDirectory()
    {
        if (!IsDirectory) { throw new IOException("The planned upload source is not a directory."); }
        return new OpenChain(Path, _entries);
    }

    /// <summary>Opens the verified file for reading, denying concurrent write and delete opens.</summary>
    public FileStream OpenRead()
    {
        if (IsDirectory)
            throw new LocalUploadFileValidationException(Path, LocalUploadFileValidationFailure.NotRegularFile);
        using var chain = new OpenChain(Path, _entries, read: true);
        SafeFileHandle handle = chain.TakeLeaf();
        try { return new FileStream(handle, FileAccess.Read, 81920, isAsync: false); }
        catch { handle.Dispose(); throw; }
    }

    /// <summary>Enumerates and captures immediate children through the pinned directory handle.</summary>
    public IReadOnlyList<LocalUploadSnapshot> ReadChildren(
        ICollection<string> skippedLinks, ICollection<string> skippedSources, CancellationToken ct)
    {
        if (!IsDirectory) { throw new IOException("The planned upload source is not a directory."); }

        using var chain = new OpenChain(Path, _entries, read: true);
        var children = new List<LocalUploadSnapshot>();
        foreach (string name in EnumerateNames(chain.Leaf, ct))
        {
            string childPath = System.IO.Path.Combine(Path, name);
            try
            {
                using SafeFileHandle child = OpenChild(chain.Leaf, name, childPath, read: false, traverse: false);
                var entries = new List<Entry>(_entries);
                AddEntry(child, entries, expected: null, directory: false, checkContents: false);
                children.Add(new LocalUploadSnapshot(childPath, entries.ToArray()));
            }
            catch (LocalUploadReparseException) { skippedLinks.Add(childPath); }
            catch (IOException) { skippedSources.Add(childPath); }
        }
        return children;
    }

    private static IEnumerable<string> EnumerateNames(SafeFileHandle directory, CancellationToken ct)
    {
        var buffer = new byte[4096];
        bool restart = true;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int status = NtQueryDirectoryFile(directory, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                out IoStatusBlock result, buffer, (uint)buffer.Length, 12, true, IntPtr.Zero, restart);
            restart = false;
            if (status == unchecked((int)0x80000006)) { yield break; }
            if (status != 0)
                throw new IOException("Cannot enumerate the verified local upload directory.",
                    new Win32Exception((int)RtlNtStatusToDosError(status)));

            uint length = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(8));
            ulong bytes = result.Information.ToUInt64();
            if (bytes < 12 || bytes > (ulong)buffer.Length || length == 0 || (length & 1) != 0 || length > bytes - 12)
                throw new IOException("Invalid local upload directory entry.");
            string name = Encoding.Unicode.GetString(buffer, 12, (int)length);
            if (name is "." or "..") { continue; }
            if (!SftpPathGuard.IsValidChildName(name) || name.Contains(':'))
                throw new IOException("The local upload directory contains an unsafe name.");
            yield return name;
        }
    }

    internal IDisposable PinReadPath() => new OpenChain(Path, _entries, read: true);

    internal void VerifyRead(SafeFileHandle handle)
    {
        Entry actual = Inspect(handle);
        if (!_entries[^1].Matches(actual, checkContents: true))
            throw new IOException("The local upload file changed while it was being read.");
    }

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = System.IO.Path.GetFullPath(path);
        if (full.StartsWith(@"\\?\", StringComparison.Ordinal)) { full = full[4..]; }
        string? root = System.IO.Path.GetPathRoot(full);
        if (root is not { Length: 3 } || !char.IsAsciiLetter(root[0]) || root[1] != ':' ||
            full.AsSpan(3).Contains(':'))
            throw new IOException("Upload sources must be ordinary paths on a local NTFS volume.");
        return System.IO.Path.TrimEndingDirectorySeparator(full);
    }

    private sealed class OpenChain : IDisposable
    {
        private readonly List<SafeFileHandle> _handles = [];
        internal Entry[] Entries { get; }
        internal SafeFileHandle Leaf => _handles[^1];

        internal OpenChain(string path, Entry[]? expected, bool read = false)
        {
            try
            {
                string root = path[..3];
                string[] names = path[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
                var entries = new List<Entry>(names.Length + 1);
                SafeFileHandle volume = CreateFileW(@"\\?\" + root, 0xA0, 3, IntPtr.Zero, 3,
                    0x02200000, IntPtr.Zero);
                if (volume.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    volume.Dispose();
                    throw OpenError(path, error);
                }
                _handles.Add(volume);
                var fs = new StringBuilder(32);
                if (GetDriveTypeW(root) is not (2 or 3) ||
                    !GetVolumeInformationByHandleW(volume, IntPtr.Zero, 0, out _, out _, out _, fs, fs.Capacity) ||
                    fs.ToString() != "NTFS")
                    throw new IOException("The upload source volume cannot provide the required NTFS identity.");
                AddEntry(volume, entries, expected, directory: true, checkContents: false);

                for (int i = 0; i < names.Length; i++)
                {
                    bool leaf = i == names.Length - 1;
                    SafeFileHandle handle = OpenChild(_handles[^1], names[i], path, read && leaf, !leaf || read && leaf &&
                        expected is not null && (expected[^1].Attributes & FileAttributes.Directory) != 0);
                    _handles.Add(handle);
                    AddEntry(handle, entries, expected, directory: !leaf, checkContents: leaf);
                }

                Entries = entries.ToArray();
            }
            catch { Dispose(); throw; }
        }

        internal SafeFileHandle TakeLeaf()
        {
            SafeFileHandle leaf = _handles[^1];
            _handles.RemoveAt(_handles.Count - 1);
            return leaf;
        }

        public void Dispose()
        {
            for (int i = _handles.Count - 1; i >= 0; i--) { _handles[i].Dispose(); }
        }
    }

    private static void AddEntry(SafeFileHandle handle, List<Entry> entries, Entry[]? expected,
        bool directory, bool checkContents)
    {
        Entry actual = Inspect(handle);
        if ((actual.Attributes & FileAttributes.ReparsePoint) != 0) { throw new LocalUploadReparseException(); }
        if (directory && (actual.Attributes & FileAttributes.Directory) == 0)
            throw new IOException("An upload ancestor is not a directory.");
        if (entries.Count > 0 && entries[0].Id.Volume != actual.Id.Volume)
            throw new IOException("The upload source changed volumes.");
        if ((actual.Attributes & (FileAttributes.Directory | FileAttributes.Device)) == 0 && actual.Links != 1)
            throw new IOException("The upload source has more than one hard link or its link count is unavailable.");
        if ((actual.Attributes & FileAttributes.Device) != 0)
            throw new IOException("The upload source is not an ordinary file or directory.");
        if (expected is not null && entries.Count < expected.Length &&
            !expected[entries.Count].Matches(actual, checkContents))
            throw new IOException("The upload source changed after planning.");
        entries.Add(actual);
    }

    private static Entry Inspect(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out FileInformation info) ||
            !GetFileInformationByHandleEx(handle, 18, out FileId id, (uint)Marshal.SizeOf<FileId>()))
            throw new IOException("Cannot verify the local upload source.", new Win32Exception(Marshal.GetLastWin32Error()));
        return new Entry(info.Attributes, id, info.Links,
            ((long)info.LengthHigh << 32) | info.LengthLow, info.LastWrite);
    }

    private static SafeFileHandle OpenChild(SafeFileHandle parent, string name, string path, bool read, bool traverse)
    {
        IntPtr buffer = IntPtr.Zero;
        IntPtr namePointer = IntPtr.Zero;
        bool retained = false;
        try
        {
            buffer = Marshal.StringToHGlobalUni(name);
            namePointer = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
            var unicode = new UnicodeString(checked((ushort)(name.Length * 2)),
                checked((ushort)((name.Length + 1) * 2)), buffer);
            Marshal.StructureToPtr(unicode, namePointer, false);
            parent.DangerousAddRef(ref retained);
            var attributes = new ObjectAttributes
            {
                Length = Marshal.SizeOf<ObjectAttributes>(),
                Root = parent.DangerousGetHandle(),
                Name = namePointer,
                Flags = 0x40,
            };

            // FILE_OPEN_REPARSE_POINT applies to this single relative component.
            // FILE_SYNCHRONOUS_IO_NONALERT requires SYNCHRONIZE. No open shares DELETE.
            const uint synchronize = 0x100000;
            const uint readAttributes = 0x80;
            uint access = synchronize | readAttributes | (read ? 1u : 0u) | (traverse ? 0x20u : 0u);
            int status = NtCreateFile(out SafeFileHandle handle, access, ref attributes, out _, IntPtr.Zero,
                0, read ? 1u : 3u, 1, 0x200020, IntPtr.Zero, 0);
            if (status >= 0) { return handle; }
            handle?.Dispose();
            throw OpenError(path, (int)RtlNtStatusToDosError(status));
        }
        finally
        {
            if (retained) { parent.DangerousRelease(); }
            Marshal.FreeHGlobal(namePointer);
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IOException OpenError(string path, int error)
        => error is 2 or 3
            ? new LocalUploadFileValidationException(path, LocalUploadFileValidationFailure.Missing)
            : new IOException("Cannot safely open the local upload source.", new Win32Exception(error));

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct FileId(ulong Volume, ulong Low, ulong High);

    private readonly record struct Entry(FileAttributes Attributes, FileId Id, uint Links, long Length, long LastWrite)
    {
        internal bool Matches(Entry actual, bool checkContents)
            => Id == actual.Id && Attributes == actual.Attributes &&
                (!checkContents || (Attributes & FileAttributes.Directory) != 0 ||
                    (Links == actual.Links && Length == actual.Length && LastWrite == actual.LastWrite));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        internal FileAttributes Attributes;
        internal System.Runtime.InteropServices.ComTypes.FILETIME Creation;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastAccess;
        internal System.Runtime.InteropServices.ComTypes.FILETIME Write;
        internal uint Volume;
        internal uint LengthHigh;
        internal uint LengthLow;
        internal uint Links;
        internal uint IndexHigh;
        internal uint IndexLow;
        internal readonly long LastWrite => ((long)Write.dwHighDateTime << 32) | (uint)Write.dwLowDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct UnicodeString(ushort Length, ushort Capacity, IntPtr Buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes
    {
        internal int Length;
        internal IntPtr Root;
        internal IntPtr Name;
        internal uint Flags;
        internal IntPtr SecurityDescriptor;
        internal IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock
    {
        internal IntPtr Status;
        internal UIntPtr Information;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share,
        IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetDriveTypeW(string path);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationByHandleW(SafeFileHandle handle, IntPtr volumeName,
        int volumeNameLength, out uint serial, out uint maximumComponentLength, out uint flags,
        StringBuilder fileSystemName, int fileSystemNameLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation info);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, out FileId id, uint size);

    [DllImport("ntdll.dll")]
    private static extern int NtCreateFile(out SafeFileHandle handle, uint access, ref ObjectAttributes attributes,
        out IoStatusBlock status, IntPtr size, uint fileAttributes, uint share, uint disposition,
        uint options, IntPtr ea, uint eaLength);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryDirectoryFile(SafeFileHandle directory, IntPtr eventHandle,
        IntPtr apcRoutine, IntPtr apcContext, out IoStatusBlock status, [Out] byte[] buffer,
        uint length, int informationClass, [MarshalAs(UnmanagedType.U1)] bool singleEntry,
        IntPtr name, [MarshalAs(UnmanagedType.U1)] bool restart);

    [DllImport("ntdll.dll")]
    private static extern uint RtlNtStatusToDosError(int status);
}

/// <summary>A local upload path contains a reparse point.</summary>
public sealed class LocalUploadReparseException : IOException
{
    internal LocalUploadReparseException() : base("The local upload source contains a reparse point.") { }
}
