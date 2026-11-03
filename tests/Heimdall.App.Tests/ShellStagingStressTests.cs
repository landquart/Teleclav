/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.Diagnostics;
using System.IO;
using Heimdall.App.Views;

namespace Heimdall.App.Tests;

[CollectionDefinition("Staging stress", DisableParallelization = true)]
public sealed class ShellStagingStressCollection { }

[Collection("Staging stress")]
public sealed class ShellStagingStressTests
{
    [Fact]
    public async Task RepeatedCaches_DoNotAccumulateDirectoriesOrHandles()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        for (int i = 0; i < 5; i++) { await OneTransfer(); }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using Process process = Process.GetCurrentProcess();
        int handlesBefore = process.HandleCount;
        long memoryBefore = GC.GetTotalMemory(true);
        for (int i = 0; i < 200; i++) { await OneTransfer(); }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        process.Refresh();
        int handlesAfter = process.HandleCount;
        long memoryAfter = GC.GetTotalMemory(true);
        Assert.True(handlesAfter <= handlesBefore + 64, $"Handles: {handlesBefore} -> {handlesAfter}");
        Assert.True(memoryAfter <= memoryBefore + 16 * 1024 * 1024, $"Managed memory: {memoryBefore} -> {memoryAfter}");
    }

    private static async Task OneTransfer()
    {
        var cache = new ShellDownloadCache(2, (_, path, ct) => path.WriteAsync(System.Text.Encoding.UTF8.GetBytes("ok"), ct).AsTask());
        string path = cache.DirectoryPath;
        using (Stream reader = await cache.OpenAsync(0, 2, CancellationToken.None)) { Assert.Equal('o', reader.ReadByte()); }
        cache.Dispose();
        Assert.False(Directory.Exists(path));
    }
}
