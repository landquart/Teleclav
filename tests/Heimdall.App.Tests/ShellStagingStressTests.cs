/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.Diagnostics;
using System.IO;
using Heimdall.App.Views;
using Xunit.Abstractions;

namespace Heimdall.App.Tests;

[CollectionDefinition("Staging stress", DisableParallelization = true)]
public sealed class ShellStagingStressCollection { }

[Collection("Staging stress")]
public sealed class ShellStagingStressTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    public async Task RepeatedCaches_DoNotAccumulateDirectoriesOrHandles(int count)
    {
        if (!OperatingSystem.IsWindows()) { return; }
        for (int i = 0; i < 5; i++) { await OneTransfer(); }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using Process process = Process.GetCurrentProcess();
        int handlesBefore = process.HandleCount;
        long memoryBefore = GC.GetTotalMemory(true);
        process.Refresh();
        long privateBefore = process.PrivateMemorySize64;
        int peakHandles = handlesBefore;
        long peakPrivate = privateBefore;
        TimeSpan cleanupTotal = TimeSpan.Zero;
        TimeSpan cleanupMax = TimeSpan.Zero;
        var paths = new List<string>(count);
        var elapsed = Stopwatch.StartNew();
        for (int i = 0; i < count; i++)
        {
            var sample = await OneTransfer(process);
            paths.Add(sample.Path);
            peakHandles = Math.Max(peakHandles, sample.Handles);
            peakPrivate = Math.Max(peakPrivate, sample.PrivateBytes);
            cleanupTotal += sample.Cleanup;
            if (sample.Cleanup > cleanupMax) { cleanupMax = sample.Cleanup; }
        }
        elapsed.Stop();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        process.Refresh();
        int handlesAfter = process.HandleCount;
        long privateAfter = process.PrivateMemorySize64;
        long memoryAfter = GC.GetTotalMemory(true);
        int remaining = paths.Count(Directory.Exists);
        output.WriteLine(FormattableString.Invariant(
            $"STAGING sequence={count} elapsed_ms={elapsed.Elapsed.TotalMilliseconds:F2} cleanup_total_ms={cleanupTotal.TotalMilliseconds:F2} cleanup_max_ms={cleanupMax.TotalMilliseconds:F2} handles={handlesBefore}/{peakHandles}/{handlesAfter} managed_bytes={memoryBefore}/{memoryAfter} private_bytes={privateBefore}/{peakPrivate}/{privateAfter} remaining={remaining}"));
        Assert.Equal(0, remaining);
        Assert.True(handlesAfter <= handlesBefore + 64, $"Handles: {handlesBefore} -> {handlesAfter}");
        Assert.True(memoryAfter <= memoryBefore + 16 * 1024 * 1024, $"Managed memory: {memoryBefore} -> {memoryAfter}");
    }

    [Fact]
    public async Task ParallelLargeCacheReaders_RetainDataUntilTheLastRelease()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        const int chunkBytes = 1024 * 1024;
        const long size = 64L * chunkBytes;
        byte[] chunk = new byte[chunkBytes];
        chunk[0] = 42;
        var elapsed = Stopwatch.StartNew();
        using var large = new ShellDownloadCache(size, async (_, stream, ct) =>
        {
            for (int i = 0; i < size / chunkBytes; i++) { await stream.WriteAsync(chunk, ct); }
        });
        using (Stream first = await large.OpenAsync(0, size, CancellationToken.None))
        using (Stream second = await large.OpenAsync(0, size, CancellationToken.None))
        {
            large.Dispose();
            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => OneTransfer()));
            Assert.True(Directory.Exists(large.DirectoryPath));
            Assert.Equal(size, first.Length);
            Assert.Equal(42, first.ReadByte());
            second.Position = size - chunkBytes;
            Assert.Equal(42, second.ReadByte());
        }
        Assert.False(Directory.Exists(large.DirectoryPath));
        output.WriteLine(FormattableString.Invariant(
            $"STAGING large_bytes={size} parallel_small_operations=8 readers=2 elapsed_ms={elapsed.Elapsed.TotalMilliseconds:F2} remaining=0"));
    }

    [Fact]
    public async Task Recovery_ManyOwnedEntriesReportsMeasuredCleanup()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        const int count = 256;
        using (var operation = ShellStagingGuard.CreateAt(area.Root))
        {
            for (int i = 0; i < count; i++)
                await ShellStagingDeletionTests.Write(operation, i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(count + 1, Directory.GetFiles(operation.DirectoryPath).Length);
        }
        var elapsed = Stopwatch.StartNew();
        var result = ShellStagingRecovery.Recover(area.Root);
        elapsed.Stop();
        Assert.Equal(1, result.Removed);
        Assert.Equal(0, result.Retained);
        Assert.Empty(Directory.GetFileSystemEntries(area.Root));
        area.AssertSentinel();
        output.WriteLine(FormattableString.Invariant(
            $"STAGING recovery_entries={count} elapsed_ms={elapsed.Elapsed.TotalMilliseconds:F2} remaining=0"));
    }

    private static async Task<(string Path, int Handles, long PrivateBytes, TimeSpan Cleanup)> OneTransfer(Process? process = null)
    {
        using var cache = new ShellDownloadCache(2, (_, path, ct) => path.WriteAsync("ok"u8.ToArray(), ct).AsTask());
        string path = cache.DirectoryPath;
        int handles;
        long privateBytes;
        using (Stream reader = await cache.OpenAsync(0, 2, CancellationToken.None))
        {
            Assert.Equal('o', reader.ReadByte());
            process?.Refresh();
            handles = process?.HandleCount ?? 0;
            privateBytes = process?.PrivateMemorySize64 ?? 0;
        }
        var elapsed = Stopwatch.StartNew();
        cache.Dispose();
        elapsed.Stop();
        Assert.False(Directory.Exists(path));
        return (path, handles, privateBytes, elapsed.Elapsed);
    }
}
