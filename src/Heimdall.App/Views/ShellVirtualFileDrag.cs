/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Heimdall.Sftp;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;
using DataFormats = System.Windows.DataFormats;

namespace Heimdall.App.Views;

/// <summary>Provides remote files as Shell virtual files, retaining the internal drag marker.</summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class ShellVirtualFileDrag : ComDataObject, IDataObjectAsyncCapability, IDisposable
{
    private const int DvEFormatEtc = unchecked((int)0x80040064);
    private const int ENotImpl = unchecked((int)0x80004001);
    private const int OleEAdvisenotSupported = unchecked((int)0x80040003);
    private readonly byte[] _descriptor;
    private readonly IRemoteBrowser _browser;
    private readonly ShellDownloadCache _cache;
    private readonly ShellAsyncLifetime _lifetime;
    private readonly object _asyncGate = new();
    private IntPtr _asyncReference;
    private bool _asyncClosed;
    private bool _asyncMode;
    internal Action<Exception>? AsyncFailure { get; set; }
    internal Action? Closed { get; set; }
    private readonly IReadOnlyList<SftpFileInfo> _files;
    private readonly short _descriptorFormat = (short)DataFormats.GetDataFormat("FileGroupDescriptorW").Id;
    private readonly short _contentsFormat = (short)DataFormats.GetDataFormat("FileContents").Id;
    private readonly short _internalFormat = (short)DataFormats.GetDataFormat(SftpRemoteDragPayload.FormatName).Id;
    private readonly short _preferredFormat = (short)DataFormats.GetDataFormat("Preferred DropEffect").Id;

    internal ShellVirtualFileDrag(IRemoteBrowser browser, SftpFileInfo file)
    {
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        ArgumentNullException.ThrowIfNull(file);
        _files = [file];
        _descriptor = ShellVirtualFileDescriptor.Create(_files);
        _cache = CreateCache();
        _lifetime = new ShellAsyncLifetime(Cleanup);
    }

    internal ShellVirtualFileDrag(IRemoteBrowser browser, IReadOnlyList<SftpFileInfo> files)
    {
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        ArgumentNullException.ThrowIfNull(files);
        _files = files.ToArray();
        _descriptor = ShellVirtualFileDescriptor.Create(_files);
        _cache = CreateCache();
        _lifetime = new ShellAsyncLifetime(Cleanup);
    }

    internal ShellVirtualFileDrag(IRemoteBrowser browser, IReadOnlyList<ShellFolderItem> items)
    {
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        ArgumentNullException.ThrowIfNull(items);
        _files = items.Select(item => new SftpFileInfo(item.RelativePath, item.RemotePath,
            item.Kind == RemoteDownloadOpKind.MakeDirectory ? RemoteEntryKind.Directory : RemoteEntryKind.File,
            item.Size, DateTime.UnixEpoch, "", "", "")).ToArray();
        _descriptor = ShellVirtualFileDescriptor.CreateHierarchy(items);
        _cache = CreateCache();
        _lifetime = new ShellAsyncLifetime(Cleanup);
    }

    private ShellDownloadCache CreateCache() => new(16L * 1024 * 1024 * 1024,
        (index, path, ct) => _browser.DownloadFileWithinLimitAsync(_files[index].FullPath, path, _files[index].Size, ct));

    private void Cleanup()
    {
        IntPtr reference;
        lock (_asyncGate)
        {
            _asyncClosed = true;
            reference = _asyncReference;
            _asyncReference = IntPtr.Zero;
        }
        try { _cache.Dispose(); }
        finally
        {
            try { if (reference != IntPtr.Zero) { Marshal.Release(reference); } }
            finally { Closed?.Invoke(); }
        }
    }

    public void Dispose() => _lifetime.DragReturned();
    internal void Cancel() => _cache.Dispose();

    private Exception? _transferError;
    internal Exception? TransferError => Volatile.Read(ref _transferError);

    internal void Start()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException("Shell drag-and-drop requires an STA thread.");
        }

        int result;
        try
        {
            Marshal.ThrowExceptionForHR(SetAsyncMode(true));
            result = NativeMethods.DoDragDrop(this, new ShellDropSource(), 3, out _);
        }
        finally { Dispose(); }
        // DRAGDROP_S_DROP and DRAGDROP_S_CANCEL are successful HRESULTs.
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    public int SetAsyncMode(bool enabled)
    {
        lock (_asyncGate)
        {
            if (_asyncClosed) { return unchecked((int)0x8000FFFF); }
            if (enabled && _asyncReference == IntPtr.Zero)
            {
                _asyncReference = Marshal.GetComInterfaceForObject(this, typeof(IDataObjectAsyncCapability));
            }
            _asyncMode = enabled;
        }
        return 0;
    }

    public int GetAsyncMode(out bool enabled) { lock (_asyncGate) { enabled = _asyncMode; } return 0; }
    public int StartOperation(IntPtr reserved) => _lifetime.Begin() ? 0 : unchecked((int)0x8000FFFF);
    public int InOperation(out bool active) { active = _lifetime.InOperation; return 0; }
    public int EndOperation(int result, IntPtr reserved, uint effects)
    {
        try
        {
            Exception? failure = TransferError;
            if (failure is null && result < 0)
            {
                failure = Marshal.GetExceptionForHR(result) ?? new COMException("Shell transfer failed", result);
            }
            if (failure is not null) { AsyncFailure?.Invoke(failure); }
        }
        finally { _lifetime.End(); }
        return 0;
    }

    public void GetData(ref FORMATETC format, out STGMEDIUM medium)
    {
        if (QueryGetData(ref format) != 0)
        {
            Marshal.ThrowExceptionForHR(DvEFormatEtc);
        }

        medium = default;
        if (format.cfFormat == _contentsFormat)
        {
            SftpFileInfo fileEntry = _files[format.lindex];
            try
            {
                Stream file = _cache.OpenAsync(format.lindex, fileEntry.Size, CancellationToken.None).GetAwaiter().GetResult();
                var stream = new ShellReadStream(file);
                try
                {
                    medium.unionmember = Marshal.GetComInterfaceForObject(stream, typeof(IShellNativeStream));
                    medium.tymed = TYMED.TYMED_ISTREAM;
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
            }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(ref _transferError, ex, null);
                throw ShellTransferFailure.ToComException(ex);
            }

            return;
        }

        byte[] bytes = format.cfFormat == _descriptorFormat ? _descriptor
            : format.cfFormat == _preferredFormat ? [1, 0, 0, 0] : [0, 0, 0, 0];
        IntPtr memory = NativeMethods.GlobalAlloc(0x42, (nuint)bytes.Length);
        if (memory == IntPtr.Zero) { throw new OutOfMemoryException(); }
        IntPtr pointer = NativeMethods.GlobalLock(memory);
        if (pointer == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(memory);
            throw new OutOfMemoryException();
        }

        try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
        finally { NativeMethods.GlobalUnlock(memory); }
        medium.tymed = TYMED.TYMED_HGLOBAL;
        medium.unionmember = memory;
    }

    public int QueryGetData(ref FORMATETC format)
    {
        if (format.dwAspect != DVASPECT.DVASPECT_CONTENT) { return DvEFormatEtc; }
        if (format.cfFormat == _contentsFormat)
        {
            return format.lindex >= 0 && format.lindex < _files.Count
                && _files[format.lindex].IsRegularFile
                && (format.tymed & TYMED.TYMED_ISTREAM) != 0 ? 0 : DvEFormatEtc;
        }

        return (format.cfFormat == _descriptorFormat || format.cfFormat == _preferredFormat || format.cfFormat == _internalFormat)
            && format.lindex == -1 && (format.tymed & TYMED.TYMED_HGLOBAL) != 0 ? 0 : DvEFormatEtc;
    }

    public IEnumFORMATETC EnumFormatEtc(DATADIR direction)
    {
        if (direction != DATADIR.DATADIR_GET) { throw new COMException("Not supported", ENotImpl); }
        List<FORMATETC> formats = [Format(_descriptorFormat, -1, TYMED.TYMED_HGLOBAL)];
        for (int i = 0; i < _files.Count; i++)
        {
            if (_files[i].IsRegularFile) { formats.Add(Format(_contentsFormat, i, TYMED.TYMED_ISTREAM)); }
        }
        formats.Add(Format(_preferredFormat, -1, TYMED.TYMED_HGLOBAL));
        formats.Add(Format(_internalFormat, -1, TYMED.TYMED_HGLOBAL));
        Marshal.ThrowExceptionForHR(NativeMethods.SHCreateStdEnumFmtEtc((uint)formats.Count, formats.ToArray(), out IEnumFORMATETC result));
        return result;
    }

    private static FORMATETC Format(short id, int index, TYMED medium) => new()
    {
        cfFormat = id,
        dwAspect = DVASPECT.DVASPECT_CONTENT,
        lindex = index,
        tymed = medium,
    };

    public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium) => throw new COMException("Not supported", ENotImpl);
    public int GetCanonicalFormatEtc(ref FORMATETC input, out FORMATETC output) { output = default; return ENotImpl; }
    public void SetData(ref FORMATETC format, ref STGMEDIUM medium, bool release) => throw new COMException("Not supported", ENotImpl);
    public int DAdvise(ref FORMATETC format, ADVF flags, IAdviseSink sink, out int connection) { connection = 0; return OleEAdvisenotSupported; }
    public void DUnadvise(int connection) => throw new COMException("Not supported", OleEAdvisenotSupported);
    public int EnumDAdvise(out IEnumSTATDATA? result) { result = null; return OleEAdvisenotSupported; }

    [ComVisible(true)]
    [Guid("00000121-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDropSource
    {
        [PreserveSig] int QueryContinueDrag(int escape, int keys);
        [PreserveSig] int GiveFeedback(int effect);
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class ShellDropSource : IDropSource
    {
        public int QueryContinueDrag(int escape, int keys) => escape != 0 ? 0x00040101 : (keys & 1) == 0 ? 0x00040100 : 0;
        public int GiveFeedback(int effect) => 0x00040102;
    }

    private static class NativeMethods
    {
        [DllImport("ole32.dll")] internal static extern int DoDragDrop(ComDataObject data, IDropSource source, int allowed, out int effect);
        [DllImport("shell32.dll")] internal static extern int SHCreateStdEnumFmtEtc(uint count, FORMATETC[] formats, out IEnumFORMATETC result);
        [DllImport("kernel32.dll")] internal static extern IntPtr GlobalAlloc(uint flags, nuint size);
        [DllImport("kernel32.dll")] internal static extern IntPtr GlobalLock(IntPtr handle);
        [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GlobalUnlock(IntPtr handle);
        [DllImport("kernel32.dll")] internal static extern IntPtr GlobalFree(IntPtr handle);
    }
}

[ComVisible(true)]
[Guid("3D8B0590-F691-11D2-8EA9-006097DF5BD4")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDataObjectAsyncCapability
{
    [PreserveSig] int SetAsyncMode([MarshalAs(UnmanagedType.Bool)] bool enabled);
    [PreserveSig] int GetAsyncMode([MarshalAs(UnmanagedType.Bool)] out bool enabled);
    [PreserveSig] int StartOperation(IntPtr reserved);
    [PreserveSig] int InOperation([MarshalAs(UnmanagedType.Bool)] out bool active);
    [PreserveSig] int EndOperation(int result, IntPtr reserved, uint effects);
}
