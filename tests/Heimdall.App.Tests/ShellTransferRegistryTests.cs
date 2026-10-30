/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using Heimdall.App.Views;

namespace Heimdall.App.Tests;

public sealed class ShellTransferRegistryTests
{
    [Fact]
    public void Stop_CancelsActiveTransfersOnceAndRejectsNewTransfers()
    {
        var registry = new ShellTransferRegistry();
        int cancelled = 0;
        Action cancel = () => cancelled++;
        Assert.True(registry.Add(cancel));
        registry.Stop();
        registry.Stop();
        Assert.Equal(1, cancelled);
        Assert.False(registry.Add(() => { }));
    }

    [Fact]
    public void CompletedTransfer_IsNotCancelled()
    {
        var registry = new ShellTransferRegistry();
        int cancelled = 0;
        Action cancel = () => cancelled++;
        Assert.True(registry.Add(cancel));
        registry.Remove(cancel);
        registry.Stop();
        Assert.Equal(0, cancelled);
    }

    [Fact]
    public void Stop_ContinuesWhenCancellationCallbackThrows()
    {
        var registry = new ShellTransferRegistry();
        int cancelled = 0;
        Assert.True(registry.Add(() => throw new AggregateException(new InvalidOperationException())));
        Assert.True(registry.Add(() => cancelled++));
        registry.Stop();
        Assert.Equal(1, cancelled);
    }

    [Fact]
    public async Task ConcurrentStopAndRegistration_DoesNotLoseCancellation()
    {
        for (int i = 0; i < 1000; i++)
        {
            var registry = new ShellTransferRegistry();
            int cancelled = 0;
            Action cancel = () => Interlocked.Increment(ref cancelled);
            await Task.WhenAll(Task.Run(() => { if (!registry.Add(cancel)) { cancel(); } }), Task.Run(registry.Stop));
            Assert.Equal(1, cancelled);
        }
    }
}
