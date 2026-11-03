/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;
using Heimdall.App.Views;

namespace Heimdall.App.Tests;

public sealed class ShellStagingOwnershipTests
{
    [Fact]
    public void Cache_EstablishesPersistentOwnershipBeforeDownloading()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var cache = new ShellDownloadCache(2, (_, path, ct) => File.WriteAllTextAsync(path, "ok", ct));
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Teleclav", "Staging"), Path.GetDirectoryName(cache.DirectoryPath));
        Assert.True(File.Exists(Path.Combine(cache.DirectoryPath, ".teleclav-owner")));
    }
}
