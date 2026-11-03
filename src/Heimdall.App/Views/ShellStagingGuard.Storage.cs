/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Heimdall.App.Views;

internal sealed partial class ShellStagingGuard
{
    internal const string OwnershipName = ".teleclav-owner";
    internal static string DefaultRootPath
    {
        get
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!Path.IsPathFullyQualified(profile)) { throw new IOException("No local application profile is available."); }
            return Path.Combine(profile, "Teleclav", "Staging");
        }
    }
    private ShellStagingJournal? _journal;
    private const uint ReadControl = 0x20000;
    private const uint Synchronize = 0x100000;

    private void InitializePersistent()
    {
        string root = Path.GetDirectoryName(_path) ?? throw new IOException("Missing staging root.");
        OpenTrustedRoot(root);
        _directory = CreateRelative(_ancestors[^1], Path.GetFileName(_path), ShellStagingDirectory.CreateSecurityDescriptor());
        RequirePrivateDirectory(_directory);
        _journal = ShellStagingJournal.Create(_directory, Identity(_ancestors[^1]), Identity(_directory),
            Guid.ParseExact(Path.GetFileName(_path), "N"));
    }

    // Reopening validates ownership and loads recorded identities, but never performs recovery or deletion.
    // F3.4 must acquire this exclusive journal lease before inspecting an abandoned operation.
    internal static ShellStagingGuard Reopen(string path) => new(path, reopen: true);

    private void RestorePersistent()
    {
        string root = Path.GetDirectoryName(_path) ?? throw new IOException("Missing staging root.");
        if (!Guid.TryParseExact(Path.GetFileName(_path), "N", out Guid operation))
            throw new IOException("Invalid staging operation name.");
        OpenTrustedRoot(root);
        _directory = OpenRelative(_ancestors[^1], Path.GetFileName(_path), false, false, true, deleteDirectory: true);
        RequirePrivateDirectory(_directory);
        _journal = ShellStagingJournal.Open(_directory, Identity(_ancestors[^1]), Identity(_directory), operation, _files);
    }

    private void OpenTrustedRoot(string path)
    {
        string full = Path.GetFullPath(path);
        string drive = Path.GetPathRoot(full) ?? throw new IOException("Missing staging volume.");
        // No UNC, device paths, relative paths, network profiles or redirected non-NTFS volumes.
        if (drive.Length != 3 || drive[1] != ':' || full.StartsWith(@"\\", StringComparison.Ordinal))
            throw new IOException("Staging requires a local drive.");
        if (GetDriveType(drive) != 3) { throw new IOException("Staging requires a fixed local volume."); }
        SafeFileHandle current = Open(drive, ReadAttributes | ReadControl | 0x20);
        _ancestors.Add(current);
        RequireDirectory(current);
        RequireNtfs(current);
        RequireTrustedAcl(current, false);
        string[] parts = full[drive.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) { throw new IOException("Staging requires a private application parent."); }
        for (int i = 0; i < parts.Length; i++)
        {
            bool privateDirectory = i >= parts.Length - 2;
            // Existing profile ancestors are opened only. Only the two application components may be created.
            current = OpenRelative(current, parts[i], privateDirectory, privateDirectory, true);
            _ancestors.Add(current);
            RequireDirectory(current);
            RequireTrustedAcl(current, privateDirectory);
        }
    }

    internal void EnsureAvailableSpace(long bytes)
    {
        if (!GetDiskFreeSpaceEx(_path, out ulong available, out _, out _))
            throw new IOException("Cannot determine available staging space.", new Win32Exception(Marshal.GetLastWin32Error()));
        if ((ulong)bytes > available || available - (ulong)bytes < 65536)
            throw new IOException("Insufficient space on the staging volume.");
    }

    [DllImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(string directory, out ulong available, out ulong total, out ulong free);

    private static void RequireNtfs(SafeFileHandle directory)
    {
        var fs = new System.Text.StringBuilder(32);
        if (!GetVolumeInformationByHandle(directory, null, 0, out _, out _, out uint flags, fs, fs.Capacity))
            throw new IOException("Cannot verify staging filesystem.", new Win32Exception(Marshal.GetLastWin32Error()));
        if (fs.ToString() != "NTFS" || (flags & 8) == 0)
            throw new IOException("Persistent staging requires NTFS with persistent ACLs.");
    }

    private static void RequirePrivateDirectory(SafeFileHandle directory)
    {
        RequireDirectory(directory);
        RequireTrustedAcl(directory, true);
    }

    private static void RequireTrustedAcl(SafeFileHandle handle, bool strict, bool file = false)
    {
        uint error = GetSecurityInfo(handle, 1, 5, out _, out _, out _, out _, out IntPtr descriptor);
        if (error != 0) { throw new IOException("Cannot verify staging access controls.", new Win32Exception((int)error)); }
        try
        {
            byte[] data = new byte[GetSecurityDescriptorLength(descriptor)];
            Marshal.Copy(descriptor, data, 0, data.Length);
            var actual = new RawSecurityDescriptor(data, 0);
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            SecurityIdentifier user = identity.User ?? throw new IOException("Missing Windows user.");
            var trusted = new HashSet<SecurityIdentifier>
            {
                user, new(WellKnownSidType.LocalSystemSid, null), new(WellKnownSidType.BuiltinAdministratorsSid, null),
                new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464") // TrustedInstaller
            };
            if (actual.Owner is null || !trusted.Contains(actual.Owner) || actual.DiscretionaryAcl is null)
                throw new IOException("Staging ancestor ownership is not trusted.");
            if (strict)
            {
                var expected = new RawSecurityDescriptor(file ? ShellStagingDirectory.CreateJournalSecurityDescriptor()
                    : ShellStagingDirectory.CreateSecurityDescriptor(), 0);
                if (!user.Equals(actual.Owner) || (actual.ControlFlags & ControlFlags.DiscretionaryAclProtected) == 0
                    || !AclEntries(actual.DiscretionaryAcl).SequenceEqual(AclEntries(expected.DiscretionaryAcl!)))
                    throw new IOException("Private staging access controls changed.");
            }
            else
            {
                foreach (GenericAce ace in actual.DiscretionaryAcl)
                {
                    if ((ace.AceFlags & AceFlags.InheritOnly) != 0) { continue; }
                    if (ace is not CommonAce common) { throw new IOException("Unsupported ancestor access control entry."); }
                    // Adding sibling directories is allowed. FILE_ADD_FILE aliases FILE_WRITE_DATA and can change reparse data.
                    const int dangerous = 0x10000 | 0x40000 | 0x80000 | 0x40 | 0x100 | 0x10 | 0x2
                        | unchecked((int)0x10000000) | unchecked((int)0x40000000);
                    if (common.AceQualifier == AceQualifier.AccessAllowed && (common.AccessMask & dangerous) != 0
                        && !trusted.Contains(common.SecurityIdentifier))
                        throw new IOException("An untrusted principal can change a staging ancestor.");
                }
            }
        }
        finally { LocalFree(descriptor); }
    }

    private static IEnumerable<string> AclEntries(RawAcl acl) => acl.Cast<GenericAce>().Select(ace =>
    {
        byte[] data = new byte[ace.BinaryLength];
        ace.GetBinaryForm(data, 0);
        return Convert.ToHexString(data);
    }).Order(StringComparer.Ordinal);

    private static void ValidateFileName(string name)
    {
        // Cache entries are indices. Reserve all other names for the ownership protocol.
        if (name.Length is < 1 or > 10 || name.Any(c => c is < '0' or > '9'))
            throw new IOException("Invalid staging file name.");
    }

    private static void ValidatePath(string path)
    {
        if (!Path.IsPathFullyQualified(path)) { throw new IOException("Staging path must be absolute."); }
        string drive = Path.GetPathRoot(path) ?? throw new IOException("Missing staging volume.");
        foreach (string component in path[drive.Length..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
            ValidateComponent(component);
    }

    private static void ValidateComponent(string name)
    {
        if (name.Length is < 1 or > 255 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.EndsWith(' ') || name.EndsWith('.') || IsDeviceName(name))
            throw new IOException("Invalid staging component.");
    }

    private static bool IsDeviceName(string name)
    {
        string stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                && stem[3] is >= '1' and <= '9' or '¹' or '²' or '³');
    }

    private static SafeFileHandle OpenRelative(SafeFileHandle parent, string name, bool create,
        bool writableDirectory, bool directory, bool journal = false, bool deleteDirectory = false)
    {
        ValidateComponent(name);
        IntPtr buffer = Marshal.StringToHGlobalUni(name);
        IntPtr unicodePointer = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
        byte[] descriptor = directory ? ShellStagingDirectory.CreateSecurityDescriptor()
            : ShellStagingDirectory.CreateJournalSecurityDescriptor();
        GCHandle security = GCHandle.Alloc(descriptor, GCHandleType.Pinned);
        bool added = false;
        try
        {
            // OBJECT_ATTRIBUTES stores a raw parent handle; explicitly retain its SafeHandle during the native call.
            parent.DangerousAddRef(ref added);
            var unicode = new UnicodeString
            {
                Length = checked((ushort)(name.Length * 2)),
                MaximumLength = checked((ushort)((name.Length + 1) * 2)),
                Buffer = buffer
            };
            Marshal.StructureToPtr(unicode, unicodePointer, false);
            var attributes = new ObjectAttributes
            {
                Length = Marshal.SizeOf<ObjectAttributes>(),
                RootDirectory = parent.DangerousGetHandle(),
                ObjectName = unicodePointer,
                Attributes = 0x40,
                SecurityDescriptor = create ? security.AddrOfPinnedObject() : IntPtr.Zero
            };
            uint access = ReadAttributes | ReadControl | Synchronize | (directory ? 0x20u : 0u);
            if (writableDirectory) { access |= 0x27; }
            if (journal) { access |= 3 | DeleteAccess; }
            if (deleteDirectory) { access |= DeleteAccess | 0x27; }
            uint options = 0x200000u | 0x20u | (directory ? 1u : 0x40u);
            uint disposition = journal ? (create ? 2u : 1u) : (create ? 3u : 1u);
            int status = NtCreateFile(out SafeFileHandle handle, access, ref attributes, out _, IntPtr.Zero,
                directory ? DirectoryAttribute : 0, journal ? 0u : ShareReadWrite, disposition, options, IntPtr.Zero, 0);
            if (status < 0)
            {
                handle?.Dispose();
                throw new IOException("Cannot open a staging component safely.", new Win32Exception((int)RtlNtStatusToDosError(status)));
            }
            return handle;
        }
        finally
        {
            if (added) { parent.DangerousRelease(); }
            security.Free();
            Marshal.FreeHGlobal(unicodePointer);
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "GetDriveTypeW", CharSet = CharSet.Unicode)]
    private static extern uint GetDriveType(string root);
    [DllImport("advapi32.dll")]
    private static extern uint GetSecurityInfo(SafeFileHandle handle, int type, uint information, out IntPtr owner,
        out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr descriptor);
    [DllImport("advapi32.dll")]
    private static extern uint GetSecurityDescriptorLength(IntPtr descriptor);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("kernel32.dll", EntryPoint = "GetVolumeInformationByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationByHandle(SafeFileHandle handle, System.Text.StringBuilder? volumeName,
        int volumeNameSize, out uint serial, out uint maximumComponentLength, out uint flags,
        System.Text.StringBuilder fileSystemName, int fileSystemNameSize);
}
