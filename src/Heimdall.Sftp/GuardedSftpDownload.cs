/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
namespace Heimdall.Sftp;

/// <summary>Copies an SFTP file while checking its visible metadata and opened-handle size.</summary>
/// <remarks>These checks do not make SFTP v3 path resolution atomic and do not eliminate TOCTOU.</remarks>
internal static class GuardedSftpDownload
{
    internal static void Copy(string path, long expectedSize, Func<string, SftpFileInfo> stat,
        Func<string, Stream> open, Stream output, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        SftpFileInfo initial = stat(path);
        if (!initial.IsRegularFile || initial.Size != expectedSize)
            throw new IOException("The remote source is not a regular file of the expected size.");
        EnsureSame(initial, stat(path));
        ct.ThrowIfCancellationRequested();
        using Stream input = open(path);
        if (!input.CanSeek || input.Length != expectedSize)
            throw new IOException("The opened file has an unexpected size or cannot be checked.");
        EnsureSame(initial, stat(path));
        byte[] buffer = new byte[81920];
        long copied = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int count = input.Read(buffer, 0, buffer.Length);
            ct.ThrowIfCancellationRequested();
            if (count == 0) { break; }
            if (count > expectedSize - copied) { throw new IOException("The remote file grew during transfer."); }
            output.Write(buffer, 0, count);
            copied += count;
        }
        ct.ThrowIfCancellationRequested();
        if (copied != expectedSize || input.Length != expectedSize)
            throw new IOException("The remote file size changed during transfer.");
        EnsureSame(initial, stat(path));
    }

    private static void EnsureSame(SftpFileInfo initial, SftpFileInfo current)
    {
        if (!current.IsRegularFile || current.Size != initial.Size || current.LastModified != initial.LastModified
            || current.Permissions != initial.Permissions || current.Owner != initial.Owner || current.Group != initial.Group)
            throw new IOException("The remote file changed during transfer.");
    }
}
