/*
 * Copyright 2026 Julien Bombled
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

namespace Heimdall.Sftp.Tests;

public sealed class LocalUploadSourceTests
{
    [Fact]
    public async Task SftpBrowserUploadFileAsync_MissingLocalFile_ThrowsClearValidationError()
    {
        using var browser = new SftpBrowser();
        string missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.txt");

        var ex = await Assert.ThrowsAsync<LocalUploadFileValidationException>(
            () => browser.UploadFileAsync(missingPath, "/tmp/missing.txt"));

        Assert.Equal(LocalUploadFileValidationFailure.Missing, ex.Failure);
        Assert.Contains("does not exist", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SftpBrowserUploadFileAsync_DirectoryLocalPath_ThrowsClearValidationError()
    {
        string directory = CreateTempDirectory();
        try
        {
            using var browser = new SftpBrowser();

            var ex = await Assert.ThrowsAsync<LocalUploadFileValidationException>(
                () => browser.UploadFileAsync(directory, "/tmp/folder"));

            Assert.Equal(LocalUploadFileValidationFailure.NotRegularFile, ex.Failure);
            Assert.Contains("not a regular file", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FtpBrowserUploadFileAsync_MissingLocalFile_ThrowsClearValidationError()
    {
        using var browser = new FtpBrowser();
        string missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.txt");

        var ex = await Assert.ThrowsAsync<LocalUploadFileValidationException>(
            () => browser.UploadFileAsync(missingPath, "/tmp/missing.txt"));

        Assert.Equal(LocalUploadFileValidationFailure.Missing, ex.Failure);
        Assert.Contains("does not exist", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FtpBrowserUploadFileAsync_DirectoryLocalPath_ThrowsClearValidationError()
    {
        string directory = CreateTempDirectory();
        try
        {
            using var browser = new FtpBrowser();

            var ex = await Assert.ThrowsAsync<LocalUploadFileValidationException>(
                () => browser.UploadFileAsync(directory, "/tmp/folder"));

            Assert.Equal(LocalUploadFileValidationFailure.NotRegularFile, ex.Failure);
            Assert.Contains("not a regular file", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SftpUpload_RefusesFileAndAncestorLinksBeforeContactingServer(bool ancestor)
    {
        if (!OperatingSystem.IsWindows()) { return; }

        string directory = CreateTempDirectory();
        try
        {
            string control = Directory.CreateDirectory(Path.Combine(directory, "control")).FullName;
            string original = Path.Combine(control, "data.txt");
            File.WriteAllText(original, "control");
            string alias = Path.Combine(directory, "alias");
            string source;
            if (ancestor)
            {
                Directory.CreateSymbolicLink(alias, control);
                source = Path.Combine(alias, "data.txt");
            }
            else
            {
                File.CreateSymbolicLink(alias, original);
                source = alias;
            }

            using var browser = new SftpBrowser();
            await Assert.ThrowsAnyAsync<IOException>(
                () => browser.UploadFileAsync(source, "/tmp/unused", overwrite: false));
            Assert.Equal("control", File.ReadAllText(original));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("file")]
    [InlineData("parent")]
    [InlineData("missing")]
    [InlineData("contents")]
    public void PlannedWindowsSource_RefusesAChangedPathOrContents(string change)
    {
        if (!OperatingSystem.IsWindows()) { return; }

        string directory = CreateTempDirectory();
        try
        {
            string parent = Directory.CreateDirectory(Path.Combine(directory, "parent")).FullName;
            string source = Path.Combine(parent, "source.txt");
            File.WriteAllText(source, "original");
            LocalUploadSnapshot snapshot = LocalUploadSnapshot.Capture(source);

            if (change == "file")
            {
                File.Move(source, Path.Combine(parent, "original.txt"));
                File.WriteAllText(source, "original");
            }
            else if (change == "parent")
            {
                Directory.Move(parent, parent + "-original");
                Directory.CreateDirectory(parent);
                File.WriteAllText(source, "original");
            }
            else if (change == "missing") { File.Delete(source); }
            else { File.WriteAllText(source, "longer replacement"); }

            Assert.ThrowsAny<IOException>(() =>
            {
                if (OperatingSystem.IsWindows()) { using FileStream refused = snapshot.OpenRead(); }
            });
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void VerifiedWindowsHandle_DeniesWriteAndDeleteWhileStreaming()
    {
        if (!OperatingSystem.IsWindows()) { return; }

        string directory = CreateTempDirectory();
        try
        {
            string source = Path.Combine(directory, "source.txt");
            File.WriteAllText(source, "original");
            LocalUploadSnapshot snapshot = LocalUploadSnapshot.Capture(source);
            using (FileStream stream = snapshot.OpenRead())
            {
                Assert.ThrowsAny<IOException>(() => File.WriteAllText(source, "overwrite"));
                Assert.ThrowsAny<IOException>(() => File.Move(source, source + ".moved"));
                using var reader = new StreamReader(stream);
                Assert.Equal("original", reader.ReadToEnd());
            }
            File.Move(source, source + ".moved");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "HeimdallUploadValidationTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
