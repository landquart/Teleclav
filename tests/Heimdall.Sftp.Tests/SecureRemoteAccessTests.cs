/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.Diagnostics;
using System.Text;
using Heimdall.Sftp;

namespace Heimdall.Sftp.Tests;

public sealed class SecureRemoteAccessTests
{
    [LinuxFact]
    public async Task FileAndDirectoryLinks_AreRejectedWithoutReadingTheirTargets()
    {
        string root = Directory.CreateTempSubdirectory("secure-remote-test-").FullName;
        try
        {
            string outside = Path.Combine(root, "outside");
            string tree = Path.Combine(root, "tree");
            Directory.CreateDirectory(outside);
            Directory.CreateDirectory(tree);
            await File.WriteAllTextAsync(Path.Combine(outside, "secret"), "do-not-read");
            File.CreateSymbolicLink(Path.Combine(tree, "file-link"), Path.Combine(outside, "secret"));
            Directory.CreateSymbolicLink(Path.Combine(tree, "dir-link"), outside);
            var file = await Run("read", Path.Combine(tree, "file-link"), "100");
            Assert.NotEqual(0, file.ExitCode);
            Assert.DoesNotContain("do-not-read", file.Output);
            var directory = await Run("list", Path.Combine(tree, "dir-link"), "0");
            Assert.NotEqual(0, directory.ExitCode);
            Assert.DoesNotContain("secret", directory.Output);
            var nested = await Run("read", Path.Combine(tree, "dir-link", "secret"), "100");
            Assert.NotEqual(0, nested.ExitCode);
            Assert.DoesNotContain("do-not-read", nested.Output);
        }
        finally { Directory.Delete(root, true); }
    }

    [LinuxFact]
    public async Task OrdinaryFile_IsReadAndSizeLimitIsEnforced()
    {
        string root = Directory.CreateTempSubdirectory("secure-remote-test-").FullName;
        try
        {
            string path = Path.Combine(root, "пробел.txt");
            await File.WriteAllTextAsync(path, "hello");
            var success = await Run("read", path, "5");
            Assert.Equal(0, success.ExitCode);
            Assert.Equal("hello", success.Output);
            var tooSmall = await Run("read", path, "4");
            Assert.NotEqual(0, tooSmall.ExitCode);
            Assert.Empty(tooSmall.Output);
        }
        finally { Directory.Delete(root, true); }
    }

    [LinuxFact]
    public async Task ConcurrentReplacementWithLink_NeverReadsTheLinkTarget()
    {
        string root = Directory.CreateTempSubdirectory("secure-remote-race-").FullName;
        string secret = Path.Combine(root, "secret");
        string current = Path.Combine(root, "current");
        using CancellationTokenSource stop = new();
        try
        {
            await File.WriteAllTextAsync(secret, "secret-data");
            await File.WriteAllTextAsync(current, "safe-data");
            Task swapper = Task.Run(() =>
            {
                int iteration = 0;
                while (!stop.IsCancellationRequested)
                {
                    string next = Path.Combine(root, "next");
                    if ((iteration++ & 1) == 0) { File.CreateSymbolicLink(next, secret); }
                    else { File.WriteAllText(next, "safe-data"); }
                    File.Move(next, current, overwrite: true);
                }
            });
            try
            {
                for (int i = 0; i < 50; i++)
                {
                    var result = await Run("read", current, "100");
                    Assert.DoesNotContain("secret-data", result.Output);
                    if (result.ExitCode == 0) { Assert.Equal("safe-data", result.Output); }
                }
            }
            finally { stop.Cancel(); await swapper; }
        }
        finally { Directory.Delete(root, true); }
    }

    [LinuxFact]
    public async Task DirectoryListing_ReportsLinksWithoutFollowingThem()
    {
        string root = Directory.CreateTempSubdirectory("secure-remote-list-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "empty"));
            File.CreateSymbolicLink(Path.Combine(root, "link"), "/etc/passwd");
            var result = await Run("list", root, "0");
            Assert.Equal(0, result.ExitCode);
            using var json = System.Text.Json.JsonDocument.Parse(result.Output);
            Assert.Contains(json.RootElement.EnumerateArray(), e => e[0].GetString() == "empty" && e[1].GetString() == "directory");
            Assert.Contains(json.RootElement.EnumerateArray(), e => e[0].GetString() == "link" && e[1].GetString() == "link");
        }
        finally { Directory.Delete(root, true); }
    }

    [LinuxFact]
    public async Task Command_DoesNotInterpretPathAsShellCode()
    {
        string root = Directory.CreateTempSubdirectory("secure-remote-shell-").FullName;
        try
        {
            string path = Path.Combine(root, "x'; touch INJECTED; echo '");
            await File.WriteAllTextAsync(path, "safe");
            var start = new ProcessStartInfo("/bin/sh") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = root };
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(SecureRemoteAccess.BuildCommand("read", path, 4));
            var result = await RunProcess(start);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("safe", result.Output);
            Assert.False(File.Exists(Path.Combine(root, "INJECTED")));
        }
        finally { Directory.Delete(root, true); }
    }

    [LinuxFact]
    public async Task Helper_DoesNotSendDataUntilRequested()
    {
        string root = Directory.CreateTempSubdirectory("secure-remote-flow-").FullName;
        try
        {
            string path = Path.Combine(root, "data");
            await File.WriteAllTextAsync(path, "hello");
            var start = new ProcessStartInfo("python3") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(SecureRemoteAccess.Script);
            start.ArgumentList.Add("read");
            start.ArgumentList.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(path)));
            start.ArgumentList.Add("5");
            using var process = Process.Start(start)!;
            byte[] header = new byte[4];
            Task read = process.StandardOutput.BaseStream.ReadExactlyAsync(header).AsTask();
            await Task.Delay(100);
            Assert.False(read.IsCompleted);
            await process.StandardInput.BaseStream.WriteAsync(new byte[] { (byte)'R' });
            await process.StandardInput.BaseStream.FlushAsync();
            await read.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(5u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header));
            byte[] data = new byte[5];
            await process.StandardOutput.BaseStream.ReadExactlyAsync(data);
            await process.StandardInput.BaseStream.WriteAsync(new byte[] { (byte)'R' });
            await process.StandardInput.BaseStream.FlushAsync();
            await process.StandardOutput.BaseStream.ReadExactlyAsync(header);
            Assert.Equal(0u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header));
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
            Assert.Equal("hello", Encoding.UTF8.GetString(data));
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task<(int ExitCode, string Output)> Run(string action, string path, string limit)
    {
        var start = new ProcessStartInfo("python3") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(SecureRemoteAccess.Script);
        start.ArgumentList.Add(action);
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(path)));
        start.ArgumentList.Add(limit);
        return await RunProcess(start);
    }

    private static async Task<(int ExitCode, string Output)> RunProcess(ProcessStartInfo start)
    {
        using var process = Process.Start(start)!;
        Task<string> error = process.StandardError.ReadToEndAsync();
        using MemoryStream data = new();
        try
        {
            byte[] header = new byte[4];
            byte[] buffer = new byte[65536];
            while (true)
            {
                await process.StandardInput.BaseStream.WriteAsync(new byte[] { (byte)'R' });
                await process.StandardInput.BaseStream.FlushAsync();
                await process.StandardOutput.BaseStream.ReadExactlyAsync(header);
                uint size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header);
                if (size > buffer.Length) { throw new IOException("Invalid frame"); }
                if (size == 0) { break; }
                await process.StandardOutput.BaseStream.ReadExactlyAsync(buffer.AsMemory(0, (int)size));
                await data.WriteAsync(buffer.AsMemory(0, (int)size));
            }
        }
        catch (IOException) { }
        process.StandardInput.Close();
        await process.WaitForExitAsync();
        _ = await error;
        return (process.ExitCode, Encoding.UTF8.GetString(data.ToArray()));
    }
}

public sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute()
    {
        if (!OperatingSystem.IsLinux()) { Skip = "The secure remote helper requires Linux."; }
    }
}
