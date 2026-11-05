/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;
using Heimdall.App.Views;

namespace Heimdall.App.Tests;

public sealed class ShellDeferredStreamTests
{
    [Fact]
    public async Task CancelPending_UnblocksReadWhenOpenerIgnoresCancellation()
    {
        var ready = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stream = new ShellDeferredStream(_ => ready.Task, 1, _ => { }, () => { });
        Task read = Task.Run(() =>
        {
            started.SetResult();
            Assert.ThrowsAny<System.Runtime.InteropServices.COMException>(() => stream.Read(new byte[1], 0, 1));
        });
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            stream.CancelPending();
            await read.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            ready.TrySetResult(new MemoryStream([42]));
            await read.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void VerifiedReader_RemainsReadableWhenLifetimeRejectsNewWork()
    {
        using var stream = new ShellDeferredStream(_ => Task.FromResult<Stream>(new MemoryStream([42])),
            1, _ => { }, () => { }, () => false, () => throw new InvalidOperationException());
        byte[] bytes = new byte[1];
        stream.ReadExactly(bytes);
        Assert.Equal(42, bytes[0]);
    }

    [Fact]
    public async Task DisposeBeforeOpenCompletes_ReleasesLateStreamExactlyOnce()
    {
        var ready = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int releases = 0;
        var stream = new ShellDeferredStream(_ => ready.Task, 0, _ => { }, () => Interlocked.Increment(ref releases));
        var late = new TrackingStream(closed);
        stream.Dispose();
        stream.Dispose();
        Assert.Equal(1, releases);
        ready.SetResult(late);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, late.DisposeCount);
    }

    private sealed class TrackingStream(TaskCompletionSource closed) : MemoryStream
    {
        public int DisposeCount;
        protected override void Dispose(bool disposing)
        {
            Interlocked.Increment(ref DisposeCount);
            base.Dispose(disposing);
            closed.TrySetResult();
        }
    }
}
