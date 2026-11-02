/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;
using Heimdall.App.Views;

namespace Heimdall.App.Tests;

public sealed class ShellDownloadCacheTests
{
    [Fact]
    public void Cleanup_RefusesReplacedStagingDirectory()
    {
        var cache = new ShellDownloadCache(2, (_, path, ct) => File.WriteAllTextAsync(path, "ok", ct));
        string original = cache.DirectoryPath;
        string moved = original + "-moved";
        string sentinel = Path.Combine(original, "unrelated.txt");
        try
        {
            try { Directory.Move(original, moved); }
            catch (IOException)
            {
                Assert.True(Directory.Exists(original));
                cache.Dispose();
                return;
            }
            Directory.CreateDirectory(original);
            File.WriteAllText(sentinel, "preserve");
            cache.Dispose();
            Assert.True(File.Exists(sentinel));
        }
        finally
        {
            if (Directory.Exists(original)) { Directory.Delete(original, recursive: true); }
            if (Directory.Exists(moved)) { Directory.Delete(moved, recursive: true); }
        }
    }

    [Fact]
    public async Task Cleanup_RefusesReplacedCachedFile()
    {
        var cache = new ShellDownloadCache(2, (_, path, ct) => File.WriteAllTextAsync(path, "ok", ct));
        string directory = cache.DirectoryPath;
        string original = Path.Combine(directory, "0");
        string saved = directory + "-saved";
        try
        {
            using (Stream reader = await cache.OpenAsync(0, 2, CancellationToken.None)) { }
            File.Move(original, saved);
            File.WriteAllText(original, "do not delete");
            cache.Dispose();
            Assert.True(File.Exists(original));
            Assert.NotNull(cache.CleanupFailure);
        }
        finally
        {
            if (File.Exists(original)) { File.Delete(original); }
            if (File.Exists(saved)) { File.Move(saved, original); }
            cache.Dispose();
            if (File.Exists(saved)) { File.Delete(saved); }
        }
    }

    [Fact]
    public async Task LockedStagingFile_CleanupFailureIsObservable()
    {
        var cache = new ShellDownloadCache(2, (_, path, ct) => File.WriteAllTextAsync(path, "ok", ct));
        string directory = cache.DirectoryPath;
        try
        {
            using (Stream reader = await cache.OpenAsync(0, 2, CancellationToken.None)) { }
            using (FileStream locked = new(Path.Combine(directory, "0"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                cache.Dispose();
                Assert.NotNull(cache.CleanupFailure);
                Assert.True(Directory.Exists(directory));
            }
            cache.Dispose();
            Assert.Null(cache.CleanupFailure);
            Assert.False(Directory.Exists(directory));
        }
        finally
        {
            if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); }
        }
    }

    [Fact]
    public async Task LockedStagingFile_IsRetriedAfterUnlock()
    {
        var cache = new ShellDownloadCache(2, (_, path, ct) => File.WriteAllTextAsync(path, "ok", ct));
        string directory = cache.DirectoryPath;
        try
        {
            using (Stream reader = await cache.OpenAsync(0, 2, CancellationToken.None)) { }
            using (FileStream locked = new(Path.Combine(directory, "0"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                cache.Dispose();
                Assert.NotNull(cache.CleanupFailure);
            }
            for (int i = 0; i < 50 && Directory.Exists(directory); i++) { await Task.Delay(100); }
            Assert.False(Directory.Exists(directory));
            Assert.Null(cache.CleanupFailure);
        }
        finally { cache.Dispose(); }
    }

    [Fact]
    public async Task Dispose_DoesNotWaitForBlockingCancellationCallbacks()
    {
        var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new ShellDownloadCache(1, async (_, _, ct) =>
        {
            using var callback = ct.Register(() => release.Task.GetAwaiter().GetResult());
            registered.SetResult();
            await release.Task;
            ct.ThrowIfCancellationRequested();
        });
        Task<Stream> waiting = cache.OpenAsync(0, 1, CancellationToken.None);
        await registered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Task cancel = Task.Run(cache.Dispose);
            await cancel.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            release.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(5)));
            cache.Dispose();
        }
    }

    [Fact]
    public async Task Timeout_StopsWaitingAndCancelsDownloadButRetainsActiveStaging()
    {
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken downloadToken = default;
        var cache = new ShellDownloadCache(2, async (_, path, ct) =>
        {
            downloadToken = ct;
            started.SetResult();
            await finish.Task;
            await File.WriteAllTextAsync(path, "ok");
        }, TimeSpan.FromSeconds(2));
        string directory = cache.DirectoryPath;
        try
        {
            Task<Stream> pending = cache.OpenAsync(0, 2, CancellationToken.None);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<TimeoutException>(() => pending);
            Assert.True(downloadToken.IsCancellationRequested);
            Assert.True(Directory.Exists(directory));
        }
        finally { finish.SetResult(); cache.Dispose(); }
    }

    [Fact]
    public async Task Dispose_UnblocksWaiterEvenIfDownloadDoesNotObserveCancellation()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new ShellDownloadCache(2, async (_, path, _) =>
        {
            started.SetResult();
            await finish.Task;
            await File.WriteAllTextAsync(path, "ok");
        });
        string directory = cache.DirectoryPath;
        Task<Stream> waiting = cache.OpenAsync(0, 2, CancellationToken.None);
        await started.Task;
        cache.Dispose();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.True(Directory.Exists(directory));
        }
        finally { finish.SetResult(); }
    }

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
