/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Heimdall.App.Views;

/// <summary>Creates a fresh private staging directory without a permissive creation window.</summary>
internal static class ShellStagingDirectory
{
    internal static string Create()
    {
        if (!OperatingSystem.IsWindows()) { return Directory.CreateTempSubdirectory("teleclav-drag-").FullName; }
        return CreateWindows();
    }

    [SupportedOSPlatform("windows")]
    private static string CreateWindows()
    {
        string parent = Path.GetTempPath();
        var volume = new StringBuilder(32768);
        if (!GetVolumePathName(parent, volume, volume.Capacity)
            || !GetVolumeInformation(volume.ToString(), null, 0, out _, out _, out uint flags, null, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot verify staging volume permissions.");
        if ((flags & 0x8) == 0) { throw new IOException("The temporary volume does not support persistent access controls."); }

        SecurityIdentifier user = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Cannot determine the current Windows user.");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        const InheritanceFlags inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        foreach (SecurityIdentifier sid in new[] { user, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                     new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, inheritance,
                PropagationFlags.None, AccessControlType.Allow));
        // Files remain readable and writable but are not executable by the current user.
        // This does not constrain administrators or an owner who changes the ACL.
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.ExecuteFile,
            InheritanceFlags.ObjectInherit, PropagationFlags.InheritOnly, AccessControlType.Deny));

        byte[] descriptor = security.GetSecurityDescriptorBinaryForm();
        GCHandle pinned = GCHandle.Alloc(descriptor, GCHandleType.Pinned);
        try
        {
            var attributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = pinned.AddrOfPinnedObject(),
            };
            string path = Path.Combine(parent, "teleclav-drag-" + Guid.NewGuid().ToString("N"));
            if (!CreateDirectory(path, ref attributes))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot create a private staging directory.");
            return path;
        }
        finally { pinned.Free(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        public int InheritHandle;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectory(string path, ref SecurityAttributes securityAttributes);

    [DllImport("kernel32.dll", EntryPoint = "GetVolumePathNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathName(string fileName, StringBuilder volumePathName, int bufferLength);

    [DllImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformation(string rootPathName, StringBuilder? volumeNameBuffer, int volumeNameSize,
        out uint serialNumber, out uint maximumComponentLength, out uint fileSystemFlags, StringBuilder? fileSystemNameBuffer,
        int fileSystemNameSize);
}
