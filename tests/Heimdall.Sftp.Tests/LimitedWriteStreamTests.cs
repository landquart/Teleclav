/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;
using Heimdall.Sftp;

namespace Heimdall.Sftp.Tests;

public sealed class LimitedWriteStreamTests
{
    [Fact]
    public void Write_AtLimit_SucceedsAndOneByteMoreDoesNotReachDestination()
    {
        using MemoryStream destination = new();
        using LimitedWriteStream stream = new(destination, 3);
        stream.Write([1, 2, 3], 0, 3);
        Assert.Throws<IOException>(() => stream.Write([4], 0, 1));
        Assert.Equal(new byte[] { 1, 2, 3 }, destination.ToArray());
    }

    [Fact]
    public async Task WriteAsync_OverLimit_DoesNotWriteAnyOfTheExcessCall()
    {
        using MemoryStream destination = new();
        using LimitedWriteStream stream = new(destination, 2);
        await stream.WriteAsync(new byte[] { 1 }, CancellationToken.None);
        await Assert.ThrowsAsync<IOException>(async () => await stream.WriteAsync(new byte[] { 2, 3 }, CancellationToken.None));
        Assert.Equal(new byte[] { 1 }, destination.ToArray());
    }

    [Fact]
    public void ZeroLimit_RejectsAnyByte()
    {
        using MemoryStream destination = new();
        using LimitedWriteStream stream = new(destination, 0);
        Assert.Throws<IOException>(() => stream.Write([1], 0, 1));
        Assert.Empty(destination.ToArray());
    }
}
