/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using Heimdall.Core.Ssh;
using Heimdall.Ssh;
using Renci.SshNet;

namespace Heimdall.Sftp;

public sealed partial class SftpBrowser
{
    // One metadata client and at most one sequential data client per browser.
    private readonly SemaphoreSlim _transferLock = new(1, 1);
    private TransferContext? _transferContext;

    private sealed class TransferContext(SftpClient source, SshConnectionParams parameters, PinnedFingerprintVerifier verifier)
    {
        internal readonly SftpClient Source = source;
        internal readonly SshConnectionParams Parameters = parameters;
        internal readonly PinnedFingerprintVerifier Verifier = verifier;
        internal readonly CancellationTokenSource Stop = new();
        internal TransferConnection? Connection;
    }

    private sealed class TransferConnection(SftpClient client)
    {
        internal readonly SftpClient Client = client;
        private readonly Lazy<Task> _close = new(() => Task.Run(() =>
        {
            try { SshConnectionFactory.AbortSftpClient(client); }
            catch (Exception) { Heimdall.Core.Logging.FileLogger.Warn("An isolated SFTP connection failed while closing."); }
        }));
        internal Task CloseAsync() => _close.Value;
    }

    private sealed class TransferLease(TransferContext context, TransferConnection connection,
        CancellationTokenSource cancellation, SemaphoreSlim gate) : IAsyncDisposable
    {
        internal readonly TransferConnection Connection = connection;
        internal CancellationToken Token => cancellation.Token;
        internal bool Failed;
        private int _ended;
        private CancellationTokenRegistration _registration;

        internal void ObserveCancellation()
            => _registration = Token.Register(() => _ = AbortAfterGraceAsync());

        private async Task AbortAfterGraceAsync()
        {
            // Give the copy loop and upload rollback a chance to finish before closing
            // the isolated socket. This callback never blocks the cancelling caller.
            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            if (Volatile.Read(ref _ended) == 0) { await Connection.CloseAsync().ConfigureAwait(false); }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _ended, 1) != 0) { return; }
            try
            {
                await _registration.DisposeAsync().ConfigureAwait(false);
                if (Failed || Token.IsCancellationRequested || !Connection.Client.IsConnected)
                {
                    context.Connection = null;
                    await Connection.CloseAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                cancellation.Dispose();
                gate.Release();
            }
        }
    }

    private async Task<TransferLease> OpenTransferAsync(CancellationToken ct)
    {
        TransferContext context;
        await _clientLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            SftpClient source = GetConnectedClient();
            context = Volatile.Read(ref _transferContext)
                ?? throw new InvalidOperationException("The SFTP transfer session is closed.");
            if (!ReferenceEquals(context.Source, source))
                throw new InvalidOperationException("The SFTP transfer session changed.");
        }
        finally { _clientLock.Release(); }

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, context.Stop.Token);
        try { await _transferLock.WaitAsync(cancellation.Token).ConfigureAwait(false); }
        catch { cancellation.Dispose(); throw; }

        TransferLease? lease = null;
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (context.Connection is null)
            {
                SftpClient client = SshConnectionFactory.CreateSftpClient(context.Parameters);
                context.Connection = new TransferConnection(client);
            }
            TransferConnection connection = context.Connection;
            lease = new TransferLease(context, connection, cancellation, _transferLock);
            lease.ObserveCancellation();
            if (!connection.Client.IsConnected)
            {
                SshConnectionFactory.AttachPinnedHostKeyVerification(connection.Client, context.Parameters, context.Verifier);
                if (context.Parameters.KeepAliveIntervalSeconds is > 0)
                    connection.Client.KeepAliveInterval = TimeSpan.FromSeconds(context.Parameters.KeepAliveIntervalSeconds.Value);
                await SshConnectionFactory.ConnectWithCancellationAsync(connection.Client, cancellation.Token).ConfigureAwait(false);
            }
            cancellation.Token.ThrowIfCancellationRequested();
            return lease;
        }
        catch
        {
            if (lease is not null)
            {
                lease.Failed = true;
                await lease.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                cancellation.Dispose();
                _transferLock.Release();
            }
            throw;
        }
    }

    private void StopTransfers(TransferContext? context)
    {
        if (context is not null) { _ = CloseTransfersAsync(context); }
    }

    private async Task CloseTransfersAsync(TransferContext context)
    {
        Task cancellation = context.Stop.CancelAsync();
        await _transferLock.WaitAsync().ConfigureAwait(false);
        try
        {
            try { await cancellation.ConfigureAwait(false); }
            catch (Exception) { Heimdall.Core.Logging.FileLogger.Warn("An SFTP transfer cancellation callback failed."); }
            if (context.Connection is { } connection)
            {
                context.Connection = null;
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
        finally { _transferLock.Release(); }
    }
}
