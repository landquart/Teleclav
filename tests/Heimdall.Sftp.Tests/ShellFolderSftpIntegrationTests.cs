/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.Security.Cryptography;
using Heimdall.Core.Ssh;
using Heimdall.Sftp;
using Heimdall.Ssh;

namespace Heimdall.Sftp.Tests;

/// <summary>Opt-in test against an isolated local OpenSSH server, using its pinned host key.</summary>
public sealed class ShellFolderSftpIntegrationTests
{
    [LocalSftpFact]
    public async Task FolderPlanAndBoundedDownload_PreserveTreeAndHashes()
    {
        int port = int.Parse(Environment.GetEnvironmentVariable("TELECLAV_TEST_SFTP_PORT")!);
        string key = Environment.GetEnvironmentVariable("TELECLAV_TEST_SFTP_KEY")!;
        string fingerprint = Environment.GetEnvironmentVariable("TELECLAV_TEST_SFTP_HOST_KEY")!;
        string testRoot = Directory.CreateTempSubdirectory("teleclav-sftp-integration-").FullName;
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(testRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        string remoteRoot = Path.Combine(testRoot, "проект");
        string localRoot = Path.Combine(testRoot, "download");
        try
        {
            Directory.CreateDirectory(Path.Combine(remoteRoot, "вложенная", "empty"));
            byte[] data = new byte[1024 * 1024];
            RandomNumberGenerator.Fill(data);
            await File.WriteAllBytesAsync(Path.Combine(remoteRoot, "вложенная", "тест file.bin"), data);
            await File.WriteAllBytesAsync(Path.Combine(remoteRoot, "zero"), []);
            using var browser = new SftpBrowser();
            await browser.ConnectAsync(new SshConnectionParams
            {
                Host = "127.0.0.1", Port = port, Username = Environment.GetEnvironmentVariable("TELECLAV_TEST_SFTP_USER") ?? Environment.UserName, KeyPath = key,
            }, new HostKeyStore(), new PinnedFingerprintVerifier("127.0.0.1", port, fingerprint));
            var roots = await browser.ListDirectoryStrictAsync(testRoot);
            SftpFileInfo root = Assert.Single(roots, item => item.Name == "проект");
            var plan = await ShellFolderPlanner.PlanAsync([root], browser.ListDirectoryStrictAsync);
            Assert.Equal(5, plan.Count);
            Assert.Contains(plan, item => item.RelativePath == "проект/вложенная/empty" && item.Kind == RemoteDownloadOpKind.MakeDirectory);
            foreach (var item in plan)
            {
                string target = Path.Combine(localRoot, item.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (item.Kind == RemoteDownloadOpKind.MakeDirectory) { Directory.CreateDirectory(target); }
                else
                {
                    await browser.DownloadFileWithinLimitAsync(item.RemotePath, target, item.Size);
                    Assert.Equal(SHA256.HashData(await File.ReadAllBytesAsync(item.RemotePath)), SHA256.HashData(await File.ReadAllBytesAsync(target)));
                }
            }
            Assert.True(Directory.Exists(Path.Combine(localRoot, "проект", "вложенная", "empty")));
            string oversized = Path.Combine(testRoot, "over-limit");
            await Assert.ThrowsAsync<IOException>(() => browser.DownloadFileWithinLimitAsync(
                Path.Combine(remoteRoot, "вложенная", "тест file.bin"), oversized, 0));
            Assert.False(File.Exists(oversized));
            string invalidName = Path.Combine(remoteRoot, "line\nbreak");
            await File.WriteAllTextAsync(invalidName, "x");
            await Assert.ThrowsAsync<IOException>(() => browser.ListDirectoryStrictAsync(remoteRoot));
            File.Delete(invalidName);
            string linked = Path.Combine(remoteRoot, "link");
            File.CreateSymbolicLink(linked, Path.Combine(remoteRoot, "zero"));
            await Assert.ThrowsAsync<IOException>(() => ShellFolderPlanner.PlanAsync([root], browser.ListDirectoryStrictAsync));
            await Assert.ThrowsAsync<IOException>(() => browser.DownloadFileWithinLimitAsync(linked, Path.Combine(testRoot, "wrong"), 0));
            Assert.False(File.Exists(Path.Combine(testRoot, "wrong")));
        }
        finally { Directory.Delete(testRoot, recursive: true); }
    }
}

public sealed class LocalSftpFactAttribute : FactAttribute
{
    public LocalSftpFactAttribute()
    {
        if (OperatingSystem.IsWindows()
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TELECLAV_TEST_SFTP_PORT"))
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TELECLAV_TEST_SFTP_KEY"))
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TELECLAV_TEST_SFTP_HOST_KEY")))
            Skip = "Requires an isolated local OpenSSH server and explicitly pinned host key.";
    }
}
