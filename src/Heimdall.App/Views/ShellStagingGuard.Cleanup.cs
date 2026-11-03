/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Heimdall.App.Views;

internal sealed partial class ShellStagingGuard
{
    private const int FileDispositionInfo = 4;
    private const int FileNamesInformation = 12;
    private const int NoMoreFiles = unchecked((int)0x80000006);

    internal void Delete()
    {
        lock (_gate)
        {
            if (_deleted) { return; } // Never resolve a reused pathname after successful teardown.
            ObjectDisposedException.ThrowIf(_disposeRequested || _directory is null, this);
            if (_writers != 0) { throw new IOException("Staging still has an active writer."); }
            ValidatePinnedChain();
            if (!_journalRemoved)
            {
                _journal!.Validate();
                _journal.MarkCleanup();
                DeleteEntries();
                Fault?.Invoke(ShellStagingPoint.BeforeJournalDelete);
                ValidatePinnedChain();
                _journal.Validate();
                if (EnumerateNames().Any(name => name != OwnershipName))
                    throw new IOException("Staging is not empty; ownership journal retained.");
                _journal.Delete();
                _journal = null;
                _journalRemoved = true;
            }
            // If final directory disposition failed, retries use only this still-pinned identity and empty directory.
            // No unknown child can be removed using the in-memory teardown authorization.
            if (EnumerateNames().Count != 0) { throw new IOException("Staging directory is not empty."); }
            MarkDelete(_directory!);
            _deleted = true;
            Dispose();
        }
    }

    private void ValidatePinnedChain()
    {
        for (int i = 0; i < _ancestors.Count; i++)
        {
            RequireDirectory(_ancestors[i]);
            RequireTrustedAcl(_ancestors[i], i >= _ancestors.Count - 2);
        }
        RequirePrivateDirectory(_directory!);
        if (Identity(_ancestors[^1]) != _rootIdentity || Identity(_directory!) != _directoryIdentity)
            throw new IOException("Staging directory identity changed.");
    }

    private sealed record DeleteEntry(string Name, SafeFileHandle Handle, OwnedFile? File);

    private void DeleteEntries()
    {
        List<DeleteEntry> entries = OpenDeletionSet();
        try
        {
            var resolvedFiles = entries.Where(e => e.File is not null).ToDictionary(e => e.File!.Target, e => e.File!, StringComparer.Ordinal);
            foreach (OwnedFile file in _objects.Values.Where(e => e.Stage != FileStage.Deleted).ToArray())
            {
                OwnedFile resolved = resolvedFiles.GetValueOrDefault(file.Target) ?? file;
                if (file.Stage != FileStage.Deleting) { Save(resolved with { Stage = FileStage.Deleting }); }
            }
            Fault?.Invoke(ShellStagingPoint.AfterDeleteIntent);
            foreach (DeleteEntry entry in entries)
            {
                if (entry.File is { } owned) { ValidateOwnedHandle(owned, entry.Handle); }
                else { ValidateLegacy(entry.Name, entry.Handle); }
                MarkDelete(entry.Handle);
                entry.Handle.Dispose(); // A successful disposition alone is not completed deletion.
                Fault?.Invoke(ShellStagingPoint.AfterFileDelete);
                if (entry.File is { } file) { Save(_objects[file.Target] with { Stage = FileStage.Deleted }); }
            }
            Fault?.Invoke(ShellStagingPoint.AfterDelete);
            foreach (OwnedFile missing in _objects.Values.Where(e => e.Stage == FileStage.Deleting).ToArray())
                Save(missing with { Stage = FileStage.Deleted });
        }
        finally { foreach (DeleteEntry entry in entries) { entry.Handle.Dispose(); } }
    }

    private List<DeleteEntry> OpenDeletionSet()
    {
        var candidates = new Dictionary<string, OwnedFile>(StringComparer.Ordinal);
        foreach (OwnedFile entry in _objects.Values.Where(e => e.Stage != FileStage.Deleted))
        {
            if (entry.Stage == FileStage.Publishing)
            {
                candidates.Add(entry.Partial, entry);
                candidates.Add(entry.Target, entry);
            }
            else { candidates.Add(entry.Name, entry); }
        }
        var entries = new List<DeleteEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (string name in EnumerateNames().Order(StringComparer.Ordinal))
            {
                if (name == OwnershipName) { continue; }
                if (!candidates.ContainsKey(name) && !_files.ContainsKey(name))
                    throw new IOException("Staging contains an unrecognized entry; cleanup refused.");
                SafeFileHandle handle = OpenRelative(_directory!, name, false, false, false,
                    fileAccess: DeleteAccess | 8, exclusive: true);
                entries.Add(new DeleteEntry(name, handle, null));
                if (candidates.TryGetValue(name, out OwnedFile? file))
                {
                    FileIdentity identity = ValidateOwnedHandle(file, handle);
                    if (!seen.Add(file.Target)) { throw new IOException("Multiple names claim one staging object."); }
                    entries[^1] = new DeleteEntry(name, handle, file with { Identity = identity, FinalName = name == file.Target });
                }
                else { ValidateLegacy(name, handle); }
            }
            return entries;
        }
        catch { foreach (DeleteEntry entry in entries) { entry.Handle.Dispose(); } throw; }
    }

    private void ValidateLegacy(string name, SafeFileHandle handle)
    {
        FileIdentity identity = Identity(handle);
        if (!_files.TryGetValue(name, out FileIdentity expected) || identity != expected
            || identity.Volume != _directoryIdentity.Volume || (identity.Attributes & (DirectoryAttribute | ReparseAttribute)) != 0
            || !GetFileInformationByHandle(handle, out FileInformation info) || info.NumberOfLinks != 1)
            throw new IOException("Legacy staging file identity changed.");
        RequireTrustedAcl(handle, false);
    }

    private List<string> EnumerateNames()
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        byte[] buffer = new byte[4096];
        bool restart = true;
        while (true)
        {
            int status = NtQueryDirectoryFile(_directory!, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out IoStatusBlock io,
                buffer, (uint)buffer.Length, FileNamesInformation, true, IntPtr.Zero, restart);
            restart = false;
            if (status == NoMoreFiles) { return names; }
            if (status != 0) { throw new IOException("Cannot enumerate pinned staging directory.", new Win32Exception((int)RtlNtStatusToDosError(status))); }
            ulong bytes = io.Information.ToUInt64();
            if (bytes < 12 || bytes > (ulong)buffer.Length || BinaryPrimitives.ReadUInt32LittleEndian(buffer) != 0)
                throw new IOException("Invalid native directory record.");
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(8));
            if (length is < 2 or > 510 || (length & 1) != 0 || length > bytes - 12)
                throw new IOException("Invalid native directory name length.");
            string name;
            try { name = new UnicodeEncoding(false, false, true).GetString(buffer, 12, (int)length); }
            catch (DecoderFallbackException ex) { throw new IOException("Invalid native directory name.", ex); }
            if (name is "." or "..") { continue; }
            ValidateComponent(name);
            if (names.Count == 65536 || !seen.Add(name))
                throw new IOException("Invalid or excessive directory entries.");
            names.Add(name);
        }
    }

    internal static void ValidateInteropLayout()
    {
        bool wide = IntPtr.Size == 8;
        if (Marshal.SizeOf<UnicodeString>() != (wide ? 16 : 8) || Marshal.SizeOf<ObjectAttributes>() != (wide ? 48 : 24)
            || Marshal.SizeOf<IoStatusBlock>() != (wide ? 16 : 8) || Marshal.SizeOf<FileInformation>() != 52
            || Marshal.SizeOf<FileIdInformation>() != 24 || Marshal.SizeOf<FileDispositionInformation>() != 1
            || (int)Marshal.OffsetOf<RenameInformation>(nameof(RenameInformation.First)) != (wide ? 20 : 12))
            throw new PlatformNotSupportedException("Windows staging interop layout mismatch.");
    }

    // Win32 FILE_DISPOSITION_INFO uses BOOLEAN (one byte), not BOOL (four bytes).
    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInformation { internal byte DeleteFile; }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryDirectoryFile(SafeFileHandle directory, IntPtr @event, IntPtr apc, IntPtr context,
        out IoStatusBlock status, [Out] byte[] buffer, uint length, int informationClass,
        [MarshalAs(UnmanagedType.U1)] bool single, IntPtr pattern, [MarshalAs(UnmanagedType.U1)] bool restart);
}
