/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Heimdall.App.Views;

namespace Heimdall.App.Tests;

public sealed class ShellStagingDirectoryTests
{
    [Fact]
    public void StagedExecutable_CannotBeStartedByCurrentUser()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        string path = ShellStagingDirectory.Create();
        System.Diagnostics.Process? process = null;
        try
        {
            string executable = Path.Combine(path, "test.exe");
            File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), executable);
            var start = new System.Diagnostics.ProcessStartInfo(executable, "/c exit") { UseShellExecute = false };
            Exception? error = Record.Exception(() => { process = System.Diagnostics.Process.Start(start); });
            Assert.Equal(5, Assert.IsType<System.ComponentModel.Win32Exception>(error).NativeErrorCode);
        }
        finally
        {
            if (process is not null)
            {
                if (!process.HasExited) { process.Kill(); process.WaitForExit(); }
                process.Dispose();
            }
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void StagingDirectory_HasProtectedAclAndChildrenInheritOnlyAllowedIdentities()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        string path = ShellStagingDirectory.Create();
        try
        {
            var user = WindowsIdentity.GetCurrent().User!;
            var allowed = new HashSet<SecurityIdentifier>
            {
                user,
                new(WellKnownSidType.BuiltinAdministratorsSid, null),
                new(WellKnownSidType.LocalSystemSid, null),
            };
            DirectorySecurity security = new DirectoryInfo(path).GetAccessControl();
            Assert.True(security.AreAccessRulesProtected);
            AssertOnlyAllowed(security, allowed);
            string filePath = Path.Combine(path, "test");
            File.WriteAllText(filePath, "test");
            FileSecurity fileSecurity = new FileInfo(filePath).GetAccessControl();
            AssertOnlyAllowed(fileSecurity, allowed);
            Assert.Contains(fileSecurity.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>(),
                rule => rule.IdentityReference.Equals(user) && rule.AccessControlType == AccessControlType.Deny
                    && (rule.FileSystemRights & FileSystemRights.ExecuteFile) != 0);
        }
        finally { Directory.Delete(path, recursive: true); }
    }

    private static void AssertOnlyAllowed(FileSystemSecurity security, HashSet<SecurityIdentifier> allowed)
    {
        HashSet<SecurityIdentifier> found = [];
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            var sid = (SecurityIdentifier)rule.IdentityReference;
            Assert.Contains(sid, allowed);
            if (rule.AccessControlType == AccessControlType.Allow) { found.Add(sid); }
        }
        Assert.True(allowed.SetEquals(found));
    }
}
