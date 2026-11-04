/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

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
        using var operation = ShellStagingGuard.Create();
        return operation.DirectoryPath;
    }

    [SupportedOSPlatform("windows")]
    internal static byte[] CreateSecurityDescriptor()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier user = identity.User
            ?? throw new InvalidOperationException("Cannot determine the current Windows user.");
        var security = new DirectorySecurity();
        security.SetOwner(user);
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

        return security.GetSecurityDescriptorBinaryForm();
    }

    [SupportedOSPlatform("windows")]
    internal static byte[] CreateJournalSecurityDescriptor()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier user = identity.User
            ?? throw new InvalidOperationException("Cannot determine the current Windows user.");
        var security = new FileSecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(true, false);
        foreach (SecurityIdentifier sid in new[] { user, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                     new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.ExecuteFile, AccessControlType.Deny));
        return security.GetSecurityDescriptorBinaryForm();
    }

}
