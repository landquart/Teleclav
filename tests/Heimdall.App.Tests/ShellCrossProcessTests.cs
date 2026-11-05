/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Threading;
using Heimdall.App.Views;
using Heimdall.Sftp;

namespace Heimdall.App.Tests;

public sealed class ShellCrossProcessTests
{
    [Theory]
    [InlineData("async")]
    [InlineData("sync")]
    [InlineData("noend")]
    [InlineData("crash")]
    [InlineData("cancel")]
    [InlineData("large")]
    public void NativeConsumer_UnmarshalsDataObjectAndReadsRepeatedStreams(string mode)
    {
        string executable = Environment.GetEnvironmentVariable("TELECLAV_NATIVE_COM_CONSUMER")
            ?? throw new InvalidOperationException("The native COM consumer must be built before this test.");
        Exception? failure = null;
        Thread thread = new(() =>
        {
            string area = Directory.CreateTempSubdirectory("teleclav-native-com-").FullName;
            string packet = Path.Combine(area, "marshal.com");
            string destination = Path.Combine(area, "received");
            try
            {
                IRemoteBrowser browser = DispatchProxy.Create<IRemoteBrowser, ShellVirtualFileDragTests.BrowserProxy>();
                ((ShellVirtualFileDragTests.BrowserProxy)browser).ProvideContent = true;
                ShellFolderItem[] entries =
                [
                    new(RemoteDownloadOpKind.MakeDirectory, "/проект", "проект", 0),
                    new(RemoteDownloadOpKind.DownloadFile, "/проект/a.txt", "проект/a.txt", 5, DateTime.UnixEpoch),
                    new(RemoteDownloadOpKind.MakeDirectory, "/проект/empty", "проект/empty", 0),
                    new(RemoteDownloadOpKind.MakeDirectory, "/проект/nested", "проект/nested", 0),
                    new(RemoteDownloadOpKind.DownloadFile, "/проект/nested/b.bin", "проект/nested/b.bin", 5, DateTime.UnixEpoch),
                    new(RemoteDownloadOpKind.DownloadFile, "/c.txt", "c.txt", 5, DateTime.UnixEpoch),
                ];
                using var drag = new ShellVirtualFileDrag(browser, entries, TimeSpan.FromMilliseconds(200));
                using var large = new ShellReadStream(new PatternStream());
                bool closed = false;
                drag.Closed = () => Volatile.Write(ref closed, true);
                Marshal.ThrowExceptionForHR(CreateStreamOnHGlobal(IntPtr.Zero, true, out IStream stream));
                Guid iid = mode == "large" ? typeof(IShellNativeStream).GUID : typeof(IShellNativeDataObject).GUID;
                IntPtr pointer = mode == "large"
                    ? Marshal.GetComInterfaceForObject(large, typeof(IShellNativeStream))
                    : Marshal.GetComInterfaceForObject(drag, typeof(IShellNativeDataObject));
                try
                {
                    Marshal.ThrowExceptionForHR(CoMarshalInterface(stream, ref iid, pointer, 0, IntPtr.Zero, 0));
                    stream.Stat(out STATSTG stat, 1);
                    byte[] bytes = new byte[checked((int)stat.cbSize)];
                    stream.Seek(0, 0, IntPtr.Zero);
                    stream.Read(bytes, bytes.Length, IntPtr.Zero);
                    File.WriteAllBytes(packet, bytes);
                    using Process child = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, ArgumentList = { packet, mode, destination } })!;
                    var frame = new DispatcherFrame();
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                    var elapsed = Stopwatch.StartNew();
                    timer.Tick += (_, _) =>
                    {
                        if (File.Exists(packet + ".cancel") && !File.Exists(packet + ".cancelled"))
                        {
                            drag.Cancel();
                            File.WriteAllText(packet + ".cancelled", "");
                        }
                        if (child.HasExited || elapsed.Elapsed > TimeSpan.FromSeconds(60)) { frame.Continue = false; }
                    };
                    timer.Start();
                    try { Dispatcher.PushFrame(frame); }
                    finally { timer.Stop(); }
                    if (!child.HasExited) { child.Kill(entireProcessTree: true); throw new TimeoutException("Native COM consumer timed out."); }
                    Assert.Equal(0, child.ExitCode);
                    if (mode is not "crash" and not "large")
                    {
                        byte[] expected = SHA256.HashData("hello"u8);
                        foreach (string relative in new[] { "проект/a.txt", "проект/nested/b.bin", "c.txt" })
                            Assert.Equal(expected, SHA256.HashData(File.ReadAllBytes(Path.Combine(destination, relative))));
                        Assert.True(Directory.Exists(Path.Combine(destination, "проект", "empty")));
                        Assert.Equal(3, Directory.GetFiles(destination, "*", SearchOption.AllDirectories).Length);
                        Assert.Equal(3, Directory.GetDirectories(destination, "*", SearchOption.AllDirectories).Length);
                    }
                    drag.Dispose();
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref closed), TimeSpan.FromSeconds(5)),
                        "The completed or abandoned COM operation did not close.");
                }
                finally
                {
                    Marshal.Release(pointer);
                    Marshal.ReleaseComObject(stream);
                }
            }
            catch (Exception ex) { failure = ex; }
            finally { Directory.Delete(area, recursive: true); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(90)), "COM host did not terminate.");
        if (failure is not null) { throw new InvalidOperationException("Cross-process COM failed.", failure); }
    }

    private sealed class PatternStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => 0x200000005L;
        public override long Position { get; set; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = (int)Math.Min(count, Math.Max(0, Length - Position));
            for (int i = 0; i < read; i++) { buffer[offset + i] = (byte)(Position++ & 255); }
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin)
            => Position = (origin == SeekOrigin.Begin ? 0 : origin == SeekOrigin.Current ? Position : Length) + offset;
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [DllImport("ole32.dll")]
    private static extern int CreateStreamOnHGlobal(IntPtr memory, [MarshalAs(UnmanagedType.Bool)] bool delete, out IStream stream);
    [DllImport("ole32.dll")]
    private static extern int CoMarshalInterface(IStream stream, ref Guid iid, IntPtr instance, int context, IntPtr destination, int flags);
}
