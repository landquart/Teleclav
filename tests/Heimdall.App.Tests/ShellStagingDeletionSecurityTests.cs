/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Heimdall.App.Views;
using Microsoft.Win32.SafeHandles;

namespace Heimdall.App.Tests;

public sealed class ShellStagingDeletionSecurityTests
{
    [Fact]
    public void NativeLayouts_MatchTheWindowsHeaderContract()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        ShellStagingGuard.ValidateInteropLayout();
        Type guard = typeof(ShellStagingGuard);
        Assert.Equal(1, Marshal.SizeOf(guard.GetNestedType("FileDispositionInformation", BindingFlags.NonPublic)!));
        Assert.Equal(52, Marshal.SizeOf(guard.GetNestedType("FileInformation", BindingFlags.NonPublic)!));
        Assert.Equal(24, Marshal.SizeOf(guard.GetNestedType("FileIdInformation", BindingFlags.NonPublic)!));
        area.AssertSentinel();
    }

    [Theory]
    [InlineData("file")]
    [InlineData("directory")]
    [InlineData("symlink")]
    [InlineData("junction")]
    public async Task ReplacedFileOrLink_RefusesWithoutTouchingOutsideData(string replacement)
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        using var guard = ShellStagingGuard.CreateAt(area.Root);
        await ShellStagingDeletionTests.Write(guard);
        string original = Path.Combine(guard.DirectoryPath, "0");
        string saved = Path.Combine(area.Controls, "owned-original");
        File.Move(original, saved);
        switch (replacement)
        {
            case "file": File.WriteAllText(original, "foreign"); break;
            case "directory": Directory.CreateDirectory(original); File.WriteAllText(Path.Combine(original, "keep"), "foreign"); break;
            case "symlink": File.CreateSymbolicLink(original, area.Sentinel); break;
            case "junction": await Junction(original, area.Controls); break;
        }
        Assert.Throws<IOException>(guard.Delete);
        Assert.Equal("hello", File.ReadAllText(saved));
        if (replacement == "directory") { Assert.Equal("foreign", File.ReadAllText(Path.Combine(original, "keep"))); }
        area.AssertSentinel();
    }

    [Fact]
    public async Task AddedHardLink_RefusesEvenWithMatchingFileIdAndCreationTag()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        using var guard = ShellStagingGuard.CreateAt(area.Root);
        await ShellStagingDeletionTests.Write(guard);
        string file = Path.Combine(guard.DirectoryPath, "0");
        string alias = Path.Combine(area.Controls, "alias");
        Assert.True(CreateHardLink(alias, file, IntPtr.Zero), "CreateHardLink failed: " + Marshal.GetLastWin32Error());
        Assert.Throws<IOException>(guard.Delete);
        Assert.Equal("hello", File.ReadAllText(file));
        Assert.Equal("hello", File.ReadAllText(alias));
        File.Delete(alias);
        guard.Delete();
        area.AssertSentinel();
    }

    [Fact]
    public void HardLinkedOwnershipJournal_IsRefusedOnReopen()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        string path;
        using (var guard = ShellStagingGuard.CreateAt(area.Root)) { path = guard.DirectoryPath; }
        string journal = Path.Combine(path, ShellStagingGuard.OwnershipName);
        string alias = Path.Combine(area.Controls, "journal-alias");
        Assert.True(CreateHardLink(alias, journal, IntPtr.Zero), "CreateHardLink failed: " + Marshal.GetLastWin32Error());
        byte[] before = File.ReadAllBytes(alias);
        Assert.Throws<IOException>(() => { using var reopened = ShellStagingGuard.Reopen(path); });
        Assert.Equal(before, File.ReadAllBytes(alias));
        area.AssertSentinel();
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("root")]
    [InlineData("operation")]
    public void PinnedParents_CannotBeRenamedDuringOwnership(string component)
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        using var guard = ShellStagingGuard.CreateAt(area.Root);
        string path = component switch
        {
            "parent" => Path.GetDirectoryName(area.Root)!,
            "root" => area.Root,
            _ => guard.DirectoryPath,
        };
        Assert.ThrowsAny<IOException>(() => Directory.Move(path, path + "-replaced"));
        guard.Delete();
        area.AssertSentinel();
    }

    [Fact]
    public async Task ReplacedOperationDirectory_WithCopiedJournal_CannotClaimOriginalIdentity()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        string path;
        using (var guard = ShellStagingGuard.CreateAt(area.Root)) { await ShellStagingDeletionTests.Write(guard); path = guard.DirectoryPath; }
        string saved = Path.Combine(area.Controls, "saved-operation");
        Directory.Move(path, saved);
        var security = new DirectoryInfo(saved).GetAccessControl();
        Directory.CreateDirectory(path);
        new DirectoryInfo(path).SetAccessControl(security);
        File.Copy(Path.Combine(saved, ShellStagingGuard.OwnershipName), Path.Combine(path, ShellStagingGuard.OwnershipName));
        File.WriteAllText(Path.Combine(path, "foreign"), "foreign");
        Assert.Throws<IOException>(() => { using var reopened = ShellStagingGuard.Reopen(path); });
        Assert.Equal("foreign", File.ReadAllText(Path.Combine(path, "foreign")));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(saved, "0")));
        area.AssertSentinel();
    }

    [Fact]
    public async Task UnknownEntry_PreventsAllDeletionAndPreservesRecoveryJournal()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        using var guard = ShellStagingGuard.CreateAt(area.Root);
        await ShellStagingDeletionTests.Write(guard);
        File.WriteAllText(Path.Combine(guard.DirectoryPath, "foreign"), "foreign");
        Assert.Throws<IOException>(guard.Delete);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(guard.DirectoryPath, "0")));
        Assert.Equal("foreign", File.ReadAllText(Path.Combine(guard.DirectoryPath, "foreign")));
        Assert.True(File.Exists(Path.Combine(guard.DirectoryPath, ShellStagingGuard.OwnershipName)));
        area.AssertSentinel();
    }

    [Fact]
    public async Task EntryAppearingBeforeTeardown_RetainsJournalAndIsNeverDeleted()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        string path;
        using (var guard = ShellStagingGuard.CreateAt(area.Root))
        {
            path = guard.DirectoryPath;
            await ShellStagingDeletionTests.Write(guard);
            guard.Fault = point => { if (point == ShellStagingPoint.BeforeJournalDelete) { File.WriteAllText(Path.Combine(path, "foreign"), "foreign"); } };
            Assert.Throws<IOException>(guard.Delete);
            Assert.True(File.Exists(Path.Combine(path, ShellStagingGuard.OwnershipName)));
        }
        using var reopened = ShellStagingGuard.Reopen(path);
        Assert.Throws<IOException>(reopened.Delete);
        Assert.Equal("foreign", File.ReadAllText(Path.Combine(path, "foreign")));
        File.Delete(Path.Combine(path, "foreign"));
        reopened.Delete();
        area.AssertSentinel();
    }

    [Fact]
    public async Task CorruptJournal_RefusesRecoveryWithoutRemovingOwnedOrOutsideData()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        string path;
        using (var guard = ShellStagingGuard.CreateAt(area.Root)) { await ShellStagingDeletionTests.Write(guard); path = guard.DirectoryPath; }
        string journal = Path.Combine(path, ShellStagingGuard.OwnershipName);
        byte[] bytes = File.ReadAllBytes(journal);
        bytes[^1] ^= 1;
        File.WriteAllBytes(journal, bytes);
        Assert.Throws<IOException>(() => { using var reopened = ShellStagingGuard.Reopen(path); });
        Assert.Equal("hello", File.ReadAllText(Path.Combine(path, "0")));
        area.AssertSentinel();
    }

    [Fact]
    public async Task ReadOnlyOrDeniedAccess_RefusesAndCanRetryAfterRestoration()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        using var guard = ShellStagingGuard.CreateAt(area.Root);
        await ShellStagingDeletionTests.Write(guard);
        string path = Path.Combine(guard.DirectoryPath, "0");
        FileAttributes original = File.GetAttributes(path);
        File.SetAttributes(path, original | FileAttributes.ReadOnly);
        try { Assert.Throws<IOException>(guard.Delete); Assert.Equal("hello", File.ReadAllText(path)); }
        finally { File.SetAttributes(path, original); }
        guard.Delete();
        area.AssertSentinel();
    }

    [Fact]
    public async Task ExplicitAccessDenial_IsReportedAndRetryPreservesTheOriginalIdentity()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        using var guard = ShellStagingGuard.CreateAt(area.Root);
        await ShellStagingDeletionTests.Write(guard);
        var file = new FileInfo(Path.Combine(guard.DirectoryPath, "0"));
        FileSecurity original = file.GetAccessControl();
        FileSecurity changed = file.GetAccessControl();
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        changed.AddAccessRule(new FileSystemAccessRule(identity.User!,
            FileSystemRights.ReadAttributes | FileSystemRights.ReadPermissions, AccessControlType.Deny));
        file.SetAccessControl(changed);
        try { Assert.Throws<IOException>(guard.Delete); area.AssertSentinel(); }
        finally
        {
            original.SetSecurityDescriptorBinaryForm(original.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
            file.SetAccessControl(original);
        }
        Assert.Equal("hello", File.ReadAllText(file.FullName));
        guard.Delete();
    }

    [Fact]
    public async Task MissingOwnedFile_IsReconciledWithoutDeletingAnythingElse()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        string path;
        using (var guard = ShellStagingGuard.CreateAt(area.Root)) { await ShellStagingDeletionTests.Write(guard); path = guard.DirectoryPath; }
        File.Delete(Path.Combine(path, "0"));
        using var reopened = ShellStagingGuard.Reopen(path);
        reopened.Delete();
        Assert.False(Directory.Exists(path));
        area.AssertSentinel();
    }

    [Fact]
    public async Task SeparateProcessLock_BlocksDeletionUntilTheReaderCloses()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        using var guard = ShellStagingGuard.CreateAt(area.Root);
        await ShellStagingDeletionTests.Write(guard);
        string file = Path.Combine(guard.DirectoryPath, "0");
        using var child = new Child(file, "lock");
        Assert.Equal("ready", await child.Process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)));
        Assert.Throws<IOException>(guard.Delete);
        area.AssertSentinel();
        await child.Release();
        guard.Delete();
        Assert.False(Directory.Exists(guard.DirectoryPath));
    }

    [Fact]
    public async Task CompetingRecoveryProcess_CannotAcquireALiveOwnersLease()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        using var owner = new Child(area.Root, "hold");
        string path = (await owner.Process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)))!;
        Assert.False(string.IsNullOrEmpty(path));
        using (var contender = new Child(path, "recover")) { await contender.ExpectExit(11); }
        Assert.Equal("hello", File.ReadAllText(Path.Combine(path, "0")));
        area.AssertSentinel();
        await owner.Release();
        using (var contender = new Child(path, "recover")) { await contender.ExpectExit(0); }
        Assert.False(Directory.Exists(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CrashAfterFirstDelete_ResumesWithoutDeletingAReusedName(bool reuseName)
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        using (var child = new Child(area.Root, "partial")) { await child.ExpectExit(37); }
        string path = Assert.Single(Directory.GetDirectories(area.Root));
        string missing = Assert.Single(new[] { "0", "1" }, name => !File.Exists(Path.Combine(path, name)));
        string remaining = missing == "0" ? "1" : "0";
        if (reuseName) { File.WriteAllText(Path.Combine(path, missing), "foreign"); }
        using var reopened = ShellStagingGuard.Reopen(path);
        if (reuseName)
        {
            Assert.Throws<IOException>(reopened.Delete);
            Assert.Equal("foreign", File.ReadAllText(Path.Combine(path, missing)));
            Assert.Equal("hello", File.ReadAllText(Path.Combine(path, remaining)));
            File.Delete(Path.Combine(path, missing));
        }
        reopened.Delete();
        Assert.False(Directory.Exists(path));
        area.AssertSentinel();
    }

    [Fact]
    public async Task JunctionToAnotherVolume_IsNotTraversedForDeletion()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        string? runnerTemp = Environment.GetEnvironmentVariable("RUNNER_TEMP");
        Assert.False(string.IsNullOrEmpty(runnerTemp), "This acceptance test requires the Windows CI second volume.");
        string other = Directory.CreateDirectory(Path.Combine(runnerTemp!, "teleclav-other-volume-" + Guid.NewGuid().ToString("N"))).FullName;
        string sentinel = Path.Combine(other, "keep");
        try
        {
            File.WriteAllText(sentinel, "other volume: preserve");
            using var guard = ShellStagingGuard.CreateAt(area.Root);
            await ShellStagingDeletionTests.Write(guard);
            using (var first = File.OpenRead(area.Sentinel))
            using (var second = File.OpenRead(sentinel))
            {
                Assert.True(GetFileId(first.SafeFileHandle, 18, out NativeFileId firstId, 24));
                Assert.True(GetFileId(second.SafeFileHandle, 18, out NativeFileId secondId, 24));
                Assert.NotEqual(firstId.Volume, secondId.Volume);
            }
            string file = Path.Combine(guard.DirectoryPath, "0");
            File.Delete(file);
            await Junction(file, other);
            Assert.Throws<IOException>(guard.Delete);
            Assert.Equal("other volume: preserve", File.ReadAllText(sentinel));
            area.AssertSentinel();
        }
        finally { Directory.Delete(other, recursive: true); }
    }

    private static async Task Junction(string link, string target)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe")) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J");
        start.ArgumentList.Add(link); start.ArgumentList.Add(target);
        using Process process = Process.Start(start)!;
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, process.ExitCode);
    }

    private sealed class Child : IDisposable
    {
        internal Process Process { get; }
        internal Child(string path, string mode)
        {
            string executable = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(a => a.Key == "StagingCrashStubPath").Value!;
            var start = new ProcessStartInfo(executable)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(path); start.ArgumentList.Add(mode);
            Process = Process.Start(start)!;
        }
        internal async Task ExpectExit(int code)
        {
            await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(Process.ExitCode == code, await Process.StandardError.ReadToEndAsync());
        }
        internal async Task Release()
        {
            await Process.StandardInput.WriteLineAsync("release");
            await Process.StandardInput.FlushAsync();
            await ExpectExit(0);
        }
        public void Dispose()
        {
            if (!Process.HasExited) { Process.Kill(entireProcessTree: true); Process.WaitForExit(5000); }
            Process.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileId { internal ulong Volume; internal ulong Low; internal ulong High; }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string name, string existing, IntPtr security);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileId(SafeFileHandle handle, int informationClass, out NativeFileId identity, uint size);
}
