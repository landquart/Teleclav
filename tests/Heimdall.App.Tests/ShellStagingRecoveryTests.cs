/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;
using Heimdall.App.Views;

namespace Heimdall.App.Tests;

public sealed class ShellStagingRecoveryTests
{
    [Fact]
    public async Task CancellationAfterOneDeletion_CanResumeFromTheJournal()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        using var cancellation = new CancellationTokenSource();
        string path;
        using (var guard = ShellStagingGuard.CreateAt(area.Root))
        {
            path = guard.DirectoryPath;
            await ShellStagingDeletionTests.Write(guard, "0");
            await ShellStagingDeletionTests.Write(guard, "1");
            guard.Fault = point => { if (point == ShellStagingPoint.AfterFileDelete) { cancellation.Cancel(); } };
            Assert.Throws<OperationCanceledException>(() => guard.Delete(cancellation.Token));
            Assert.Single(Directory.GetFiles(path), file => Path.GetFileName(file) != ShellStagingGuard.OwnershipName);
        }
        ShellStagingRecovery.Recover(area.Root);
        Assert.False(Directory.Exists(path));
        area.AssertSentinel();
    }

    [Fact]
    public async Task Discovery_CancellationLeavesOwnershipForTheNextPass()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        string path;
        using (var guard = ShellStagingGuard.CreateAt(area.Root))
        {
            path = guard.DirectoryPath;
            await ShellStagingDeletionTests.Write(guard);
        }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ShellStagingRecovery.Recover(area.Root, cancellation.Token));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(path, "0")));
        ShellStagingRecovery.Recover(area.Root);
        Assert.False(Directory.Exists(path));
        area.AssertSentinel();
    }

    [Fact]
    public async Task Discovery_EntryBudgetReportsIncompleteAndTheNextPassContinues()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        for (int i = 0; i < 4; i++)
        {
            using var guard = ShellStagingGuard.CreateAt(area.Root);
            await ShellStagingDeletionTests.Write(guard);
        }
        var result = ShellStagingRecovery.Recover(area.Root, maximumEntries: 2);
        Assert.Equal(2, result.Inspected);
        Assert.Equal(2, result.Removed);
        Assert.Equal(0, result.Retained);
        Assert.True(result.Limited);
        Assert.Equal(2, Directory.GetDirectories(area.Root).Length);
        result = ShellStagingRecovery.Recover(area.Root, maximumEntries: 2);
        Assert.Equal(2, result.Removed);
        Assert.False(result.Limited);
        Assert.Empty(Directory.GetDirectories(area.Root));
        area.AssertSentinel();
    }

    [Fact]
    public async Task BackgroundRecovery_RetriesAfterAReaderCloses()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        string path;
        using (var guard = ShellStagingGuard.CreateAt(area.Root))
        {
            path = guard.DirectoryPath;
            await ShellStagingDeletionTests.Write(guard);
        }
        using var reader = new FileStream(Path.Combine(path, "0"), FileMode.Open, FileAccess.Read, FileShare.Read);
        int passes = 0;
        await ShellStagingRecovery.RunAsync(area.Root, CancellationToken.None, TimeSpan.Zero, result =>
        {
            passes++;
            if (passes == 1) { Assert.Equal(1, result.Retained); reader.Dispose(); }
            else { Assert.Equal(1, result.Removed); }
        });
        Assert.Equal(2, passes);
        Assert.False(Directory.Exists(path));
        area.AssertSentinel();
    }

    [Fact]
    public async Task BackgroundRecovery_StopsAfterThreeRefusalsWithoutDeletingALiveOwner()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        using var guard = ShellStagingGuard.CreateAt(area.Root);
        await ShellStagingDeletionTests.Write(guard);
        int passes = 0;
        await ShellStagingRecovery.RunAsync(area.Root, CancellationToken.None, TimeSpan.Zero, result =>
        {
            passes++;
            Assert.Equal(1, result.Retained);
            Assert.Equal(0, result.Removed);
        });
        Assert.Equal(3, passes);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(guard.DirectoryPath, "0")));
        guard.Delete();
        area.AssertSentinel();
    }

    [Fact]
    public void Discovery_RefusesARootSymlinkWithoutChangingItsTarget()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        using (var guard = ShellStagingGuard.CreateAt(area.Root)) { guard.Delete(); }
        Directory.Delete(area.Root);
        Directory.CreateSymbolicLink(area.Root, area.Controls);
        Assert.Throws<IOException>(() => ShellStagingRecovery.Recover(area.Root));
        area.AssertSentinel();
    }

    [Fact]
    public async Task Discovery_RetainsACorruptJournalAndStillCleansOtherOperations()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        string damaged;
        using (var guard = ShellStagingGuard.CreateAt(area.Root))
        {
            damaged = guard.DirectoryPath;
            await ShellStagingDeletionTests.Write(guard);
        }
        string owner = Path.Combine(damaged, ShellStagingGuard.OwnershipName);
        byte[] bytes = File.ReadAllBytes(owner);
        bytes[^1] ^= 0x80;
        File.WriteAllBytes(owner, bytes);
        string valid;
        using (var guard = ShellStagingGuard.CreateAt(area.Root))
        {
            valid = guard.DirectoryPath;
            await ShellStagingDeletionTests.Write(guard);
        }
        ShellStagingRecovery.Recover(area.Root);
        Assert.Equal(bytes, File.ReadAllBytes(owner));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(damaged, "0")));
        Assert.False(Directory.Exists(valid));
        area.AssertSentinel();
    }

    [Fact]
    public async Task Discovery_LockedFileRemainsRecoverableAfterUnlock()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        string path;
        using (var guard = ShellStagingGuard.CreateAt(area.Root))
        {
            path = guard.DirectoryPath;
            await ShellStagingDeletionTests.Write(guard);
        }
        using (var reader = new FileStream(Path.Combine(path, "0"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            ShellStagingRecovery.Recover(area.Root);
            Assert.True(File.Exists(Path.Combine(path, ShellStagingGuard.OwnershipName)));
            Assert.Equal((byte)'h', reader.ReadByte());
        }
        ShellStagingRecovery.Recover(area.Root);
        Assert.False(Directory.Exists(path));
        area.AssertSentinel();
    }

    [Fact]
    public void Discovery_NeverFollowsAnOperationSymlink()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        using (var guard = ShellStagingGuard.CreateAt(area.Root)) { guard.Delete(); }
        string link = Path.Combine(area.Root, Guid.NewGuid().ToString("N"));
        Directory.CreateSymbolicLink(link, area.Controls);
        ShellStagingRecovery.Recover(area.Root);
        Assert.True(Directory.Exists(link));
        area.AssertSentinel();
    }

    [Fact]
    public async Task Discovery_CleansAnAbandonedPublishedOperation()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        string path;
        using (var guard = ShellStagingGuard.CreateAt(area.Root))
        {
            path = guard.DirectoryPath;
            await ShellStagingDeletionTests.Write(guard);
        }
        ShellStagingRecovery.Recover(area.Root);
        Assert.False(Directory.Exists(path));
        area.AssertSentinel();
    }

    [Fact]
    public async Task Discovery_CleansTheCreateBeforeIdentityCrashWindow()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        string path;
        using (var guard = ShellStagingGuard.CreateAt(area.Root))
        {
            path = guard.DirectoryPath;
            guard.Fault = point => { if (point == ShellStagingPoint.AfterCreate) { throw new IOException("Injected interruption."); } };
            await Assert.ThrowsAsync<IOException>(() => ShellStagingDeletionTests.Write(guard));
            Assert.Single(Directory.GetFiles(path, "*.part"));
        }
        ShellStagingRecovery.Recover(area.Root);
        Assert.False(Directory.Exists(path));
        area.AssertSentinel();
    }

    [Fact]
    public async Task Discovery_LeavesLiveOwnersThenRecoversAfterLeaseRelease()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        string path;
        using (var guard = ShellStagingGuard.CreateAt(area.Root))
        {
            path = guard.DirectoryPath;
            await ShellStagingDeletionTests.Write(guard);
            ShellStagingRecovery.Recover(area.Root);
            Assert.Equal("hello", File.ReadAllText(Path.Combine(path, "0")));
        }
        ShellStagingRecovery.Recover(area.Root);
        Assert.False(Directory.Exists(path));
        area.AssertSentinel();
    }

    [Fact]
    public void Discovery_DoesNotCreateAnUnusedStagingRoot()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        ShellStagingRecovery.Recover(area.Root);
        Assert.False(Directory.Exists(area.Root));
        area.AssertSentinel();
    }

    [Fact]
    public async Task Discovery_LeavesUnknownAndUnjournalledObjectsWhileCleaningOwnedFiles()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        string path;
        using (var guard = ShellStagingGuard.CreateAt(area.Root))
        {
            path = guard.DirectoryPath;
            await ShellStagingDeletionTests.Write(guard);
        }
        string orphan = Directory.CreateDirectory(Path.Combine(area.Root, Guid.NewGuid().ToString("N"))).FullName;
        string foreign = Path.Combine(orphan, "0");
        File.WriteAllText(foreign, "foreign");
        string empty = Directory.CreateDirectory(Path.Combine(area.Root, Guid.NewGuid().ToString("N"))).FullName;
        string unknown = Path.Combine(area.Root, "unrecognized.txt");
        File.WriteAllText(unknown, "unknown");
        ShellStagingRecovery.Recover(area.Root);
        Assert.False(Directory.Exists(path));
        Assert.Equal("foreign", File.ReadAllText(foreign));
        Assert.Equal("unknown", File.ReadAllText(unknown));
        Assert.True(Directory.Exists(empty));
        area.AssertSentinel();
    }
}
