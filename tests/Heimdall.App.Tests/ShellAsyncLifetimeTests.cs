/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using Heimdall.App.Views;

namespace Heimdall.App.Tests;

public sealed class ShellAsyncLifetimeTests
{
    [Fact]
    public async Task MissingEndOperation_EventuallyReleasesAfterDragReturns()
    {
        var cleaned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lifetime = new ShellAsyncLifetime(() => cleaned.TrySetResult(), TimeSpan.FromMilliseconds(50));
        Assert.True(lifetime.Begin());
        lifetime.DragReturned();
        await cleaned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        lifetime.End();
    }

    [Fact]
    public async Task IdleDeadline_DoesNotReleaseAnActiveCallback()
    {
        var cleaned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lifetime = new ShellAsyncLifetime(() => cleaned.TrySetResult(), TimeSpan.FromMilliseconds(50));
        Assert.True(lifetime.Begin());
        Assert.True(lifetime.Enter());
        lifetime.DragReturned();
        await Task.Delay(250);
        Assert.False(cleaned.Task.IsCompleted);
        lifetime.Exit();
        await cleaned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(lifetime.Enter());
    }

    [Fact]
    public void CancelBeforeDragReturns_WaitsForActiveCallbackAndDragReturn()
    {
        int cleaned = 0;
        var lifetime = new ShellAsyncLifetime(() => cleaned++);
        Assert.True(lifetime.Begin());
        Assert.True(lifetime.Enter());
        lifetime.Cancel();
        lifetime.DragReturned();
        Assert.Equal(0, cleaned);
        lifetime.Exit();
        Assert.Equal(1, cleaned);
        lifetime.End();
        lifetime.Cancel();
        Assert.Equal(1, cleaned);
        Assert.False(lifetime.Enter());
    }

    [Fact]
    public void AsyncExtractionRetainsResourcesUntilBothOperationsFinish()
    {
        int cleaned = 0;
        var lifetime = new ShellAsyncLifetime(() => cleaned++);
        Assert.True(lifetime.Begin());
        lifetime.DragReturned();
        Assert.Equal(0, cleaned);
        lifetime.End();
        lifetime.End();
        Assert.Equal(1, cleaned);
    }

    [Fact]
    public void ExtractionMayEndBeforeDragReturns()
    {
        int cleaned = 0;
        var lifetime = new ShellAsyncLifetime(() => cleaned++);
        Assert.True(lifetime.Begin());
        lifetime.End();
        Assert.Equal(0, cleaned);
        lifetime.DragReturned();
        Assert.Equal(1, cleaned);
    }

    [Fact]
    public void SynchronousDragCleansUpOnReturn()
    {
        int cleaned = 0;
        var lifetime = new ShellAsyncLifetime(() => cleaned++);
        lifetime.DragReturned();
        Assert.False(lifetime.Begin());
        Assert.Equal(1, cleaned);
    }
    [Fact]
    public async Task ConcurrentCompletionAndReturn_CleansExactlyOnce()
    {
        for (int i = 0; i < 1000; i++)
        {
            int cleaned = 0;
            var lifetime = new ShellAsyncLifetime(() => Interlocked.Increment(ref cleaned));
            Assert.True(lifetime.Begin());
            await Task.WhenAll(Task.Run(lifetime.End), Task.Run(lifetime.DragReturned));
            Assert.Equal(1, cleaned);
        }
    }
    [Fact]
    public void CompletedExtraction_CannotBeStartedAgain()
    {
        var lifetime = new ShellAsyncLifetime(() => { });
        Assert.True(lifetime.Begin());
        lifetime.End();
        Assert.False(lifetime.Begin());
        lifetime.DragReturned();
    }
}
