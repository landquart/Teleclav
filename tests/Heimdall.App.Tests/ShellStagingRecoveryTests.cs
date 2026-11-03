/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;
using Heimdall.App.Views;

namespace Heimdall.App.Tests;

public sealed class ShellStagingRecoveryTests
{
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
}
