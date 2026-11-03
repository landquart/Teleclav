/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Heimdall.App.Views;

internal sealed partial class ShellStagingGuard
{
    /// <summary>
    /// Exclusive live-operation lease and durable, bounded, hash-chained ownership records.
    /// Hashes detect corruption. The validated directory ACL is the source of trust, not the hash.
    /// </summary>
    private sealed class ShellStagingJournal : IDisposable
    {
        private const int MaximumBytes = 16 * 1024 * 1024;
        private readonly FileStream _stream;
        private byte[] _previous = new byte[32];
        private bool _cleanup;
        private int _version = 2;
        private bool _failed;

        private ShellStagingJournal(SafeFileHandle handle)
        {
            try
            {
                FileIdentity identity = Identity(handle);
                if ((identity.Attributes & (DirectoryAttribute | ReparseAttribute)) != 0)
                    throw new IOException("Ownership journal is not a regular file.");
                RequireTrustedAcl(handle, true, file: true);
                _stream = new FileStream(handle, FileAccess.ReadWrite);
            }
            catch { handle.Dispose(); throw; }
        }

        internal static ShellStagingJournal Create(SafeFileHandle directory, FileIdentity root, FileIdentity operation, Guid id)
        {
            var journal = new ShellStagingJournal(OpenRelative(directory, OwnershipName, true, false, false, true));
            try
            {
                journal.Append(Header(root, operation, id));
                return journal;
            }
            catch { journal.Dispose(); throw; }
        }

        internal static ShellStagingJournal Open(SafeFileHandle directory, FileIdentity root, FileIdentity operation,
            Guid id, Dictionary<string, FileIdentity> files, Dictionary<string, OwnedFile> objects)
        {
            var journal = new ShellStagingJournal(OpenRelative(directory, OwnershipName, false, false, false, true));
            try
            {
                // Read through the same exclusively opened handle. Never deserialize arbitrary paths or type names.
                if (journal._stream.Length > MaximumBytes) { throw new IOException("Ownership journal is too large."); }
                using var reader = new BinaryReader(journal._stream, Encoding.UTF8, leaveOpen: true);
                bool first = true;
                while (journal._stream.Position < journal._stream.Length)
                {
                    long frameStart = journal._stream.Position;
                    if (journal._stream.Length - frameStart < 4)
                    {
                        if (first) { throw new IOException("Truncated ownership header."); }
                        journal.TrimTail(frameStart);
                        break;
                    }
                    int length = reader.ReadInt32();
                    if (length is < 1 or > 128) { throw new IOException("Invalid ownership record length."); }
                    if (journal._stream.Length - journal._stream.Position < length + 32)
                    {
                        if (first) { throw new IOException("Truncated ownership header."); }
                        journal.TrimTail(frameStart);
                        break;
                    }
                    byte[] payload = reader.ReadBytes(length);
                    byte[] hash = reader.ReadBytes(32);
                    if (payload.Length != length || hash.Length != 32
                        || !CryptographicOperations.FixedTimeEquals(hash, Digest(journal._previous, payload)))
                        throw new IOException("Damaged ownership journal.");
                    journal._previous = hash;
                    if (first)
                    {
                        if (payload.SequenceEqual(Header(root, operation, id, 1))) { journal._version = 1; }
                        else if (!payload.SequenceEqual(Header(root, operation, id)))
                            throw new IOException("Ownership journal does not identify this directory.");
                        first = false;
                        continue;
                    }
                    using var record = new BinaryReader(new MemoryStream(payload), Encoding.UTF8);
                    byte kind = record.ReadByte();
                    if (kind == 1 && journal._version == 1 && !journal._cleanup)
                    {
                        int count = record.ReadByte();
                        if (count is < 1 or > 10) { throw new IOException("Invalid ownership entry name length."); }
                        byte[] nameBytes = record.ReadBytes(count);
                        if (nameBytes.Length != count) { throw new IOException("Truncated ownership entry name."); }
                        string name = Encoding.ASCII.GetString(nameBytes);
                        ValidateFileName(name);
                        var identity = new FileIdentity(record.ReadUInt32(), record.ReadUInt64(), record.ReadUInt64(), record.ReadUInt64());
                        if ((identity.Attributes & (DirectoryAttribute | ReparseAttribute)) != 0 || identity.Volume != root.Volume)
                            throw new IOException("Invalid ownership file identity.");
                        if (!files.TryAdd(name, identity)) { throw new IOException("Duplicate ownership entry."); }
                    }
                    else if (kind == 3 && journal._version == 2)
                    {
                        OwnedFile entry = ReadObject(record);
                        if (journal._cleanup && entry.Stage < FileStage.Deleting)
                            throw new IOException("Creation record after cleanup began.");
                        if (entry.Identity is { } identity && ((identity.Attributes & (DirectoryAttribute | ReparseAttribute)) != 0 || identity.Volume != root.Volume))
                            throw new IOException("Invalid owned-file identity.");
                        ValidateTransition(objects.GetValueOrDefault(entry.Target), entry);
                        objects[entry.Target] = entry;
                    }
                    else if (kind == 2 && !journal._cleanup) { journal._cleanup = true; }
                    else { throw new IOException("Invalid ownership state transition."); }
                    if (record.BaseStream.Position != record.BaseStream.Length) { throw new IOException("Invalid ownership record."); }
                }
                if (first) { throw new IOException("Missing ownership header."); }
                return journal;
            }
            catch (EndOfStreamException ex) { journal.Dispose(); throw new IOException("Truncated ownership journal.", ex); }
            catch { journal.Dispose(); throw; }
        }

        private static byte[] Header(FileIdentity root, FileIdentity operation, Guid id, int version = 2)
        {
            using var buffer = new MemoryStream();
            using var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true);
            writer.Write("Teleclav staging");
            writer.Write(version); // Format version. FILE_ID_INFO supplies a 64-bit volume serial and 128-bit file ID.
            writer.Write((byte)0); // Active. Cleanup-pending is an append-only state transition.
            writer.Write(id.ToByteArray());
            WriteIdentity(writer, root);
            WriteIdentity(writer, operation);
            return buffer.ToArray();
        }

        private void TrimTail(long length)
        {
            _stream.SetLength(length);
            _stream.Position = length;
            _stream.Flush(flushToDisk: true);
        }

        private static OwnedFile ReadObject(BinaryReader reader)
        {
            int count = reader.ReadByte();
            if (count is < 1 or > 10) { throw new IOException("Invalid owned-file target length."); }
            byte[] name = reader.ReadBytes(count);
            byte[] id = reader.ReadBytes(16);
            if (name.Length != count || id.Length != 16) { throw new IOException("Truncated owned-file record."); }
            string target = Encoding.ASCII.GetString(name);
            long size = reader.ReadInt64();
            FileStage stage = (FileStage)reader.ReadByte();
            byte finalName = reader.ReadByte();
            byte hasIdentity = reader.ReadByte();
            if (finalName > 1 || hasIdentity > 1) { throw new IOException("Invalid owned-file flags."); }
            FileIdentity? identity = hasIdentity == 1
                ? new FileIdentity(reader.ReadUInt32(), reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64()) : null;
            return new OwnedFile(target, new Guid(id), size, stage, identity, finalName == 1);
        }

        internal void AppendObject(OwnedFile entry)
        {
            if (_version != 2 || (_cleanup && entry.Stage < FileStage.Deleting))
                throw new IOException("Staging operation cannot create files.");
            using var buffer = new MemoryStream();
            using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write((byte)3);
                writer.Write(entry.Target);
                writer.Write(entry.ObjectId.ToByteArray());
                writer.Write(entry.Size);
                writer.Write((byte)entry.Stage);
                writer.Write((byte)(entry.FinalName ? 1 : 0));
                writer.Write((byte)(entry.Identity is null ? 0 : 1));
                if (entry.Identity is { } identity) { WriteIdentity(writer, identity); }
            }
            Append(buffer.ToArray());
        }

        internal void MarkCleanup()
        {
            if (_cleanup) { return; }
            Append([2]);
            _cleanup = true;
        }

        private void Append(byte[] payload)
        {
            if (_failed) { throw new IOException("Ownership journal requires offline inspection."); }
            try
            {
                if (_stream.Length + payload.Length + 36 > MaximumBytes) { throw new IOException("Ownership journal limit exceeded."); }
                byte[] hash = Digest(_previous, payload);
                using var writer = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: true);
                writer.Write(payload.Length);
                writer.Write(payload);
                writer.Write(hash);
                writer.Flush();
                _stream.Flush(flushToDisk: true);
                _previous = hash;
            }
            catch { _failed = true; throw; }
        }

        private static byte[] Digest(byte[] previous, byte[] payload)
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(previous);
            hash.AppendData(payload);
            return hash.GetHashAndReset();
        }

        private static void WriteIdentity(BinaryWriter writer, FileIdentity identity)
        {
            writer.Write(identity.Attributes);
            writer.Write(identity.Volume);
            writer.Write(identity.IndexHigh);
            writer.Write(identity.IndexLow);
        }

        internal void Delete()
        {
            if (_failed) { throw new IOException("Cannot delete a failed ownership journal."); }
            MarkDelete(_stream.SafeFileHandle);
            Dispose();
        }

        public void Dispose() => _stream.Dispose();
    }
}
