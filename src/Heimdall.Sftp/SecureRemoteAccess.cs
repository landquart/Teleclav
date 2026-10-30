/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.Text;
using Heimdall.Core.Ssh;
using Heimdall.Ssh;
using Renci.SshNet;

namespace Heimdall.Sftp;

/// <summary>Linux file access helper: every path component is opened without following links.</summary>
internal static class SecureRemoteAccess
{
    internal const string Script = """
import base64, json, os, stat, struct, sys
def send(chunk):
    if sys.stdin.buffer.read(1) != b'R':
        raise RuntimeError('transfer request is missing')
    sys.stdout.buffer.write(struct.pack('<I', len(chunk)))
    sys.stdout.buffer.write(chunk)
    sys.stdout.buffer.flush()
mode, encoded, limit = sys.argv[1:4]
path = base64.b64decode(encoded, validate=True).decode('utf-8', 'strict')
parts = path.split('/')
if not path.startswith('/') or len(parts) < 2 or any(p in ('', '.', '..') for p in parts[1:]):
    raise ValueError('invalid absolute path')
if not all(hasattr(os, n) for n in ('O_PATH', 'O_NOFOLLOW', 'O_DIRECTORY')):
    raise RuntimeError('safe file access is unavailable')
dirfd = os.open('/', os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
try:
    for part in parts[1:-1]:
        nextfd = os.open(part, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=dirfd)
        os.close(dirfd)
        dirfd = nextfd
    if mode == 'list':
        target = os.open(parts[-1], os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=dirfd)
        try:
            entries = []
            with os.scandir(target) as it:
                for entry in it:
                    if len(entries) >= 10000:
                        raise ValueError('too many directory entries')
                    entry.name.encode('utf-8', 'strict')
                    st = os.stat(entry.name, dir_fd=target, follow_symlinks=False)
                    kind = ('file' if stat.S_ISREG(st.st_mode) else
                            'directory' if stat.S_ISDIR(st.st_mode) else
                            'link' if stat.S_ISLNK(st.st_mode) else 'other')
                    entries.append([entry.name, kind, st.st_size, int(st.st_mtime)])
            encoded_entries = json.dumps(entries, ensure_ascii=True, separators=(',', ':')).encode('utf-8')
            for offset in range(0, len(encoded_entries), 65536):
                send(encoded_entries[offset:offset+65536])
            send(b'')
        finally:
            os.close(target)
    elif mode == 'read':
        handle = os.open(parts[-1], os.O_PATH | os.O_NOFOLLOW, dir_fd=dirfd)
        try:
            info = os.fstat(handle)
            if not stat.S_ISREG(info.st_mode):
                raise ValueError('not a regular file')
            maxbytes = int(limit)
            if maxbytes < 0 or info.st_size > maxbytes:
                raise ValueError('file exceeds transfer limit')
            opened = os.open('/proc/self/fd/' + str(handle), os.O_RDONLY | os.O_NONBLOCK)
            try:
                current = os.fstat(opened)
                if not stat.S_ISREG(current.st_mode) or (current.st_dev, current.st_ino) != (info.st_dev, info.st_ino):
                    raise ValueError('opened file changed')
                count = 0
                while True:
                    chunk = os.read(opened, 65536)
                    if not chunk:
                        break
                    count += len(chunk)
                    if count > maxbytes:
                        raise ValueError('file exceeds transfer limit')
                    send(chunk)
                send(b'')
            finally:
                os.close(opened)
        finally:
            os.close(handle)
    else:
        raise ValueError('unsupported operation')
finally:
    os.close(dirfd)
""";

    internal static string BuildCommand(string mode, string path, long limit)
    {
        if (mode is not ("list" or "read")) { throw new ArgumentException("Unknown operation.", nameof(mode)); }
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string script = Convert.ToBase64String(Encoding.UTF8.GetBytes(Script));
        string encoded = Convert.ToBase64String(new UTF8Encoding(false, true).GetBytes(path));
        return $"python3 -c 'import base64;exec(base64.b64decode(\"{script}\"))' {mode} {encoded} {limit.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }
    internal static async Task RunAsync(SshConnectionParams connection, PinnedFingerprintVerifier verifier,
        string mode, string path, long limit, Stream destination, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using SshClient client = SshConnectionFactory.CreateSshClient(connection);
        SshConnectionFactory.AttachPinnedHostKeyVerification(client, connection, verifier);
        using CancellationTokenRegistration registration = ct.Register(static state => ((SshClient)state!).Dispose(), client);
        try
        {
            await SshConnectionFactory.ConnectWithCancellationAsync(client, ct).ConfigureAwait(false);
            using SshCommand command = client.CreateCommand(BuildCommand(mode, path, limit));
            Task running = command.ExecuteAsync(ct);
            Task<string> errors = DrainErrorsAsync(command.ExtendedOutputStream, ct);
            try
            {
                using Stream input = command.CreateInputStream();
                Stream output = command.OutputStream;
                byte[] request = [(byte)'R'];
                byte[] header = new byte[4];
                byte[] data = new byte[65536];
                while (true)
                {
                    await input.WriteAsync(request, ct).ConfigureAwait(false);
                    await input.FlushAsync(ct).ConfigureAwait(false);
                    await output.ReadExactlyAsync(header, ct).ConfigureAwait(false);
                    uint size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header);
                    if (size > data.Length) { throw new IOException("Invalid secure transfer frame."); }
                    if (size == 0) { break; }
                    await output.ReadExactlyAsync(data.AsMemory(0, (int)size), ct).ConfigureAwait(false);
                    await destination.WriteAsync(data.AsMemory(0, (int)size), ct).ConfigureAwait(false);
                }
                input.Dispose();
                await running.ConfigureAwait(false);
                string error = await errors.ConfigureAwait(false);
                if (command.ExitStatus != 0)
                    throw new IOException($"Secure remote access failed (SSH exec and python3 are required): {error}");
            }
            catch (Exception ex)
            {
                client.Dispose();
                try { await running.ConfigureAwait(false); } catch { }
                string error = "";
                try { error = await errors.ConfigureAwait(false); } catch { }
                if (ex is EndOfStreamException && !ct.IsCancellationRequested)
                    throw new IOException($"Secure remote access failed (SSH exec and python3 are required): {error}", ex);
                throw;
            }
        }
        catch (Exception ex) when (ct.IsCancellationRequested && ex is not OperationCanceledException)
        {
            throw new OperationCanceledException("Secure remote access was cancelled.", ex, ct);
        }
    }

    private static async Task<string> DrainErrorsAsync(Stream stream, CancellationToken ct)
    {
        byte[] buffer = new byte[4096];
        using MemoryStream stored = new();
        int count;
        while ((count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
        {
            int take = Math.Min(count, (int)(8192 - stored.Length));
            if (take > 0) { stored.Write(buffer, 0, take); }
        }
        return Encoding.UTF8.GetString(stored.ToArray());
    }
}
