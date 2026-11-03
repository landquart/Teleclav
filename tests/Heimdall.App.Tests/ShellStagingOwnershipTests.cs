/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;
using System.Security.AccessControl;
using Heimdall.App.Views;

namespace Heimdall.App.Tests;

public sealed class ShellStagingOwnershipTests
{
    [Fact]
    public async Task FailedDownload_AfterPartialCreation_RemainsRecoverable()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        var cache = new ShellDownloadCache(2, async (_, path, ct) =>
        {
            await path.WriteAsync("x"u8.ToArray(), ct);
            throw new IOException("Interrupted after partial creation.");
        });
        string directory = cache.DirectoryPath;
        try
        {
            await Assert.ThrowsAsync<IOException>(() => cache.OpenAsync(0, 2, CancellationToken.None));
            cache.Dispose();
            Assert.Null(cache.CleanupFailure);
            Assert.False(Directory.Exists(directory));
        }
        finally
        {
            cache.Dispose();
        }
    }

    [Fact]
    public void Cache_EstablishesPersistentOwnershipBeforeDownloading()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var cache = new ShellDownloadCache(2, (_, path, ct) => path.WriteAsync(System.Text.Encoding.UTF8.GetBytes("ok"), ct).AsTask());
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Teleclav", "Staging"), Path.GetDirectoryName(cache.DirectoryPath));
        Assert.True(File.Exists(Path.Combine(cache.DirectoryPath, ".teleclav-owner")));
    }
}

// Each fixture uses its own private application root so hostile ACL/reparse tests cannot affect a live cache.
public sealed class ShellStagingOwnershipRecoveryTests
{
    [Fact]
    public async Task Reopen_RestoresRecordedFileIdentityAndCleansOnlyThatOperation()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new OwnershipArea();
        string path;
        using (var guard = ShellStagingGuard.CreateAt(area.Root))
        {
            path = guard.DirectoryPath;
            await guard.DownloadAsync("0", 2, (stream, ct) => stream.WriteAsync("ok"u8.ToArray(), ct).AsTask(), CancellationToken.None);
        }
        using var reopened = ShellStagingGuard.Reopen(path);
        using (var file = File.OpenRead(Path.Combine(path, "0"))) { reopened.Verify("0", file.SafeFileHandle); }
        reopened.Delete();
        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public void ActiveOperation_HasExclusiveOwnershipLease()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new OwnershipArea();
        using var guard = ShellStagingGuard.CreateAt(area.Root);
        Assert.Throws<IOException>(() => ShellStagingGuard.Reopen(guard.DirectoryPath));
        guard.Delete();
    }

    [Fact]
    public void ParallelOperations_SharePersistentRootWithDistinctIds()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new OwnershipArea();
        var guards = new ShellStagingGuard[8];
        try
        {
            Parallel.For(0, guards.Length, i => guards[i] = ShellStagingGuard.CreateAt(area.Root));
            Assert.Equal(guards.Length, guards.Select(g => g.DirectoryPath).Distinct().Count());
            foreach (var guard in guards) { guard.Delete(); }
            using var again = ShellStagingGuard.CreateAt(area.Root);
            again.Delete();
        }
        finally { foreach (var guard in guards) { guard?.Dispose(); } }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(40)]
    public void CorruptedMetadata_IsRefusedWithoutRemovingData(int truncateAt)
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new OwnershipArea();
        string path;
        using (var guard = ShellStagingGuard.CreateAt(area.Root)) { path = guard.DirectoryPath; }
        File.WriteAllText(Path.Combine(path, "foreign"), "keep");
        using (var file = new FileStream(Path.Combine(path, ShellStagingGuard.OwnershipName), FileMode.Open, FileAccess.Write))
        {
            file.SetLength(truncateAt);
        }
        Assert.Throws<IOException>(() => ShellStagingGuard.Reopen(path));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(path, "foreign")));
    }

    [Fact]
    public void CopiedMetadata_CannotClaimReplacementDirectory()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new OwnershipArea();
        string path;
        using (var guard = ShellStagingGuard.CreateAt(area.Root)) { path = guard.DirectoryPath; }
        string moved = path + "-old";
        Directory.Move(path, moved);
        // A second legitimately ACL-protected operation still has a different file ID and random operation ID.
        using (var replacement = ShellStagingGuard.CreateAt(area.Root))
        {
            string replacementPath = replacement.DirectoryPath;
            replacement.Dispose();
            File.Copy(Path.Combine(moved, ShellStagingGuard.OwnershipName),
                Path.Combine(replacementPath, ShellStagingGuard.OwnershipName), overwrite: true);
            Directory.Move(replacementPath, path);
        }
        File.WriteAllText(Path.Combine(path, "foreign"), "keep");
        Assert.Throws<IOException>(() => ShellStagingGuard.Reopen(path));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(path, "foreign")));
    }

    [Fact]
    public void RecreatedRoot_CannotAdoptMovedOperation()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new OwnershipArea();
        string path;
        using (var guard = ShellStagingGuard.CreateAt(area.Root)) { path = guard.DirectoryPath; }
        string oldRoot = area.Root + "-old";
        Directory.Move(area.Root, oldRoot);
        using (var guard = ShellStagingGuard.CreateAt(area.Root)) { guard.Delete(); }
        Directory.Move(Path.Combine(oldRoot, Path.GetFileName(path)), path);
        File.WriteAllText(Path.Combine(path, "foreign"), "keep");
        Assert.Throws<IOException>(() => ShellStagingGuard.Reopen(path));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(path, "foreign")));
    }

    [Fact]
    public void MetadataBitFlip_IsRejected()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new OwnershipArea();
        string path;
        using (var guard = ShellStagingGuard.CreateAt(area.Root)) { path = guard.DirectoryPath; }
        string metadata = Path.Combine(path, ShellStagingGuard.OwnershipName);
        byte[] bytes = File.ReadAllBytes(metadata);
        bytes[^1] ^= 1;
        File.WriteAllBytes(metadata, bytes);
        Assert.Throws<IOException>(() => ShellStagingGuard.Reopen(path));
    }

    [Fact]
    public void MetadataWithPermissiveAcl_IsRejected()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new OwnershipArea();
        string path;
        using (var guard = ShellStagingGuard.CreateAt(area.Root)) { path = guard.DirectoryPath; }
        var metadata = new FileInfo(Path.Combine(path, ShellStagingGuard.OwnershipName));
        var security = metadata.GetAccessControl();
        var everyone = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null);
        security.AddAccessRule(new FileSystemAccessRule(everyone, FileSystemRights.Read, AccessControlType.Allow));
        metadata.SetAccessControl(security);
        Assert.Throws<IOException>(() => ShellStagingGuard.Reopen(path));
    }

    [Fact]
    public void RootWithUnexpectedAcl_IsRejectedAndNotRepaired()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new OwnershipArea();
        using (var guard = ShellStagingGuard.CreateAt(area.Root)) { guard.Delete(); }
        var info = new DirectoryInfo(area.Root);
        var security = info.GetAccessControl();
        var everyone = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null);
        security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(everyone,
            System.Security.AccessControl.FileSystemRights.Read, System.Security.AccessControl.AccessControlType.Allow));
        info.SetAccessControl(security);
        Assert.Throws<IOException>(() => ShellStagingGuard.CreateAt(area.Root));
        Assert.Contains(info.GetAccessControl().GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier))
            .Cast<System.Security.AccessControl.FileSystemAccessRule>(), rule => rule.IdentityReference.Equals(everyone));
    }

    [Fact]
    public void RootReparsePoint_IsRejectedWithoutTouchingTarget()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new OwnershipArea();
        using (var guard = ShellStagingGuard.CreateAt(area.Root)) { guard.Delete(); }
        string target = Path.Combine(area.Parent, "target");
        Directory.CreateDirectory(target);
        string sentinel = Path.Combine(target, "keep");
        File.WriteAllText(sentinel, "keep");
        Directory.Delete(area.Root);
        // Junctions require neither elevation nor Developer Mode on Windows 11.
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"), $"/c mklink /J \"{area.Root}\" \"{target}\"")
        { UseShellExecute = false, CreateNoWindow = true });
        Assert.NotNull(process);
        Assert.True(process.WaitForExit(10000));
        Assert.Equal(0, process.ExitCode);
        try
        {
            Assert.Throws<IOException>(() => ShellStagingGuard.CreateAt(area.Root));
            Assert.Equal("keep", File.ReadAllText(sentinel));
        }
        finally { Directory.Delete(area.Root); }
    }

    [Fact]
    public async Task Cleanup_DoesNotTouchAnotherOperation()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new OwnershipArea();
        using var first = ShellStagingGuard.CreateAt(area.Root);
        using var second = ShellStagingGuard.CreateAt(area.Root);
        string sentinel = Path.Combine(second.DirectoryPath, "0");
        await second.DownloadAsync("0", 4, (stream, ct) => stream.WriteAsync("keep"u8.ToArray(), ct).AsTask(), CancellationToken.None);
        first.Delete();
        Assert.Equal("keep", File.ReadAllText(sentinel));
        second.Delete();
    }

    [Theory]
    [InlineData(FileSystemRights.Modify)]
    [InlineData(FileSystemRights.CreateFiles)]
    public void WritableAncestor_IsRejectedBeforeTraversingFurther(FileSystemRights rights)
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new OwnershipArea();
        using (var guard = ShellStagingGuard.CreateAt(area.Root)) { guard.Delete(); }
        var parent = new DirectoryInfo(area.Parent);
        var security = parent.GetAccessControl();
        var everyone = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null);
        security.AddAccessRule(new FileSystemAccessRule(everyone, rights, AccessControlType.Allow));
        parent.SetAccessControl(security);
        string nested = Path.Combine(area.Root, "Nested", "Staging");
        Assert.Throws<IOException>(() => ShellStagingGuard.CreateAt(nested));
        Assert.False(Directory.Exists(Path.Combine(area.Root, "Nested")));
    }

    [Theory]
    [InlineData("Staging.")]
    [InlineData("Staging ")]
    [InlineData("NUL")]
    public void AmbiguousWin32Component_IsRejected(string component)
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new OwnershipArea();
        Assert.Throws<IOException>(() => ShellStagingGuard.CreateAt(Path.Combine(area.Parent, component)));
    }

    [Fact]
    public void UnknownPartialFile_IsPreservedAfterReopen()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new OwnershipArea();
        string path;
        using (var guard = ShellStagingGuard.CreateAt(area.Root)) { path = guard.DirectoryPath; }
        string partial = Path.Combine(path, "0.unknown.part");
        File.WriteAllText(partial, "keep");
        using var reopened = ShellStagingGuard.Reopen(path);
        Assert.Throws<IOException>(reopened.Delete);
        Assert.Equal("keep", File.ReadAllText(partial));
    }

    private sealed class OwnershipArea : IDisposable
    {
        internal string Parent { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Teleclav-ownership-tests-" + Guid.NewGuid().ToString("N"));
        internal string Root => Path.Combine(Parent, "Staging");
        public void Dispose()
        {
            if (Directory.Exists(Parent)) { Directory.Delete(Parent, recursive: true); }
        }
    }
}
