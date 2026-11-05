/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Threading;
using Heimdall.App.Views;
using Heimdall.Sftp;

namespace Heimdall.App.Tests;

public sealed class ShellCrossProcessTests
{
    [Fact]
    public void NativeConsumer_UnmarshalsDataObjectAndReadsRepeatedStreams()
    {
        string executable = Environment.GetEnvironmentVariable("TELECLAV_NATIVE_COM_CONSUMER")
            ?? throw new InvalidOperationException("The native COM consumer must be built before this test.");
        Exception? failure = null;
        Thread thread = new(() =>
        {
            string packet = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".com");
            try
            {
                IRemoteBrowser browser = DispatchProxy.Create<IRemoteBrowser, ShellVirtualFileDragTests.BrowserProxy>();
                ((ShellVirtualFileDragTests.BrowserProxy)browser).ProvideContent = true;
                SftpFileInfo entry = new("a.txt", "/a.txt", RemoteEntryKind.File, 5, DateTime.UnixEpoch, "", "", "");
                using var drag = new ShellVirtualFileDrag(browser, new[] { entry, entry with { Name = "b.txt", FullPath = "/b.txt" } });
                Marshal.ThrowExceptionForHR(CreateStreamOnHGlobal(IntPtr.Zero, true, out IStream stream));
                IntPtr pointer = Marshal.GetComInterfaceForObject(drag, typeof(IDataObject));
                try
                {
                    Guid iid = typeof(IDataObject).GUID;
                    Marshal.ThrowExceptionForHR(CoMarshalInterface(stream, ref iid, pointer, 0, IntPtr.Zero, 0));
                    stream.Stat(out STATSTG stat, 1);
                    byte[] bytes = new byte[checked((int)stat.cbSize)];
                    stream.Seek(0, 0, IntPtr.Zero);
                    stream.Read(bytes, bytes.Length, IntPtr.Zero);
                    File.WriteAllBytes(packet, bytes);
                    using Process child = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, ArgumentList = { packet } })!;
                    var frame = new DispatcherFrame();
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                    var elapsed = Stopwatch.StartNew();
                    timer.Tick += (_, _) =>
                    {
                        if (child.HasExited || elapsed.Elapsed > TimeSpan.FromSeconds(60)) { frame.Continue = false; }
                    };
                    timer.Start();
                    try { Dispatcher.PushFrame(frame); }
                    finally { timer.Stop(); }
                    if (!child.HasExited) { child.Kill(entireProcessTree: true); throw new TimeoutException("Native COM consumer timed out."); }
                    Assert.Equal(0, child.ExitCode);
                }
                finally
                {
                    Marshal.Release(pointer);
                    Marshal.ReleaseComObject(stream);
                }
            }
            catch (Exception ex) { failure = ex; }
            finally { File.Delete(packet); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(90)), "COM host did not terminate.");
        if (failure is not null) { throw new InvalidOperationException("Cross-process COM failed.", failure); }
    }

    [DllImport("ole32.dll")]
    private static extern int CreateStreamOnHGlobal(IntPtr memory, [MarshalAs(UnmanagedType.Bool)] bool delete, out IStream stream);
    [DllImport("ole32.dll")]
    private static extern int CoMarshalInterface(IStream stream, ref Guid iid, IntPtr instance, int context, IntPtr destination, int flags);
}
