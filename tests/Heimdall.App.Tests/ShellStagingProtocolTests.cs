/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using Heimdall.App.Views;

namespace Heimdall.App.Tests;

public sealed class ShellStagingProtocolTests
{
    public static IEnumerable<object[]> CreationPoints => Enum.GetValues<ShellStagingPoint>()
        .Where(p => p <= ShellStagingPoint.BeforeComplete).Select(p => new object[] { (int)p });
    public static IEnumerable<object[]> CrashPoints => Enum.GetValues<ShellStagingPoint>().Select(p => new object[] { (int)p });

    [Theory]
    [MemberData(nameof(CreationPoints))]
    public async Task InterruptedCreation_IsRecoverableWithoutTheOriginalGuard(int point)
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new Area();
        string path;
        using (var guard = ShellStagingGuard.CreateAt(area.Root))
        {
            path = guard.DirectoryPath;
            guard.Fault = current => { if ((int)current == point) { throw new IOException("Injected stop."); } };
            await Assert.ThrowsAsync<IOException>(() => Write(guard));
        }
        using var reopened = ShellStagingGuard.Reopen(path);
        if ((ShellStagingPoint)point == ShellStagingPoint.AfterCreate)
        {
            Assert.Equal(ShellStagingGuard.FileStage.Reserved, reopened.GetFileState("0")!.Stage);
            Assert.Null(reopened.GetFileState("0")!.Identity);
            Assert.Single(Directory.GetFiles(path, "*.part"));
        }
        if ((ShellStagingPoint)point >= ShellStagingPoint.AfterPublish)
        {
            Assert.True(File.Exists(Path.Combine(path, "0")));
            using var file = File.OpenRead(Path.Combine(path, "0"));
            Assert.Throws<IOException>(() => reopened.Verify("0", file.SafeFileHandle));
        }
        reopened.Delete();
        Assert.False(Directory.Exists(path));
    }

    [Theory]
    [MemberData(nameof(CrashPoints))]
    public async Task ProcessExitAtEachCreationBoundary_ReopensAndCleansSafely(int point)
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new Area();
        string executable = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "StagingCrashStubPath").Value!;
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(area.Root);
        start.ArgumentList.Add(point.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using Process process = Process.Start(start)!;
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
        catch { if (!process.HasExited) { process.Kill(entireProcessTree: true); } throw; }
        Assert.True(process.ExitCode == 37, await process.StandardError.ReadToEndAsync());
        string path = Assert.Single(Directory.GetDirectories(area.Root));
        using var reopened = ShellStagingGuard.Reopen(path);
        reopened.Delete();
        Assert.False(Directory.Exists(path));
    }

    [Theory]
    [InlineData((int)ShellStagingPoint.AfterDeleteIntent)]
    [InlineData((int)ShellStagingPoint.AfterDelete)]
    public async Task InterruptedDeletion_IsIdempotent(int point)
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new Area();
        string path;
        using (var guard = ShellStagingGuard.CreateAt(area.Root))
        {
            path = guard.DirectoryPath;
            await Write(guard);
            guard.Fault = current => { if ((int)current == point) { throw new IOException("Injected deletion stop."); } };
            Assert.Throws<IOException>(guard.Delete);
        }
        using var reopened = ShellStagingGuard.Reopen(path);
        reopened.Delete();
        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public async Task ExistingFileAtReservedName_IsNeverAdoptedOrOverwritten()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new Area();
        string path;
        string? foreign = null;
        using (var guard = ShellStagingGuard.CreateAt(area.Root))
        {
            path = guard.DirectoryPath;
            guard.Fault = point =>
            {
                if (point != ShellStagingPoint.BeforeCreate) { return; }
                foreign = Path.Combine(path, guard.GetFileState("0")!.Partial);
                File.WriteAllText(foreign, "foreign");
                var security = new FileSecurity();
                security.SetSecurityDescriptorBinaryForm(ShellStagingDirectory.CreateJournalSecurityDescriptor());
                new FileInfo(foreign).SetAccessControl(security);
            };
            await Assert.ThrowsAsync<IOException>(() => Write(guard));
        }
        using var reopened = ShellStagingGuard.Reopen(path);
        Assert.Throws<IOException>(reopened.Delete);
        Assert.Equal("foreign", File.ReadAllText(foreign!));
    }

    [Fact]
    public async Task PublicationCollision_PreservesBothForeignTargetAndOwnedPartial()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new Area();
        using var guard = ShellStagingGuard.CreateAt(area.Root);
        string target = Path.Combine(guard.DirectoryPath, "0");
        File.WriteAllText(target, "foreign");
        await Assert.ThrowsAsync<IOException>(() => Write(guard));
        Assert.Equal("foreign", File.ReadAllText(target));
        Assert.Equal("hello", File.ReadAllText(Assert.Single(Directory.GetFiles(guard.DirectoryPath, "*.part"))));
        Assert.Throws<IOException>(guard.Delete);
        Assert.Equal("foreign", File.ReadAllText(target));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NetworkOrCancellationAfterWriting_RemainsOwned(bool cancel)
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new Area();
        string path;
        using (var guard = ShellStagingGuard.CreateAt(area.Root))
        {
            path = guard.DirectoryPath;
            Exception error = await Assert.ThrowsAnyAsync<Exception>(() => guard.DownloadAsync("0", 5, async (stream, ct) =>
            {
                await stream.WriteAsync("he"u8.ToArray(), ct);
                if (cancel) { throw new OperationCanceledException(); }
                throw new IOException("Network or disk write failure.");
            }, CancellationToken.None));
            if (cancel) { Assert.IsType<OperationCanceledException>(error); }
            else { Assert.IsType<IOException>(error); }
        }
        using var reopened = ShellStagingGuard.Reopen(path);
        Assert.Equal(ShellStagingGuard.FileStage.Writing, reopened.GetFileState("0")!.Stage);
        reopened.Delete();
        Assert.False(Directory.Exists(path));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(31)]
    public async Task TornLastRecord_UsesValidPrefixAndCreationTag(int missingBytes)
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new Area();
        string path;
        using (var guard = ShellStagingGuard.CreateAt(area.Root))
        {
            path = guard.DirectoryPath;
            guard.Fault = point => { if (point == ShellStagingPoint.AfterIdentity) { throw new IOException("stop"); } };
            await Assert.ThrowsAsync<IOException>(() => Write(guard));
        }
        using (var metadata = File.Open(Path.Combine(path, ShellStagingGuard.OwnershipName), FileMode.Open, FileAccess.Write))
        {
            metadata.SetLength(metadata.Length - missingBytes);
            metadata.Flush(flushToDisk: true);
        }
        using var reopened = ShellStagingGuard.Reopen(path);
        Assert.Equal(ShellStagingGuard.FileStage.Reserved, reopened.GetFileState("0")!.Stage);
        reopened.Delete();
        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public async Task MultipleFiles_ArePersistedIndependentlyAndPublishOnce()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new Area();
        string path;
        using (var guard = ShellStagingGuard.CreateAt(area.Root))
        {
            path = guard.DirectoryPath;
            await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Write(guard, i.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            await Assert.ThrowsAsync<IOException>(() => Write(guard));
        }
        using var reopened = ShellStagingGuard.Reopen(path);
        for (int i = 0; i < 8; i++)
        {
            string name = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(ShellStagingGuard.FileStage.Published, reopened.GetFileState(name)!.Stage);
            using var file = File.OpenRead(Path.Combine(path, name));
            reopened.Verify(name, file.SafeFileHandle);
        }
        reopened.Delete();
        Assert.False(Directory.Exists(path));
    }

    private static Task Write(ShellStagingGuard guard, string target = "0") => guard.DownloadAsync(target, 5,
        (stream, ct) => stream.WriteAsync("hello"u8.ToArray(), ct).AsTask(), CancellationToken.None);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task InvalidSizeOrDiskFull_NeverPublishesAndCanBeRecovered(int failure)
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new Area();
        string path;
        using (var guard = ShellStagingGuard.CreateAt(area.Root))
        {
            path = guard.DirectoryPath;
            await Assert.ThrowsAsync<IOException>(() => guard.DownloadAsync("0", 5, async (stream, ct) =>
            {
                await stream.WriteAsync(failure == 1 ? "toolong"u8.ToArray() : "he"u8.ToArray(), ct);
                if (failure == 2) { throw new IOException("Injected ERROR_DISK_FULL.", unchecked((int)0x80070070)); }
            }, CancellationToken.None));
            Assert.False(File.Exists(Path.Combine(path, "0")));
        }
        using var reopened = ShellStagingGuard.Reopen(path);
        reopened.Delete();
        Assert.False(Directory.Exists(path));
    }

    private sealed class Area : IDisposable
    {
        private string Parent { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Teleclav-protocol-tests-" + Guid.NewGuid().ToString("N"));
        internal string Root => Path.Combine(Parent, "Staging");
        public void Dispose() { if (Directory.Exists(Parent)) { Directory.Delete(Parent, recursive: true); } }
    }
}
