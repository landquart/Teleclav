/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using DataFormats = System.Windows.DataFormats;
using Heimdall.Sftp;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Heimdall.App.Views;

/// <summary>Provides one remote file as a Shell virtual file, retaining the internal drag marker.</summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class ShellVirtualFileDrag : ComDataObject
{
    private const int DvEFormatEtc = unchecked((int)0x80040064);
    private const int ENotImpl = unchecked((int)0x80004001);
    private const int OleEAdvisenotSupported = unchecked((int)0x80040003);
    private readonly byte[] _descriptor;
    private readonly IRemoteBrowser _browser;
    private readonly SftpFileInfo _file;
    private readonly short _descriptorFormat = (short)DataFormats.GetDataFormat("FileGroupDescriptorW").Id;
    private readonly short _contentsFormat = (short)DataFormats.GetDataFormat("FileContents").Id;
    private readonly short _internalFormat = (short)DataFormats.GetDataFormat(SftpRemoteDragPayload.FormatName).Id;
    private readonly short _preferredFormat = (short)DataFormats.GetDataFormat("Preferred DropEffect").Id;

    internal ShellVirtualFileDrag(IRemoteBrowser browser, SftpFileInfo file)
    {
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        _file = file ?? throw new ArgumentNullException(nameof(file));
        _descriptor = ShellVirtualFileDescriptor.Create(file);
    }

    internal Exception? TransferError { get; private set; }

    internal void Start()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException("Shell drag-and-drop requires an STA thread.");
        }

        int result = NativeMethods.DoDragDrop(this, new ShellDropSource(), 3, out _);
        // DRAGDROP_S_DROP and DRAGDROP_S_CANCEL are successful HRESULTs.
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
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
            string directory = Directory.CreateTempSubdirectory("teleclav-drag-").FullName;
            string path = Path.Combine(directory, "content");
            try
            {
                // The actual browser is captured by the view, including host-key verification.
                // Run outside the UI synchronization context; no data is read on drag-over.
                Task.Run(() => _browser.DownloadFileAsync(_file.FullPath, path, overwrite: false)).GetAwaiter().GetResult();
                if (new FileInfo(path).Length != _file.Size)
                {
                    throw new IOException("The remote file changed size during the transfer.");
                }

                FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                    81920, FileOptions.DeleteOnClose | FileOptions.SequentialScan);
                var stream = new ShellReadStream(file, directory);
                try
                {
                    medium.unionmember = Marshal.GetComInterfaceForObject(stream, typeof(IStream));
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
                TransferError = ex;
                try { Directory.Delete(directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
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
            return format.lindex == 0 && (format.tymed & TYMED.TYMED_ISTREAM) != 0 ? 0 : DvEFormatEtc;
        }

        return (format.cfFormat == _descriptorFormat || format.cfFormat == _preferredFormat || format.cfFormat == _internalFormat)
            && format.lindex == -1 && (format.tymed & TYMED.TYMED_HGLOBAL) != 0 ? 0 : DvEFormatEtc;
    }

    public IEnumFORMATETC EnumFormatEtc(DATADIR direction)
    {
        if (direction != DATADIR.DATADIR_GET) { throw new COMException("Not supported", ENotImpl); }
        FORMATETC[] formats =
        [
            Format(_descriptorFormat, -1, TYMED.TYMED_HGLOBAL),
            Format(_contentsFormat, 0, TYMED.TYMED_ISTREAM),
            Format(_preferredFormat, -1, TYMED.TYMED_HGLOBAL),
            Format(_internalFormat, -1, TYMED.TYMED_HGLOBAL),
        ];
        Marshal.ThrowExceptionForHR(NativeMethods.SHCreateStdEnumFmtEtc((uint)formats.Length, formats, out IEnumFORMATETC result));
        return result;
    }

    private static FORMATETC Format(short id, int index, TYMED medium) => new()
    {
        cfFormat = id, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = index, tymed = medium,
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
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GlobalUnlock(IntPtr handle);
        [DllImport("kernel32.dll")] internal static extern IntPtr GlobalFree(IntPtr handle);
    }
}
