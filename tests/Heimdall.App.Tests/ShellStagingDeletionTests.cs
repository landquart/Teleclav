/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Heimdall.App.Views;

namespace Heimdall.App.Tests;

public sealed class ShellStagingDeletionTests
{
    [Fact]
    public async Task DeleteDuringWrite_DoesNotPoisonTheLiveOperation()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        using var guard = ShellStagingGuard.CreateAt(area.Root);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task transfer = guard.DownloadAsync("0", 5, async (stream, ct) =>
        {
            started.SetResult();
            await finish.Task;
            await stream.WriteAsync("hello"u8.ToArray(), ct);
        }, CancellationToken.None);
        await started.Task;
        try { Assert.Throws<IOException>(guard.Delete); }
        finally { finish.TrySetResult(); }
        await transfer;
        Assert.Equal("hello", File.ReadAllText(Path.Combine(guard.DirectoryPath, "0")));
        guard.Delete();
        area.AssertSentinel();
    }

    [Fact]
    public async Task ChangedOperationAcl_RefusesDeletionUntilTrustIsRestored()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        using var guard = ShellStagingGuard.CreateAt(area.Root);
        await Write(guard);
        var directory = new DirectoryInfo(guard.DirectoryPath);
        DirectorySecurity original = directory.GetAccessControl();
        DirectorySecurity changed = directory.GetAccessControl();
        changed.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Read, AccessControlType.Allow));
        directory.SetAccessControl(changed);
        try
        {
            Assert.Throws<IOException>(guard.Delete);
            Assert.Equal("hello", File.ReadAllText(Path.Combine(guard.DirectoryPath, "0")));
            area.AssertSentinel();
        }
        finally { if (directory.Exists) { directory.SetAccessControl(original); } }
        guard.Delete();
    }

    [Fact]
    public async Task CompletedDelete_IsIdempotentAndNeverTouchesAReusedPath()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        using var guard = ShellStagingGuard.CreateAt(area.Root);
        await Write(guard);
        string path = guard.DirectoryPath;
        guard.Delete();
        Directory.CreateDirectory(path);
        string foreign = Path.Combine(path, "0");
        File.WriteAllText(foreign, "foreign");
        guard.Delete();
        Assert.Equal("foreign", File.ReadAllText(foreign));
        area.AssertSentinel();
    }

    internal static Task Write(ShellStagingGuard guard, string name = "0") => guard.DownloadAsync(name, 5,
        (stream, ct) => stream.WriteAsync("hello"u8.ToArray(), ct).AsTask(), CancellationToken.None);

    [Fact]
    public async Task DisposeDuringWrite_RetainsTheLeaseUntilTheWriterCloses()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        using var guard = ShellStagingGuard.CreateAt(area.Root);
        string path = guard.DirectoryPath;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task transfer = guard.DownloadAsync("0", 5, async (stream, ct) =>
        {
            started.SetResult(); await finish.Task; await stream.WriteAsync("hello"u8.ToArray(), ct);
        }, CancellationToken.None);
        await started.Task;
        guard.Dispose();
        try { Assert.Throws<IOException>(() => { using var other = ShellStagingGuard.Reopen(path); }); }
        finally { finish.TrySetResult(); }
        await transfer;
        using var recovered = ShellStagingGuard.Reopen(path);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(path, "0")));
        recovered.Delete();
        area.AssertSentinel();
    }

    [Fact]
    public async Task ExhaustedBoundedRetries_RetainOwnedFilesAndObservableFailure()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        using var cache = new ShellDownloadCache(5, (_, stream, ct) => stream.WriteAsync("hello"u8.ToArray(), ct).AsTask());
        using (Stream reader = await cache.OpenAsync(0, 5, CancellationToken.None)) { }
        string path = cache.DirectoryPath;
        using (var locked = new FileStream(Path.Combine(path, "0"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            cache.Dispose();
            await Assert.IsAssignableFrom<Task>(cache.CleanupRetries).WaitAsync(TimeSpan.FromSeconds(55));
            Assert.NotNull(cache.CleanupFailure);
            Assert.True(File.Exists(Path.Combine(path, "0")));
            Assert.True(File.Exists(Path.Combine(path, ShellStagingGuard.OwnershipName)));
            Assert.Throws<IOException>(() => { using var other = ShellStagingGuard.Reopen(path); });
            area.AssertSentinel();
        }
        cache.Dispose();
        Assert.Null(cache.CleanupFailure);
        Assert.False(Directory.Exists(path));
    }
}

internal sealed class DeletionArea : IDisposable
{
    private string Parent { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Teleclav-deletion-tests-" + Guid.NewGuid().ToString("N"));
    internal string Root => Path.Combine(Parent, "Staging");
    internal string Controls { get; } = Directory.CreateDirectory(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Teleclav-controls-" + Guid.NewGuid().ToString("N"))).FullName;
    internal string Sentinel => Path.Combine(Controls, "sentinel.txt");
    internal DeletionArea() { File.WriteAllText(Sentinel, "outside staging: preserve"); }
    internal void AssertSentinel() => Assert.Equal("outside staging: preserve", File.ReadAllText(Sentinel));
    public void Dispose()
    {
        try { AssertSentinel(); }
        finally
        {
            if (Directory.Exists(Parent)) { Directory.Delete(Parent, recursive: true); }
            Directory.Delete(Controls, recursive: true);
        }
    }
}
