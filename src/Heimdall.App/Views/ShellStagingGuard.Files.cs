/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Heimdall.App.Views;

internal enum ShellStagingPoint
{
    BeforeIntent, AfterIntent, BeforeCreate, AfterCreate, AfterIdentity, DuringWrite,
    AfterWrite, BeforePublish, AfterPublish, BeforeComplete, AfterDeleteIntent, AfterDelete,
}

internal sealed partial class ShellStagingGuard
{
    internal enum FileStage : byte { Reserved, Created, Writing, Written, Publishing, Published, Deleting, Deleted }

    internal sealed record OwnedFile(string Target, Guid ObjectId, long Size, FileStage Stage,
        FileIdentity? Identity = null, bool FinalName = false)
    {
        internal string Partial => Target + "." + ObjectId.ToString("N") + ".part";
        internal string Name => FinalName ? Target : Partial;
    }

    private readonly Dictionary<string, OwnedFile> _objects = new(StringComparer.Ordinal);
    // Per-operation, internal test seam. No environment variable or user setting enables it.
    internal Action<ShellStagingPoint>? Fault { get; set; }
    internal OwnedFile? GetFileState(string target) { lock (_gate) { return _objects.GetValueOrDefault(target); } }

    internal async Task DownloadAsync(string target, long size, Func<Stream, CancellationToken, Task> download,
        CancellationToken ct)
    {
        ValidateFileName(target);
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        ct.ThrowIfCancellationRequested();
        Fault?.Invoke(ShellStagingPoint.BeforeIntent);
        OwnedFile entry = new(target, Guid.NewGuid(), size, FileStage.Reserved);
        lock (_gate)
        {
            if (_files.ContainsKey(target) || _objects.ContainsKey(target)) { throw new IOException("Duplicate staging target."); }
            Save(entry);
        }
        Fault?.Invoke(ShellStagingPoint.AfterIntent);
        ct.ThrowIfCancellationRequested();
        Fault?.Invoke(ShellStagingPoint.BeforeCreate);
        // FILE_CREATE plus the EA and DACL are one native creation request. Never open an existing file for writing.
        SafeFileHandle handle = OpenRelative(_directory!, entry.Partial, true, false, false,
            extendedAttributes: OwnerEa(entry), fileAccess: DeleteAccess | 0x1b, exclusive: true);
        using FileStream file = OwnStream(handle);
        Fault?.Invoke(ShellStagingPoint.AfterCreate);
        FileIdentity identity = ValidateOwnedHandle(entry, file.SafeFileHandle);
        Save(entry = entry with { Stage = FileStage.Created, Identity = identity });
        Fault?.Invoke(ShellStagingPoint.AfterIdentity);
        Save(entry = entry with { Stage = FileStage.Writing });
        using (var output = new ShellStagingWriteStream(file, size, () => Fault?.Invoke(ShellStagingPoint.DuringWrite), Path.Combine(_path, entry.Partial)))
            await download(output, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (file.Length != size) { throw new IOException("The downloaded file has an unexpected size."); }
        file.Flush(flushToDisk: true);
        Fault?.Invoke(ShellStagingPoint.AfterWrite);
        Save(entry = entry with { Stage = FileStage.Written });
        Fault?.Invoke(ShellStagingPoint.BeforePublish);
        ct.ThrowIfCancellationRequested();
        Save(entry = entry with { Stage = FileStage.Publishing });
        if (ValidateOwnedHandle(entry, file.SafeFileHandle) != identity) { throw new IOException("Staging identity changed before publication."); }
        Rename(file.SafeFileHandle, target);
        file.Flush(flushToDisk: true);
        Fault?.Invoke(ShellStagingPoint.AfterPublish);
        if (ValidateOwnedHandle(entry, file.SafeFileHandle) != identity) { throw new IOException("Staging identity changed during publication."); }
        Fault?.Invoke(ShellStagingPoint.BeforeComplete);
        Save(entry with { Stage = FileStage.Published, FinalName = true });
    }

    private static FileStream OwnStream(SafeFileHandle handle)
    {
        try { return new FileStream(handle, FileAccess.ReadWrite, 81920, isAsync: false); }
        catch { handle.Dispose(); throw; }
    }

    private void Save(OwnedFile entry)
    {
        lock (_gate)
        {
            ValidateTransition(_objects.GetValueOrDefault(entry.Target), entry);
            _journal!.AppendObject(entry);
            _objects[entry.Target] = entry;
        }
    }

    private static void ValidateTransition(OwnedFile? previous, OwnedFile next)
    {
        ValidateFileName(next.Target);
        if (next.ObjectId == Guid.Empty || next.Size < 0 || !Enum.IsDefined(next.Stage))
            throw new IOException("Invalid owned-file record.");
        if (previous is null)
        {
            if (next.Stage != FileStage.Reserved || next.Identity is not null || next.FinalName)
                throw new IOException("Missing creation intent.");
            return;
        }
        if (next.ObjectId != previous.ObjectId || next.Size != previous.Size
            || (previous.Identity is not null && previous.Identity != next.Identity))
            throw new IOException("Owned-file identity was rewritten.");
        bool deleting = next.Stage == FileStage.Deleting && previous.Stage < FileStage.Deleting;
        bool advancing = previous.Stage < FileStage.Deleted && next.Stage == previous.Stage + 1;
        if ((!deleting && !advancing) || (next.Stage >= FileStage.Created && next.Stage < FileStage.Deleting && next.Identity is null)
            || (next.FinalName && next.Stage < FileStage.Published)
            || (previous.FinalName && !next.FinalName)
            || (next.Stage == FileStage.Published && !next.FinalName)
            || (next.FinalName != previous.FinalName && previous.Stage != FileStage.Publishing))
            throw new IOException("Invalid owned-file state transition.");
    }

    private byte[] OwnerEa(OwnedFile entry)
    {
        byte[] name = Encoding.ASCII.GetBytes("TELECLAV.OWNER");
        byte[] result = new byte[8 + name.Length + 1 + 34];
        result[5] = (byte)name.Length;
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), 34);
        name.CopyTo(result, 8);
        int value = 9 + name.Length;
        result[value] = 1; // EA format version
        result[value + 1] = 1; // Regular file
        Guid.ParseExact(Path.GetFileName(_path), "N").TryWriteBytes(result.AsSpan(value + 2));
        entry.ObjectId.TryWriteBytes(result.AsSpan(value + 18));
        return result;
    }

    private FileIdentity ValidateOwnedHandle(OwnedFile entry, SafeFileHandle handle)
    {
        FileIdentity identity = Identity(handle);
        if ((identity.Attributes & (DirectoryAttribute | ReparseAttribute)) != 0
            || identity.Volume != Identity(_directory!).Volume
            || !GetFileInformationByHandle(handle, out FileInformation info) || info.NumberOfLinks != 1)
            throw new IOException("Owned staging object is not a private regular file.");
        RequireTrustedAcl(handle, true, file: true);
        byte[] expected = OwnerEa(entry);
        byte[] query = new byte[6 + expected[5]];
        query[4] = expected[5];
        expected.AsSpan(8, expected[5]).CopyTo(query.AsSpan(5));
        byte[] actual = new byte[expected.Length];
        int status = NtQueryEaFile(handle, out IoStatusBlock result, actual, (uint)actual.Length,
            true, query, (uint)query.Length, IntPtr.Zero, true);
        if (status != 0 || result.Information.ToUInt64() != (ulong)expected.Length || !expected.SequenceEqual(actual))
            throw new IOException("Staging object has no matching creation tag.");
        if (entry.Identity is { } recorded && recorded != identity)
            throw new IOException("Staging file identity changed.");
        return identity;
    }

    private void Rename(SafeFileHandle file, string target)
    {
        int rootOffset = (int)Marshal.OffsetOf<RenameInformation>(nameof(RenameInformation.Root));
        int lengthOffset = (int)Marshal.OffsetOf<RenameInformation>(nameof(RenameInformation.Length));
        int nameOffset = (int)Marshal.OffsetOf<RenameInformation>(nameof(RenameInformation.First));
        byte[] name = Encoding.Unicode.GetBytes(target);
        byte[] buffer = new byte[Math.Max(Marshal.SizeOf<RenameInformation>(), nameOffset + name.Length + 2)];
        bool retained = false;
        try
        {
            _directory!.DangerousAddRef(ref retained);
            if (IntPtr.Size == 8) { BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(rootOffset), _directory.DangerousGetHandle().ToInt64()); }
            else { BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(rootOffset), _directory.DangerousGetHandle().ToInt32()); }
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(lengthOffset), name.Length);
            name.CopyTo(buffer, nameOffset);
            // Native FileRenameInformation = 10. ReplaceIfExists remains FALSE.
            int status = NtSetInformationFile(file, out _, buffer, (uint)buffer.Length, 10);
            if (status < 0) { throw new IOException("Exclusive staging publication failed.", new Win32Exception((int)RtlNtStatusToDosError(status))); }
        }
        finally { if (retained) { _directory!.DangerousRelease(); } }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RenameInformation { internal uint Flags; internal IntPtr Root; internal uint Length; internal char First; }

    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationFile(SafeFileHandle handle, out IoStatusBlock status,
        [In] byte[] information, uint length, int informationClass);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryEaFile(SafeFileHandle handle, out IoStatusBlock status,
        [Out] byte[] buffer, uint length, [MarshalAs(UnmanagedType.U1)] bool single,
        [In] byte[] query, uint queryLength, IntPtr index, [MarshalAs(UnmanagedType.U1)] bool restart);
}

/// <summary>A bounded write-only view; the guard retains ownership of the underlying handle.</summary>
internal sealed class ShellStagingWriteStream(FileStream file, long limit, Action written, string? filePath = null) : Stream
{
    private long _count;
    internal string FilePath => filePath ?? file.Name;
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _count;
    public override long Position { get => _count; set => throw new NotSupportedException(); }
    public override void Flush() => file.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length > limit - _count) { throw new IOException("The download exceeds its permitted size."); }
        file.Write(buffer);
        _count += buffer.Length;
        written();
    }
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
}
