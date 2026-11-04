/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using Heimdall.App.Views;
using Heimdall.Sftp;

namespace Heimdall.App.Tests;

public sealed class ShellVirtualFileDragTests
{
    [Fact]
    public async Task GetContents_ReturnsStreamBeforeRemoteDownloadCompletes()
    {
        IRemoteBrowser browser = DispatchProxy.Create<IRemoteBrowser, BrowserProxy>();
        BrowserProxy proxy = (BrowserProxy)browser;
        proxy.ProvideContent = true;
        proxy.PauseDownload = true;
        using ShellVirtualFileDrag drag = new(browser, FileEntry());
        Task<STGMEDIUM> call = Task.Run(() =>
        {
            FORMATETC request = Format("FileContents", 0, TYMED.TYMED_ISTREAM);
            drag.GetData(ref request, out STGMEDIUM result);
            return result;
        });
        try
        {
            await proxy.DownloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Same(call, await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(2))));
        }
        finally
        {
            proxy.ContinueDownload.TrySetResult();
            STGMEDIUM medium = await call.WaitAsync(TimeSpan.FromSeconds(20));
            var stream = Assert.IsType<ShellReadStream>(Marshal.GetObjectForIUnknown(medium.unionmember));
            Marshal.Release(medium.unionmember);
            stream.Dispose();
        }
    }

    [Fact]
    public async Task IssuedStream_RemainsUsableAfterSynchronousDragReturns()
    {
        IRemoteBrowser browser = DispatchProxy.Create<IRemoteBrowser, BrowserProxy>();
        BrowserProxy proxy = (BrowserProxy)browser;
        proxy.ProvideContent = true;
        proxy.PauseDownload = true;
        using ShellVirtualFileDrag drag = new(browser, FileEntry());
        FORMATETC request = Format("FileContents", 0, TYMED.TYMED_ISTREAM);
        drag.GetData(ref request, out STGMEDIUM medium);
        var stream = Assert.IsType<ShellReadStream>(Marshal.GetObjectForIUnknown(medium.unionmember));
        try
        {
            await proxy.DownloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            drag.Dispose();
            proxy.ContinueDownload.TrySetResult();
            byte[] data = new byte[5];
            stream.Read(data, data.Length, IntPtr.Zero);
            Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(data));
        }
        finally
        {
            proxy.ContinueDownload.TrySetResult();
            Marshal.Release(medium.unionmember);
            stream.Dispose();
        }
    }

    [Fact]
    public async Task DisposeAndReadWhileDownloadPending_DoNotHang()
    {
        IRemoteBrowser browser = DispatchProxy.Create<IRemoteBrowser, BrowserProxy>();
        BrowserProxy proxy = (BrowserProxy)browser;
        proxy.ProvideContent = true;
        proxy.PauseDownload = true;
        using ShellVirtualFileDrag drag = new(browser, FileEntry());
        FORMATETC request = Format("FileContents", 0, TYMED.TYMED_ISTREAM);
        drag.GetData(ref request, out STGMEDIUM medium);
        var stream = Assert.IsType<ShellReadStream>(Marshal.GetObjectForIUnknown(medium.unionmember));
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await proxy.DownloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Task<Exception> read = Task.Run(() =>
            {
                reading.SetResult();
                return Record.Exception(() => stream.Read(new byte[5], 5, IntPtr.Zero));
            });
            await reading.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Run(stream.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
            Exception failure = await read.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(failure is COMException or ObjectDisposedException);
        }
        finally
        {
            proxy.ContinueDownload.TrySetResult();
            Marshal.Release(medium.unionmember);
            stream.Dispose();
        }
    }

    [Fact]
    public void CancelledAsyncDrag_CleansUpAfterReturnWithoutEndOperation()
    {
        using ShellVirtualFileDrag drag = CreateDrag();
        int closed = 0;
        drag.Closed = () => Interlocked.Increment(ref closed);
        Assert.Equal(0, drag.SetAsyncMode(true));
        Assert.Equal(0, drag.StartOperation(IntPtr.Zero));
        drag.Cancel();
        drag.Dispose();
        Assert.Equal(1, closed);
        Assert.Equal(0, drag.EndOperation(0, IntPtr.Zero, 0));
        Assert.Equal(1, closed);
    }

    [Fact]
    public void CancellationAfterGetData_DoesNotInvalidateTheIssuedStream()
    {
        IRemoteBrowser browser = DispatchProxy.Create<IRemoteBrowser, BrowserProxy>();
        ((BrowserProxy)browser).ProvideContent = true;
        using ShellVirtualFileDrag drag = new(browser, FileEntry());
        Assert.Equal(0, drag.SetAsyncMode(true));
        Assert.Equal(0, drag.StartOperation(IntPtr.Zero));
        FORMATETC contents = Format("FileContents", 0, TYMED.TYMED_ISTREAM);
        drag.GetData(ref contents, out STGMEDIUM medium);
        var stream = Assert.IsType<ShellReadStream>(Marshal.GetObjectForIUnknown(medium.unionmember));
        stream.Read(new byte[5], 5, IntPtr.Zero);
        stream.Seek(0, 0, IntPtr.Zero);
        string directory = Path.GetDirectoryName(Assert.IsType<string>(((BrowserProxy)browser).LastPath))!;
        try
        {
            drag.Cancel();
            drag.Dispose();
            byte[] bytes = new byte[5];
            stream.Read(bytes, bytes.Length, IntPtr.Zero);
            Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(bytes));
            Assert.True(Directory.Exists(directory));
        }
        finally
        {
            Marshal.Release(medium.unionmember);
            stream.Dispose();
        }
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void Constructor_RejectsDirectories()
    {
        IRemoteBrowser browser = DispatchProxy.Create<IRemoteBrowser, BrowserProxy>();
        SftpFileInfo directory = new("folder", "/folder", RemoteEntryKind.Directory, 0,
            DateTime.UnixEpoch, "", "", "");
        Assert.Throws<ArgumentException>(() => new ShellVirtualFileDrag(browser, directory));
    }

    [Fact]
    public void Formats_OfferOneDescriptorAndOneIndexedStream()
    {
        using ShellVirtualFileDrag drag = CreateDrag();
        FORMATETC descriptor = Format("FileGroupDescriptorW", -1, TYMED.TYMED_HGLOBAL);
        FORMATETC contents = Format("FileContents", 0, TYMED.TYMED_ISTREAM);
        Assert.Equal(0, drag.QueryGetData(ref descriptor));
        Assert.Equal(0, drag.QueryGetData(ref contents));
        contents.lindex = 1;
        Assert.NotEqual(0, drag.QueryGetData(ref contents));
    }

    [Fact]
    public void Formats_MultipleFiles_AcceptsEveryValidIndex()
    {
        IRemoteBrowser browser = DispatchProxy.Create<IRemoteBrowser, BrowserProxy>();
        SftpFileInfo[] entries = [FileEntry(), FileEntry() with { Name = "b.txt", FullPath = "/b.txt" }];
        using ShellVirtualFileDrag drag = new(browser, entries);
        FORMATETC contents = Format("FileContents", 1, TYMED.TYMED_ISTREAM);
        Assert.Equal(0, drag.QueryGetData(ref contents));
        contents.lindex = 2;
        Assert.NotEqual(0, drag.QueryGetData(ref contents));
        contents.lindex = -1;
        Assert.NotEqual(0, drag.QueryGetData(ref contents));
    }

    [Fact]
    public void GetContents_SecondIndex_UsesSecondRemotePath()
    {
        IRemoteBrowser browser = DispatchProxy.Create<IRemoteBrowser, BrowserProxy>();
        BrowserProxy proxy = (BrowserProxy)browser;
        proxy.ProvideContent = true;
        SftpFileInfo[] entries = [FileEntry(), FileEntry() with { Name = "b.txt", FullPath = "/other/b.txt" }];
        using ShellVirtualFileDrag drag = new(browser, entries);
        FORMATETC contents = Format("FileContents", 1, TYMED.TYMED_ISTREAM);
        drag.GetData(ref contents, out STGMEDIUM medium);
        ShellReadStream? stream = null;
        try
        {
            stream = Assert.IsType<ShellReadStream>(Marshal.GetObjectForIUnknown(medium.unionmember));
            byte[] bytes = new byte[5];
            stream.Read(bytes, bytes.Length, IntPtr.Zero);
            Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(bytes));
            Assert.Equal("/other/b.txt", proxy.LastRemotePath);
        }
        finally
        {
            Marshal.Release(medium.unionmember);
            stream?.Dispose();
        }
    }

    [Fact]
    public void EnumerateFormats_AdvertisesDescriptorContentsAndInternalMarker()
    {
        using ShellVirtualFileDrag drag = CreateDrag();
        IEnumFORMATETC enumerator = drag.EnumFormatEtc(DATADIR.DATADIR_GET);
        FORMATETC[] one = new FORMATETC[1];
        List<short> formats = [];
        int[] fetched = new int[1];
        while (enumerator.Next(1, one, fetched) == 0)
        {
            formats.Add(one[0].cfFormat);
        }

        Assert.Contains((short)DataFormats.GetDataFormat("FileGroupDescriptorW").Id, formats);
        Assert.Contains((short)DataFormats.GetDataFormat("FileContents").Id, formats);
        Assert.Contains((short)DataFormats.GetDataFormat(SftpRemoteDragPayload.FormatName).Id, formats);
    }

    [Fact]
    public void EnumerateFormats_Hierarchy_AdvertisesEveryFileIndexAndNoDirectories()
    {
        IRemoteBrowser browser = DispatchProxy.Create<IRemoteBrowser, BrowserProxy>();
        ShellFolderItem[] entries = [
            new(RemoteDownloadOpKind.MakeDirectory, "/folder", "folder", 0),
            new(RemoteDownloadOpKind.DownloadFile, "/folder/a.txt", "folder/a.txt", 5),
            new(RemoteDownloadOpKind.MakeDirectory, "/folder/empty", "folder/empty", 0),
            new(RemoteDownloadOpKind.DownloadFile, "/folder/b.txt", "folder/b.txt", 5)
        ];
        using ShellVirtualFileDrag drag = new(browser, entries);
        IEnumFORMATETC enumerator = drag.EnumFormatEtc(DATADIR.DATADIR_GET);
        FORMATETC[] one = new FORMATETC[1];
        List<int> indices = [];
        int[] fetched = new int[1];
        short contents = (short)DataFormats.GetDataFormat("FileContents").Id;
        while (enumerator.Next(1, one, fetched) == 0)
        {
            if (one[0].cfFormat == contents)
            {
                FORMATETC advertised = one[0];
                Assert.Equal(0, drag.QueryGetData(ref advertised));
                indices.Add(advertised.lindex);
            }
        }

        Assert.Equal(new[] { 1, 3 }, indices);
    }

    [Fact]
    public void GetContents_MissingRemoteFile_ReportsFailure()
    {
        IRemoteBrowser browser = DispatchProxy.Create<IRemoteBrowser, BrowserProxy>();
        using ShellVirtualFileDrag drag = new(browser, FileEntry());
        FORMATETC contents = Format("FileContents", 0, TYMED.TYMED_ISTREAM);
        drag.GetData(ref contents, out STGMEDIUM medium);
        var stream = Assert.IsType<ShellReadStream>(Marshal.GetObjectForIUnknown(medium.unionmember));
        try
        {
            var error = Assert.Throws<COMException>(() => stream.Read(new byte[5], 5, IntPtr.Zero));
            Assert.Equal("The file transfer did not complete.", error.Message);
            Assert.IsType<FileNotFoundException>(drag.TransferError);
        }
        finally
        {
            Marshal.Release(medium.unionmember);
            stream.Dispose();
        }
        string attemptedPath = Assert.IsType<string>(((BrowserProxy)browser).LastPath);
        drag.Dispose();
        Assert.False(Directory.Exists(Path.GetDirectoryName(attemptedPath)));
    }

    [Fact]
    public void GetContents_ReturnsTheDownloadedBytes()
    {
        IRemoteBrowser browser = DispatchProxy.Create<IRemoteBrowser, BrowserProxy>();
        ((BrowserProxy)browser).ProvideContent = true;
        using ShellVirtualFileDrag drag = new(browser, FileEntry());
        FORMATETC contents = Format("FileContents", 0, TYMED.TYMED_ISTREAM);
        drag.GetData(ref contents, out STGMEDIUM medium);
        Assert.Equal(TYMED.TYMED_ISTREAM, medium.tymed);
        ShellReadStream? stream = null;
        try
        {
            stream = Assert.IsType<ShellReadStream>(Marshal.GetObjectForIUnknown(medium.unionmember));
            byte[] bytes = new byte[5];
            stream.Read(bytes, bytes.Length, IntPtr.Zero);
            Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(bytes));
        }
        finally
        {
            Marshal.Release(medium.unionmember);
            stream?.Dispose();
        }
    }

    [Fact]
    public void Hierarchy_AdvertisesDirectoryWithoutFileContents()
    {
        IRemoteBrowser browser = DispatchProxy.Create<IRemoteBrowser, BrowserProxy>();
        ShellFolderItem[] entries = [
            new(RemoteDownloadOpKind.MakeDirectory, "/project", "project", 0),
            new(RemoteDownloadOpKind.MakeDirectory, "/project/empty", "project/empty", 0),
            new(RemoteDownloadOpKind.DownloadFile, "/project/a.txt", "project/a.txt", 5)
        ];
        using ShellVirtualFileDrag drag = new(browser, entries);
        FORMATETC directoryContents = Format("FileContents", 1, TYMED.TYMED_ISTREAM);
        FORMATETC fileContents = Format("FileContents", 2, TYMED.TYMED_ISTREAM);
        Assert.NotEqual(0, drag.QueryGetData(ref directoryContents));
        Assert.Equal(0, drag.QueryGetData(ref fileContents));
        byte[] descriptor = ShellVirtualFileDescriptor.CreateHierarchy(entries);
        Assert.Equal(3u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(descriptor));
        Assert.Equal(0x10u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(4 + 592 + 36)));
        Assert.Equal("project\\empty", System.Text.Encoding.Unicode.GetString(descriptor.AsSpan(4 + 592 + 72, 520)).TrimEnd('\0'));
    }

    [Fact]
    public void AsyncExtraction_KeepsDownloadsAvailableAfterDragReturns()
    {
        IRemoteBrowser browser = DispatchProxy.Create<IRemoteBrowser, BrowserProxy>();
        ((BrowserProxy)browser).ProvideContent = true;
        using ShellVirtualFileDrag drag = new(browser, FileEntry());
        Assert.Equal(0, drag.SetAsyncMode(true));
        Assert.Equal(0, drag.StartOperation(IntPtr.Zero));
        drag.Dispose(); // DoDragDrop returned; the target is still extracting.
        FORMATETC contents = Format("FileContents", 0, TYMED.TYMED_ISTREAM);
        drag.GetData(ref contents, out STGMEDIUM medium);
        ShellReadStream? stream = null;
        try
        {
            stream = Assert.IsType<ShellReadStream>(Marshal.GetObjectForIUnknown(medium.unionmember));
            byte[] bytes = new byte[5];
            stream.Read(bytes, bytes.Length, IntPtr.Zero);
            stream.Seek(0, 0, IntPtr.Zero);
            string directory = Path.GetDirectoryName(Assert.IsType<string>(((BrowserProxy)browser).LastPath))!;
            Assert.Equal(0, drag.EndOperation(0, IntPtr.Zero, 1));
            Assert.True(Directory.Exists(directory));
            stream.Read(bytes, bytes.Length, IntPtr.Zero);
            Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(bytes));
        }
        finally
        {
            Marshal.Release(medium.unionmember);
            stream?.Dispose();
        }
        Assert.False(Directory.Exists(Path.GetDirectoryName(Assert.IsType<string>(((BrowserProxy)browser).LastPath))));
    }

    [Fact]
    public void ReadStream_ProvidesBytesAndReleasesTemporaryFile()
    {
        string directory = Directory.CreateTempSubdirectory("teleclav-test-").FullName;
        string path = Path.Combine(directory, "content");
        File.WriteAllText(path, "hello");
        FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
            4096, FileOptions.DeleteOnClose);
        ShellReadStream stream = new(file, directory);
        byte[] bytes = new byte[5];
        stream.Read(bytes, bytes.Length, IntPtr.Zero);
        Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(bytes));
        stream.Dispose();
        Assert.False(Directory.Exists(directory));
    }

    private static ShellVirtualFileDrag CreateDrag()
        => new(DispatchProxy.Create<IRemoteBrowser, BrowserProxy>(), FileEntry());

    private static SftpFileInfo FileEntry() => new("a.txt", "/a.txt", RemoteEntryKind.File, 5,
        DateTime.UnixEpoch, "", "", "");

    private static FORMATETC Format(string name, int index, TYMED medium) => new()
    {
        cfFormat = (short)DataFormats.GetDataFormat(name).Id,
        dwAspect = DVASPECT.DVASPECT_CONTENT,
        lindex = index,
        tymed = medium,
    };

    public class BrowserProxy : DispatchProxy
    {
        public bool ProvideContent { get; set; }
        public bool PauseDownload { get; set; }
        public TaskCompletionSource DownloadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueDownload { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? LastPath { get; private set; }
        public string? LastRemotePath { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IRemoteBrowser.DownloadToStreamWithinLimitAsync))
            {
                throw new NotSupportedException();
            }

            Assert.Equal(DateTime.UnixEpoch, Assert.IsType<DateTime>(args![3]));
            LastRemotePath = (string)args[0]!;
            LastPath = Assert.IsType<ShellStagingWriteStream>(args[1]).FilePath;
            if (!ProvideContent)
            {
                return Task.FromException(new FileNotFoundException("remote file"));
            }

            return WriteAsync((Stream)args![1]!);
        }

        private async Task WriteAsync(Stream path)
        {
            DownloadStarted.TrySetResult();
            if (PauseDownload) { await ContinueDownload.Task; }
            await path.WriteAsync(System.Text.Encoding.UTF8.GetBytes("hello"), CancellationToken.None);
        }
    }
}
