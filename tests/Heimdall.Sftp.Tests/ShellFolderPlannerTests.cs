/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using Heimdall.Sftp;

namespace Heimdall.Sftp.Tests;

public sealed class ShellFolderPlannerTests
{
    private static SftpFileInfo Item(string name, string path, RemoteEntryKind kind, long size = 0)
        => new(name, path, kind, size, DateTime.UnixEpoch, "", "", "");

    [Fact]
    public async Task Plan_PreservesNestedAndEmptyFolders()
    {
        var root = Item("project", "/project", RemoteEntryKind.Directory);
        var children = new Dictionary<string, IReadOnlyList<SftpFileInfo>>
        {
            ["/project"] = [Item("readme.txt", "/project/readme.txt", RemoteEntryKind.File, 3), Item("empty", "/project/empty", RemoteEntryKind.Directory)],
            ["/project/empty"] = []
        };
        var plan = await ShellFolderPlanner.PlanAsync([root], (path, _) => Task.FromResult(children[path]));
        Assert.Equal(["project", "project/readme.txt", "project/empty"], plan.Select(x => x.RelativePath));
        Assert.Equal([RemoteDownloadOpKind.MakeDirectory, RemoteDownloadOpKind.DownloadFile, RemoteDownloadOpKind.MakeDirectory], plan.Select(x => x.Kind));
    }

    [Fact]
    public async Task Plan_RejectsCaseAndNormalizationCollisions()
    {
        var root = Item("d", "/d", RemoteEntryKind.Directory);
        var children = new[] { Item("é", "/d/é", RemoteEntryKind.File), Item("e\u0301", "/d/e\u0301", RemoteEntryKind.File) };
        await Assert.ThrowsAsync<IOException>(() => ShellFolderPlanner.PlanAsync([root], (_, _) => Task.FromResult<IReadOnlyList<SftpFileInfo>>(children)));
    }

    [Fact]
    public async Task Plan_RejectsSpecialObjectsInsteadOfPartialSuccess()
    {
        var root = Item("d", "/d", RemoteEntryKind.Directory);
        var children = new[] { Item("loop", "/d/loop", RemoteEntryKind.SymbolicLink) };
        await Assert.ThrowsAsync<IOException>(() => ShellFolderPlanner.PlanAsync([root], (_, _) => Task.FromResult<IReadOnlyList<SftpFileInfo>>(children)));
    }

    [Fact]
    public async Task Plan_RejectsChildOutsideListedDirectory()
    {
        var root = Item("d", "/d", RemoteEntryKind.Directory);
        var children = new[] { Item("x", "/elsewhere/x", RemoteEntryKind.File) };
        await Assert.ThrowsAsync<IOException>(() => ShellFolderPlanner.PlanAsync([root], (_, _) => Task.FromResult<IReadOnlyList<SftpFileInfo>>(children)));
    }

    [Fact]
    public async Task Plan_PropagatesCancellationAndListingFailure()
    {
        var root = Item("d", "/d", RemoteEntryKind.Directory);
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ShellFolderPlanner.PlanAsync([root], (_, _) => throw new Exception(), stop.Token));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ShellFolderPlanner.PlanAsync([root], (_, _) => throw new UnauthorizedAccessException()));
    }
    [Theory]
    [InlineData("CON")]
    [InlineData("bad:name")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    [InlineData("with\\slash")]
    public async Task Plan_RefusesUnrepresentableNames(string name)
    {
        var root = Item("d", "/d", RemoteEntryKind.Directory);
        var children = new[] { Item(name, "/d/" + name, RemoteEntryKind.File) };
        await Assert.ThrowsAsync<IOException>(() => ShellFolderPlanner.PlanAsync([root], (_, _) => Task.FromResult<IReadOnlyList<SftpFileInfo>>(children)));
    }

    [Theory]
    [InlineData(RemoteEntryKind.Fifo)]
    [InlineData(RemoteEntryKind.Socket)]
    [InlineData(RemoteEntryKind.Device)]
    [InlineData(RemoteEntryKind.Unknown)]
    public async Task Plan_RefusesSpecialTypes(RemoteEntryKind kind)
    {
        var item = Item("object", "/object", kind);
        await Assert.ThrowsAsync<IOException>(() => ShellFolderPlanner.PlanAsync([item], (_, _) => throw new Exception("must not list")));
    }

    [Fact]
    public async Task Plan_MixedSelectionAndUnicode()
    {
        var folder = Item("папка", "/папка", RemoteEntryKind.Directory);
        var file = Item("café file.txt", "/café file.txt", RemoteEntryKind.File, 7);
        var plan = await ShellFolderPlanner.PlanAsync([folder, file], (_, _) => Task.FromResult<IReadOnlyList<SftpFileInfo>>([]));
        Assert.Equal(["папка", "café file.txt"], plan.Select(item => item.RelativePath));
    }

    [Fact]
    public async Task Plan_RejectsDuplicateNamesIgnoringCase()
    {
        var a = Item("File.txt", "/a/File.txt", RemoteEntryKind.File);
        var b = Item("file.txt", "/b/file.txt", RemoteEntryKind.File);
        await Assert.ThrowsAsync<IOException>(() => ShellFolderPlanner.PlanAsync([a, b], (_, _) => throw new Exception("must not list")));
    }
    [Fact]
    public async Task Plan_RejectsSelectedPathWithTraversal()
    {
        var file = Item("x", "/d/../secret/x", RemoteEntryKind.File);
        await Assert.ThrowsAsync<IOException>(() => ShellFolderPlanner.PlanAsync([file], (_, _) => throw new Exception("must not list")));
    }
    [Fact]
    public async Task Plan_RejectsNegativeFileSize()
    {
        var file = Item("bad", "/bad", RemoteEntryKind.File, -1);
        await Assert.ThrowsAsync<IOException>(() => ShellFolderPlanner.PlanAsync([file], (_, _) => throw new Exception("must not list")));
    }
    [Fact]
    public async Task Plan_RejectsNegativeSizeInsideDirectory()
    {
        var root = Item("d", "/d", RemoteEntryKind.Directory);
        var children = new[] { Item("bad", "/d/bad", RemoteEntryKind.File, -1) };
        await Assert.ThrowsAsync<IOException>(() => ShellFolderPlanner.PlanAsync([root], (_, _) => Task.FromResult<IReadOnlyList<SftpFileInfo>>(children)));
    }
}
