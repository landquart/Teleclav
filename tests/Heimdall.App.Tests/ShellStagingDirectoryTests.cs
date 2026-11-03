/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Heimdall.App.Views;

namespace Heimdall.App.Tests;

public sealed class ShellStagingDirectoryTests
{
    [Fact]
    public async Task CacheCreatedDirectory_HasProtectedAclAndInheritedExecuteDenial()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var cache = new ShellDownloadCache(2, (_, path, ct) => File.WriteAllTextAsync(path, "ok", ct));
        using Stream reader = await cache.OpenAsync(0, 2, CancellationToken.None);
        var user = WindowsIdentity.GetCurrent().User!;
        var allowed = new HashSet<SecurityIdentifier>
        {
            user, new(WellKnownSidType.BuiltinAdministratorsSid, null), new(WellKnownSidType.LocalSystemSid, null)
        };
        DirectorySecurity security = new DirectoryInfo(cache.DirectoryPath).GetAccessControl();
        Assert.True(security.AreAccessRulesProtected);
        AssertOnlyAllowed(security, allowed);
        FileSecurity child = new FileInfo(Path.Combine(cache.DirectoryPath, "0")).GetAccessControl();
        Assert.Contains(child.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>(),
            rule => rule.IdentityReference.Equals(user) && rule.AccessControlType == AccessControlType.Deny
                && (rule.FileSystemRights & FileSystemRights.ExecuteFile) != 0);
    }

    [Fact]
    public void Guard_PreventsRenamingAnAncestorUntilCleanup()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        string parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "teleclav-guard-test-" + Guid.NewGuid().ToString("N"));
        string moved = parent + "-moved";
        try
        {
            using var guard = ShellStagingGuard.CreateAt(Path.Combine(parent, "Staging"));
            Assert.ThrowsAny<IOException>(() => Directory.Move(parent, moved));
            guard.Delete();
        }
        finally
        {
            if (Directory.Exists(parent)) { Directory.Delete(parent, recursive: true); }
            if (Directory.Exists(moved)) { Directory.Delete(moved, recursive: true); }
        }
    }

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
