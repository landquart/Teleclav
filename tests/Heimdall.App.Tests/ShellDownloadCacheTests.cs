/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;
using Heimdall.App.Views;

namespace Heimdall.App.Tests;

public sealed class ShellDownloadCacheTests
{
    [Fact]
    public async Task AlreadyCancelledRequest_DoesNotStartDownload()
    {
        using var cache = new ShellDownloadCache(2, (_, path, ct) => File.WriteAllTextAsync(path, "ok", ct));
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.OpenAsync(0, 2, stop.Token));
        using Stream stream = await cache.OpenAsync(1, 2, CancellationToken.None);
        Assert.Equal('o', stream.ReadByte());
    }

    [Fact]
    public async Task RepeatedCompletedTransfers_RemoveTheirCacheDirectories()
    {
        for (int i = 0; i < 100; i++)
        {
            var cache = new ShellDownloadCache(2, (_, path, ct) => File.WriteAllTextAsync(path, "ok", ct));
            string directory = cache.DirectoryPath;
            using (Stream stream = await cache.OpenAsync(0, 2, CancellationToken.None))
            {
                Assert.Equal('o', stream.ReadByte());
            }
            cache.Dispose();
            Assert.False(Directory.Exists(directory));
        }
    }

    [Fact]
    public async Task ConcurrentRequests_ShareDownloadButHaveIndependentReaders()
    {
        int count = 0;
        var cache = new ShellDownloadCache(100, async (_, path, ct) =>
        {
            Interlocked.Increment(ref count);
            await Task.Delay(20, ct);
            await File.WriteAllTextAsync(path, "hello", ct);
        });
        string directory = cache.DirectoryPath;
        Task<Stream> first = cache.OpenAsync(0, 5, CancellationToken.None);
        Task<Stream> second = cache.OpenAsync(0, 5, CancellationToken.None);
        using Stream a = await first;
        using Stream b = await second;
        Assert.Equal(1, count);
        Assert.Equal('h', a.ReadByte());
        Assert.Equal('h', b.ReadByte());
        cache.Dispose();
        Assert.True(Directory.Exists(directory));
        a.Dispose();
        b.Dispose();
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task Quota_RejectsBeforeDownload()
    {
        using var cache = new ShellDownloadCache(4, (_, _, _) => throw new Exception("Download started"));
        await Assert.ThrowsAsync<IOException>(() => cache.OpenAsync(0, 5, CancellationToken.None));
    }

    [Fact]
    public async Task CancelledWaiter_DoesNotCancelAnotherWaiter()
    {
        var begun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cache = new ShellDownloadCache(10, async (_, path, ct) =>
        {
            begun.SetResult();
            await finish.Task.WaitAsync(ct);
            await File.WriteAllTextAsync(path, "ok", ct);
        });
        using var stop = new CancellationTokenSource();
        Task<Stream> cancelled = cache.OpenAsync(0, 2, stop.Token);
        await begun.Task;
        Task<Stream> survivor = cache.OpenAsync(0, 2, CancellationToken.None);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        finish.SetResult();
        using Stream stream = await survivor;
        Assert.Equal('o', stream.ReadByte());
    }

    [Fact]
    public async Task SizeMismatch_FailsAndCleansUp()
    {
        var cache = new ShellDownloadCache(10, (_, path, ct) => File.WriteAllTextAsync(path, "toolong", ct));
        string directory = cache.DirectoryPath;
        await Assert.ThrowsAsync<IOException>(() => cache.OpenAsync(0, 2, CancellationToken.None));
        cache.Dispose();
        Assert.False(Directory.Exists(directory));
    }
}
