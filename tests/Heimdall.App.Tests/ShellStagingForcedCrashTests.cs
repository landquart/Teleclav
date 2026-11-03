/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Heimdall.App.Views;

namespace Heimdall.App.Tests;

public sealed class ShellStagingForcedCrashTests
{
    public static IEnumerable<object[]> KillPoints => ShellStagingProtocolTests.CrashPoints
        .Select(p => new object[] { ((ShellStagingPoint)(int)p[0]).ToString() });

    [Theory]
    [MemberData(nameof(KillPoints))]
    [InlineData("journal")]
    [InlineData("reader-closed")]
    public async Task ExternalTermination_RecoversEveryRegisteredBoundaryInANewProcess(string point)
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        using var owner = new CrashChild(area.Root, "kill:" + point);
        await owner.Ready();
        Assert.Single(Directory.GetDirectories(area.Root));
        await owner.Kill();
        if (point == nameof(ShellStagingPoint.DuringWrite))
        {
            string operation = Assert.Single(Directory.GetDirectories(area.Root));
            Assert.Equal("he", File.ReadAllText(Assert.Single(Directory.GetFiles(operation, "*.part"))));
        }
        await Scan(area.Root);
        Assert.Empty(Directory.GetDirectories(area.Root));
        await Scan(area.Root);
        await Transfer(area.Root);
        Assert.Empty(Directory.GetDirectories(area.Root));
        area.AssertSentinel();
    }

    [Fact]
    public async Task KilledBeforeJournal_LeavesUnprovenDirectoryAndAllowsNewTransfers()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        using var owner = new CrashChild(area.Root, "kill:directory");
        await owner.Ready();
        string path = Assert.Single(Directory.GetDirectories(area.Root));
        Assert.Empty(Directory.GetFileSystemEntries(path));
        await owner.Kill();
        await Scan(area.Root);
        Assert.True(Directory.Exists(path));
        Assert.Empty(Directory.GetFileSystemEntries(path));
        await Scan(area.Root);
        await Transfer(area.Root);
        Assert.Equal(path, Assert.Single(Directory.GetDirectories(area.Root)));
        area.AssertSentinel();
    }

    [Fact]
    public async Task KilledRecovery_AfterFirstDeletion_IsResumedByAnotherProcess()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        string path;
        using (var operation = ShellStagingGuard.CreateAt(area.Root))
        {
            path = operation.DirectoryPath;
            await ShellStagingDeletionTests.Write(operation, "0");
            await ShellStagingDeletionTests.Write(operation, "1");
        }
        using var recovery = new CrashChild(path, "kill:recovery");
        await recovery.Ready();
        Assert.True(File.Exists(Path.Combine(path, ShellStagingGuard.OwnershipName)));
        Assert.Single(Directory.GetFiles(path), file => Path.GetFileName(file) != ShellStagingGuard.OwnershipName);
        await Scan(area.Root);
        Assert.Single(Directory.GetFiles(path), file => Path.GetFileName(file) != ShellStagingGuard.OwnershipName);
        await recovery.Kill();
        await Scan(area.Root);
        Assert.False(Directory.Exists(path));
        await Scan(area.Root);
        await Transfer(area.Root);
        area.AssertSentinel();
    }

    [Fact]
    public async Task TwoActiveOwners_SurviveScan_AndOnlyTheKilledOwnerIsRecovered()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        using var first = new CrashChild(area.Root, "kill:reader-open");
        using var second = new CrashChild(area.Root, "kill:reader-open");
        await first.Ready();
        await second.Ready();
        Assert.Equal(2, Directory.GetDirectories(area.Root).Length);
        await Scan(area.Root);
        Assert.Equal(2, Directory.GetDirectories(area.Root).Length);
        await first.Kill();
        await Scan(area.Root);
        string live = Assert.Single(Directory.GetDirectories(area.Root));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(live, "0")));
        await second.Kill();
        await Scan(area.Root);
        Assert.Empty(Directory.GetDirectories(area.Root));
        await Transfer(area.Root);
        area.AssertSentinel();
    }

    [Fact]
    public async Task ExternalReader_OutlivesKilledOwner_ThenUnlockPermitsRecovery()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        using var owner = new CrashChild(area.Root, "kill:reader-open");
        await owner.Ready();
        string path = Assert.Single(Directory.GetDirectories(area.Root));
        using var reader = new CrashChild(Path.Combine(path, "0"), "lock");
        await reader.Ready();
        await owner.Kill();
        await Scan(area.Root);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(path, "0")));
        Assert.True(File.Exists(Path.Combine(path, ShellStagingGuard.OwnershipName)));
        area.AssertSentinel();
        await reader.Kill();
        await Scan(area.Root);
        Assert.False(Directory.Exists(path));
        await Scan(area.Root);
        await Transfer(area.Root);
        area.AssertSentinel();
    }

    [Theory]
    [InlineData("truncated-header", false)]
    [InlineData("unsupported-version", false)]
    [InlineData("operation-id", false)]
    [InlineData("corrupt-record", false)]
    [InlineData("missing-journal", false)]
    [InlineData("unknown-object", false)]
    [InlineData("replaced-file", false)]
    [InlineData("operation-link", false)]
    [InlineData("operation-junction", false)]
    [InlineData("denied-access", false)]
    [InlineData("missing-file", true)]
    [InlineData("torn-tail", true)]
    public async Task FreshScanner_HandlesDamagedOperationsWithoutBlockingValidPeers(string damage, bool removed)
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        using var owner = new CrashChild(area.Root, "kill:reader-open");
        await owner.Ready();
        string path = Assert.Single(Directory.GetDirectories(area.Root));
        await owner.Kill();
        string journal = Path.Combine(path, ShellStagingGuard.OwnershipName);
        string file = Path.Combine(path, "0");
        FileSecurity? permissions = null;
        switch (damage)
        {
            case "truncated-header": File.WriteAllBytes(journal, [1, 2, 3]); break;
            case "unsupported-version": ChangeHeader(journal, changeVersion: true); break;
            case "operation-id": ChangeHeader(journal, changeVersion: false); break;
            case "corrupt-record":
                byte[] bytes = File.ReadAllBytes(journal);
                bytes[^1] ^= 1;
                File.WriteAllBytes(journal, bytes);
                break;
            case "missing-journal": File.Delete(journal); break;
            case "unknown-object": File.WriteAllText(Path.Combine(path, "foreign"), "foreign"); break;
            case "replaced-file":
                File.Move(file, Path.Combine(area.Controls, "original"));
                File.WriteAllText(file, "foreign");
                break;
            case "operation-link":
            case "operation-junction":
                Directory.Move(path, Path.Combine(area.Controls, "original-operation"));
                if (damage == "operation-link") { Directory.CreateSymbolicLink(path, area.Controls); }
                else
                {
                    var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
                    { UseShellExecute = false, CreateNoWindow = true };
                    start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J");
                    start.ArgumentList.Add(path); start.ArgumentList.Add(area.Controls);
                    using Process link = Process.Start(start)!;
                    await link.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    Assert.Equal(0, link.ExitCode);
                }
                break;
            case "denied-access":
                var info = new FileInfo(journal);
                permissions = info.GetAccessControl();
                FileSecurity denied = info.GetAccessControl();
                using (WindowsIdentity user = WindowsIdentity.GetCurrent())
                    denied.AddAccessRule(new FileSystemAccessRule(user.User!, FileSystemRights.ReadData, AccessControlType.Deny));
                info.SetAccessControl(denied);
                break;
            case "missing-file": File.Delete(file); break;
            case "torn-tail":
                using (var stream = new FileStream(journal, FileMode.Open, FileAccess.Write))
                {
                    stream.SetLength(stream.Length - 1);
                    stream.Flush(flushToDisk: true);
                }
                break;
        }
        string valid;
        using (var peer = ShellStagingGuard.CreateAt(area.Root))
        {
            valid = peer.DirectoryPath;
            await ShellStagingDeletionTests.Write(peer);
        }
        try
        {
            await Scan(area.Root);
            Assert.Equal(!removed, Directory.Exists(path));
            Assert.False(Directory.Exists(valid));
            if (!removed && damage is not ("operation-link" or "operation-junction"))
                Assert.Equal(damage == "replaced-file" ? "foreign" : "hello", File.ReadAllText(file));
            if (damage == "unknown-object") { Assert.Equal("foreign", File.ReadAllText(Path.Combine(path, "foreign"))); }
            await Scan(area.Root);
            await Transfer(area.Root);
            Assert.Equal(!removed, Directory.Exists(path));
            area.AssertSentinel();
        }
        finally
        {
            if (permissions is not null)
            {
                permissions.SetSecurityDescriptorBinaryForm(permissions.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
                new FileInfo(journal).SetAccessControl(permissions);
            }
        }
    }

    private static void ChangeHeader(string path, bool changeVersion)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int length = BitConverter.ToInt32(bytes, 0);
        using var reader = new BinaryReader(new MemoryStream(bytes, 4, length));
        reader.ReadString();
        int versionOffset = 4 + (int)reader.BaseStream.Position;
        if (changeVersion) { BitConverter.GetBytes(999).CopyTo(bytes, versionOffset); }
        else { bytes[versionOffset + 5] ^= 1; } // Skip version and state; change the operation GUID.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(new byte[32]);
        hash.AppendData(bytes.AsSpan(4, length));
        hash.GetHashAndReset().CopyTo(bytes, 4 + length);
        File.WriteAllBytes(path, bytes);
    }

    [Fact]
    public async Task KilledReaderOwner_IsRecoveredByANewProcessAndAllowsAnotherTransfer()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        using var owner = new CrashChild(area.Root, "kill:reader-open");
        await owner.Ready();
        string path = Assert.Single(Directory.GetDirectories(area.Root));
        // A live reader and journal owner must survive another instance's startup scan.
        await Scan(area.Root);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(path, "0")));
        area.AssertSentinel();
        await owner.Kill();
        await Scan(area.Root);
        Assert.False(Directory.Exists(path));
        await Scan(area.Root);
        await Transfer(area.Root);
        area.AssertSentinel();
    }

    private static async Task Scan(string root)
    {
        using var scanner = new CrashChild(root, "scan");
        await scanner.Succeed();
    }

    private static async Task Transfer(string root)
    {
        using var transfer = new CrashChild(root, "transfer");
        await transfer.Succeed();
    }

    private sealed class CrashChild : IDisposable
    {
        private Process Process { get; }

        internal CrashChild(string path, string mode)
        {
            string executable = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(a => a.Key == "StagingCrashStubPath").Value!;
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add(path);
            start.ArgumentList.Add(mode);
            Process = Process.Start(start)!;
        }

        internal async Task Ready()
        {
            string? signal = await Process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(signal == "ready", "Crash fixture did not reach its requested boundary: " +
                (Process.HasExited ? await Process.StandardError.ReadToEndAsync() : signal));
        }

        internal async Task Kill()
        {
            Assert.False(Process.HasExited);
            Process.Kill(entireProcessTree: true);
            await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.NotEqual(0, Process.ExitCode);
        }

        internal async Task Succeed()
        {
            await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(Process.ExitCode == 0, await Process.StandardError.ReadToEndAsync());
        }

        public void Dispose()
        {
            if (!Process.HasExited) { Process.Kill(entireProcessTree: true); Process.WaitForExit(5000); }
            Process.Dispose();
        }
    }
}
