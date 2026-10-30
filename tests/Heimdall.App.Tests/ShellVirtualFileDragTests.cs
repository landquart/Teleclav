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
        ShellVirtualFileDrag drag = CreateDrag();
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
        ShellVirtualFileDrag drag = new(browser, entries);
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
        ShellVirtualFileDrag drag = new(browser, entries);
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
        ShellVirtualFileDrag drag = CreateDrag();
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
    public void GetContents_MissingRemoteFile_ReportsFailure()
    {
        IRemoteBrowser browser = DispatchProxy.Create<IRemoteBrowser, BrowserProxy>();
        ShellVirtualFileDrag drag = new(browser, FileEntry());
        FORMATETC contents = Format("FileContents", 0, TYMED.TYMED_ISTREAM);
        Assert.Throws<FileNotFoundException>(() => drag.GetData(ref contents, out _));
        Assert.IsType<FileNotFoundException>(drag.TransferError);
        string attemptedPath = Assert.IsType<string>(((BrowserProxy)browser).LastPath);
        Assert.False(Directory.Exists(Path.GetDirectoryName(attemptedPath)));
    }

    [Fact]
    public void GetContents_ReturnsTheDownloadedBytes()
    {
        IRemoteBrowser browser = DispatchProxy.Create<IRemoteBrowser, BrowserProxy>();
        ((BrowserProxy)browser).ProvideContent = true;
        ShellVirtualFileDrag drag = new(browser, FileEntry());
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
        public string? LastPath { get; private set; }
        public string? LastRemotePath { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IRemoteBrowser.DownloadFileAsync))
            {
                throw new NotSupportedException();
            }

            LastRemotePath = (string)args![0]!;
            LastPath = (string)args[1]!;
            if (!ProvideContent)
            {
                return Task.FromException(new FileNotFoundException("remote file"));
            }

            File.WriteAllText((string)args![1]!, "hello");
            return Task.CompletedTask;
        }
    }
}
