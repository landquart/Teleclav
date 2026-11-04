/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Heimdall.App.Views;

namespace Heimdall.App.Tests;

public sealed class ShellStagingRecoveryProcessTests
{
    [Theory]
    [MemberData(nameof(ShellStagingProtocolTests.CrashPoints), MemberType = typeof(ShellStagingProtocolTests))]
    public async Task Discovery_RecoversAfterRealProcessExitAtEveryProtocolBoundary(int point)
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        using var child = new RecoveryChild(area.Root, point.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(37, await child.Exit());
        Assert.Single(Directory.GetDirectories(area.Root));
        ShellStagingRecovery.Recover(area.Root);
        Assert.Empty(Directory.GetDirectories(area.Root));
        ShellStagingRecovery.Recover(area.Root);
        Assert.Empty(Directory.GetDirectories(area.Root));
        area.AssertSentinel();
    }

    [Fact]
    public async Task Discovery_CannotStealAnotherProcessesLiveOperation()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        using var child = new RecoveryChild(area.Root, "hold");
        string? path = await child.Process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.NotNull(path);
        ShellStagingRecovery.Recover(area.Root);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(path, "0")));
        child.Process.StandardInput.WriteLine();
        child.Process.StandardInput.Flush();
        Assert.Equal(0, await child.Exit());
        ShellStagingRecovery.Recover(area.Root);
        Assert.False(Directory.Exists(path));
        area.AssertSentinel();
    }

    [Fact]
    public async Task Discovery_TwoRecoveryProcessesSafelyCompete()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var area = new DeletionArea();
        for (int i = 0; i < 4; i++)
        {
            using var operation = ShellStagingGuard.CreateAt(area.Root);
            await ShellStagingDeletionTests.Write(operation);
        }
        using var first = new RecoveryChild(area.Root, "scan");
        using var second = new RecoveryChild(area.Root, "scan");
        Assert.Equal(0, await first.Exit());
        Assert.Equal(0, await second.Exit());
        // A competing open may transiently block disposition. The next bounded pass must finish it.
        ShellStagingRecovery.Recover(area.Root);
        Assert.Empty(Directory.GetDirectories(area.Root));
        area.AssertSentinel();
    }

    private sealed class RecoveryChild : IDisposable
    {
        internal Process Process { get; }
        internal RecoveryChild(string root, string mode)
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
            start.ArgumentList.Add(root);
            start.ArgumentList.Add(mode);
            Process = Process.Start(start)!;
        }
        internal async Task<int> Exit()
        {
            await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(Process.ExitCode is 0 or 37, await Process.StandardError.ReadToEndAsync());
            return Process.ExitCode;
        }
        public void Dispose()
        {
            if (!Process.HasExited) { Process.Kill(entireProcessTree: true); Process.WaitForExit(5000); }
            Process.Dispose();
        }
    }
}
