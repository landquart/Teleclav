/* Copyright 2026. Licensed under the Apache License, Version 2.0. */
using System.Net;
using System.Net.Sockets;

namespace Heimdall.Sftp.Tests;

/// <summary>Loopback byte relay; SSH authentication and pinned host keys remain end to end.</summary>
internal sealed class PausableSftpRelay : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _connections = [];
    private readonly Task _accept;
    private readonly int _targetPort;
    private int _connectionCount;
    internal int ConnectionCount => Volatile.Read(ref _connectionCount);
    internal int PausedConnection;
    internal volatile bool PauseDataReplies;
    internal TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource ResumeData { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    internal PausableSftpRelay(int targetPort)
    {
        _targetPort = targetPort;
        _listener.Start();
        _accept = AcceptAsync();
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _connections.Add(RelayAsync(client, Interlocked.Increment(ref _connectionCount)));
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }

    private async Task RelayAsync(TcpClient client, int number)
    {
        using (client)
        using (var server = new TcpClient())
        using (var connectionStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            try
            {
                await server.ConnectAsync(IPAddress.Loopback, _targetPort, connectionStop.Token);
                NetworkStream source = server.GetStream();
                NetworkStream destination = client.GetStream();
                Task upload = destination.CopyToAsync(source, connectionStop.Token);
                Task download = ReplyAsync(source, destination, number, connectionStop.Token);
                await Task.WhenAny(upload, download);
                await connectionStop.CancelAsync();
                await Task.WhenAll(upload, download);
            }
            catch (OperationCanceledException) when (connectionStop.IsCancellationRequested) { }
            catch (IOException) { } // Expected when the isolated client aborts its socket.
            catch (SocketException) { }
        }
    }

    private async Task ReplyAsync(Stream source, Stream destination, int number, CancellationToken ct)
    {
        byte[] buffer = new byte[16 * 1024];
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) != 0)
        {
            // The test selects the active data connection after authentication/probing.
            if (number == Volatile.Read(ref PausedConnection) && PauseDataReplies)
            {
                Blocked.TrySetResult();
                await ResumeData.Task.WaitAsync(ct);
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _accept.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(_connections).WaitAsync(TimeSpan.FromSeconds(5));
        _stop.Dispose();
    }
}
