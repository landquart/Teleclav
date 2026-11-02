/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;
using Heimdall.App.Views;

namespace Heimdall.App.Tests;

public sealed class ShellDeferredStreamTests
{
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
