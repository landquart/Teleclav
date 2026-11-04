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
    public async Task UploadTree_NoClobberAndCancelledRetry_PreserveHashes()
    {
        const string host = "127.0.0.1";
        int port = int.Parse(Environment.GetEnvironmentVariable("TELECLAV_TEST_SFTP_PORT")!);
        string root = Directory.CreateTempSubdirectory("teleclav-upload-integration-").FullName;
        try
        {
            string source = Directory.CreateDirectory(Path.Combine(root, "проект")).FullName;
            Directory.CreateDirectory(Path.Combine(source, "nested", "empty"));
            string local = Path.Combine(source, "nested", "тест.bin");
            byte[] bytes = RandomNumberGenerator.GetBytes(1024 * 1024);
            await File.WriteAllBytesAsync(local, bytes);
            string destination = Directory.CreateDirectory(Path.Combine(root, "destination")).FullName;

            Task ConnectAsync(SftpBrowser client) => client.ConnectAsync(new SshConnectionParams
            {
                Host = host,
                Port = port,
                Username = Environment.GetEnvironmentVariable("TELECLAV_TEST_SFTP_USER") ?? Environment.UserName,
                KeyPath = Environment.GetEnvironmentVariable("TELECLAV_TEST_SFTP_KEY")!,
            }, new HostKeyStore(), new PinnedFingerprintVerifier(host, port,
                Environment.GetEnvironmentVariable("TELECLAV_TEST_SFTP_HOST_KEY")!));

            using var browser = new SftpBrowser();
            await ConnectAsync(browser);

            static LocalUploadEntry Entry(string path) => new(path, Path.GetFileName(path), Directory.Exists(path));
            var plan = await RemoteUploadTreePlanner.PlanAsync([Entry(source)], destination,
                (path, _) => Task.FromResult<IReadOnlyList<LocalUploadEntry>>(
                    Directory.EnumerateFileSystemEntries(path).Select(Entry).ToArray()));
            foreach (var op in plan)
            {
                if (op.Kind == RemoteUploadOpKind.MakeDirectory) { await browser.CreateDirectoryAsync(op.RemotePath); }
                else { await browser.UploadFileAsync(op.LocalPath, op.RemotePath, overwrite: false); }
            }
            Assert.True(Directory.Exists(Path.Combine(destination, "проект", "nested", "empty")));
            string uploaded = Path.Combine(destination, "проект", "nested", "тест.bin");
            Assert.Equal(SHA256.HashData(bytes), SHA256.HashData(await File.ReadAllBytesAsync(uploaded)));
            string different = Path.Combine(root, "different.bin");
            await File.WriteAllBytesAsync(different, [1, 2, 3]);
            await Assert.ThrowsAsync<RemoteDestinationExistsException>(
                () => browser.UploadFileAsync(different, uploaded, overwrite: false));
            Assert.Equal(SHA256.HashData(bytes), SHA256.HashData(await File.ReadAllBytesAsync(uploaded)));

            string outside = Path.Combine(root, "must-not-appear");
            string link = Path.Combine(destination, "dangling-link");
            File.CreateSymbolicLink(link, outside);
            await Assert.ThrowsAsync<RemoteDestinationExistsException>(
                () => browser.UploadFileAsync(different, link, overwrite: false));
            Assert.Equal(outside, new FileInfo(link).LinkTarget);
            Assert.False(File.Exists(outside));

            string retry = Path.Combine(destination, "retry.bin");
            using var cancellation = new CancellationTokenSource();
            void CancelAfterBytes(SftpTransferProgress progress)
            {
                if (progress.IsUpload) { cancellation.Cancel(); }
            }
            browser.TransferProgress += CancelAfterBytes;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => browser.UploadFileAsync(local, retry, overwrite: false, cancellation.Token));
            browser.TransferProgress -= CancelAfterBytes;
            Assert.False(File.Exists(retry));
            // The legacy overload must also publish a new file on an SFTP-only account.
            await browser.UploadFileAsync(local, retry);
            Assert.Equal(SHA256.HashData(bytes), SHA256.HashData(await File.ReadAllBytesAsync(retry)));
            using var contender = new SftpBrowser();
            await ConnectAsync(contender);
            string collision = Path.Combine(destination, "collision.bin");
            async Task<bool> PublishAsync(SftpBrowser client, string path)
            {
                try { await client.UploadFileAsync(path, collision, overwrite: false); }
                catch (RemoteDestinationExistsException) { return false; }
                return true;
            }

            bool[] commits = await Task.WhenAll(PublishAsync(browser, local), PublishAsync(contender, different));
            Assert.Single(commits, published => published);
            string winner = commits[0] ? local : different;
            Assert.Equal(SHA256.HashData(await File.ReadAllBytesAsync(winner)),
                SHA256.HashData(await File.ReadAllBytesAsync(collision)));
            Assert.Empty(Directory.EnumerateFiles(destination, "*.part", SearchOption.AllDirectories));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [LocalSftpFact]
    public Task FolderPlanAndBoundedDownload_PreserveTreeAndHashes()
        => VerifyFolderDownloadAsync(Environment.GetEnvironmentVariable("TELECLAV_TEST_SFTP_USER") ?? Environment.UserName,
            Environment.GetEnvironmentVariable("TELECLAV_TEST_SFTP_KEY")!, null);

    [LocalSftpPasswordFact]
    public Task PasswordAuthentication_PreservesTreeAndHashes()
        => VerifyFolderDownloadAsync(Environment.GetEnvironmentVariable("TELECLAV_TEST_SFTP_PASSWORD_USER")!,
            null, Environment.GetEnvironmentVariable("TELECLAV_TEST_SFTP_PASSWORD")!);

    private static async Task VerifyFolderDownloadAsync(string user, string? key, string? password)
    {
        int port = int.Parse(Environment.GetEnvironmentVariable("TELECLAV_TEST_SFTP_PORT")!);
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
            await File.WriteAllTextAsync(Path.Combine(remoteRoot, "readme.txt"), "Teleclav SFTP");
            using var browser = new SftpBrowser();
            await browser.ConnectAsync(new SshConnectionParams
            {
                Host = "127.0.0.1",
                Port = port,
                Username = user,
                KeyPath = key,
                Password = password,
            }, new HostKeyStore(), new PinnedFingerprintVerifier("127.0.0.1", port, fingerprint));
            var roots = await browser.ListDirectoryStrictAsync(testRoot);
            SftpFileInfo root = Assert.Single(roots, item => item.Name == "проект");
            var plan = await ShellFolderPlanner.PlanAsync([root], browser.ListDirectoryStrictAsync);
            Assert.Equal(6, plan.Count);
            Assert.Contains(plan, item => item.RelativePath == "проект/вложенная/empty" && item.Kind == RemoteDownloadOpKind.MakeDirectory);
            foreach (var item in plan)
            {
                string target = Path.Combine(localRoot, item.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (item.Kind == RemoteDownloadOpKind.MakeDirectory) { Directory.CreateDirectory(target); }
                else
                {
                    await browser.DownloadFileWithinLimitAsync(item.RemotePath, target, item.Size);
                    Assert.Equal(SHA256.HashData(await File.ReadAllBytesAsync(item.RemotePath)), SHA256.HashData(await File.ReadAllBytesAsync(target)));
                    using var stream = new MemoryStream();
                    await browser.DownloadToStreamWithinLimitAsync(item.RemotePath, stream, item.Size, item.LastModified, default);
                    Assert.True(stream.CanWrite); // The browser never owns the caller's destination.
                    Assert.Equal(SHA256.HashData(await File.ReadAllBytesAsync(target)), SHA256.HashData(stream.ToArray()));
                }
            }
            Assert.True(Directory.Exists(Path.Combine(localRoot, "проект", "вложенная", "empty")));
            var plannedText = Assert.Single(plan, item => item.RelativePath.EndsWith("/readme.txt", StringComparison.Ordinal));
            await File.WriteAllTextAsync(plannedText.RemotePath, "Modified file");
            File.SetLastWriteTimeUtc(plannedText.RemotePath, plannedText.LastModified.AddMinutes(1));
            using var rejected = new MemoryStream();
            await Assert.ThrowsAsync<IOException>(() => browser.DownloadToStreamWithinLimitAsync(
                plannedText.RemotePath, rejected, plannedText.Size, plannedText.LastModified, default));
            Assert.Equal(0, rejected.Length);

            string nestedPath = Path.Combine(remoteRoot, "вложенная");
            string movedPath = Path.Combine(testRoot, "moved");
            Directory.Move(nestedPath, movedPath);
            Directory.CreateSymbolicLink(nestedPath, movedPath);
            var plannedBinary = Assert.Single(plan, item => item.RelativePath.EndsWith(".bin", StringComparison.Ordinal));
            await Assert.ThrowsAsync<IOException>(() => browser.DownloadToStreamWithinLimitAsync(
                plannedBinary.RemotePath, rejected, plannedBinary.Size, plannedBinary.LastModified, default));
            Assert.Equal(0, rejected.Length);
            Directory.Delete(nestedPath);
            Directory.Move(movedPath, nestedPath);

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

public class LocalSftpFactAttribute : FactAttribute
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

public sealed class LocalSftpPasswordFactAttribute : LocalSftpFactAttribute
{
    public LocalSftpPasswordFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TELECLAV_TEST_SFTP_PASSWORD_USER"))
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TELECLAV_TEST_SFTP_PASSWORD")))
            Skip ??= "Requires the isolated OpenSSH password account.";
    }
}
