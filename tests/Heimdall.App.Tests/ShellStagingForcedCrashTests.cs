/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Heimdall.App.Views;

namespace Heimdall.App.Tests;

public sealed class ShellStagingForcedCrashTests
{
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
