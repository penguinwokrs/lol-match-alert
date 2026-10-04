// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using MatchAlert.Domain;

namespace MatchAlert.Lcu;

/// <summary>
/// Follows the League client's gameflow phase over its local websocket, waiting for the client to
/// start and reconnecting whenever it restarts. Only the phase event is subscribed: it fires once
/// per change, in the same millisecond as the ready check itself (measured 2026-10-04).
/// </summary>
public sealed class LcuGameEvents(
    Func<string?> findLockfile,
    Action<string> log,
    TimeSpan? retryDelay = null,
    TimeSpan? connectTimeout = null) : IGameEvents
{
    private const string ClientProcess = "LeagueClientUx";
    private readonly TimeSpan _retry = retryDelay ?? TimeSpan.FromSeconds(3);
    private readonly TimeSpan _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(10);

    /// <summary>The lockfile next to the running client's executable, or null when it is not running.</summary>
    public static string? FindRunningClientLockfile()
    {
        foreach (var process in Process.GetProcessesByName(ClientProcess))
        {
            using (process)
            {
                try
                {
                    if (process.MainModule?.FileName is { } exe
                        && Path.Combine(Path.GetDirectoryName(exe)!, "lockfile") is var lockfile
                        && File.Exists(lockfile))
                        return lockfile;
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    // Exited while we looked, or not ours to inspect.
                }
            }
        }
        return null;
    }

    public async IAsyncEnumerable<ClientState> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // A producer task does the connecting so errors can be caught freely around it;
        // an iterator cannot yield from inside a try that has a catch.
        var channel = Channel.CreateUnbounded<ClientState>(new UnboundedChannelOptions { SingleReader = true });
        // Its own token, so a consumer that stops reading early also stops the producer.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var producer = Task.Run(() => ProduceAsync(channel.Writer, stop.Token), stop.Token);
        try
        {
            await foreach (var state in channel.Reader.ReadAllAsync(cancellationToken)) yield return state;
        }
        finally
        {
            stop.Cancel();
            try { await producer; } catch (OperationCanceledException) { }
        }
    }

    private async Task ProduceAsync(ChannelWriter<ClientState> writer, CancellationToken ct)
    {
        bool? connected = null;
        void Report(ClientState s)
        {
            if (!s.Connected && connected == false) return;   // say "waiting" once, not every poll
            connected = s.Connected;
            writer.TryWrite(s);
        }

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var lockfile = ReadLockfile();
                if (lockfile is null)
                {
                    Report(ClientState.Disconnected);
                    await Task.Delay(_retry, ct);
                    continue;
                }

                try
                {
                    await FollowAsync(lockfile, Report, ct);
                    log("League client: connection closed");
                }
                catch (Exception e)
                {
                    // Shutting down: whatever failed on the way out no longer matters.
                    if (ct.IsCancellationRequested) break;
                    // Anything else at all: a refused or timed-out connect, a client that died mid
                    // read. Giving up here would leave the app looking alive and never alerting again.
                    log($"League client: {e.GetType().Name}: {e.Message}");
                }
                Report(ClientState.Disconnected);
                await Task.Delay(_retry, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private Lockfile? ReadLockfile()
    {
        if (findLockfile() is not { } path) return null;
        try
        {
            // The client holds it open; share so we can still read it.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return Lockfile.Parse(reader.ReadToEnd());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            return null;   // half-written while the client starts; the next poll gets it
        }
    }

    private async Task FollowAsync(Lockfile lockfile, Action<ClientState> report, CancellationToken ct)
    {
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", $"Basic {lockfile.BasicAuth}");
        // The client serves a self-signed certificate, and only ever on 127.0.0.1.
        socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        using (var connecting = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            // A client that accepts and then hangs must not hang us with it.
            connecting.CancelAfter(_connectTimeout);
            await socket.ConnectAsync(new Uri($"wss://127.0.0.1:{lockfile.Port}/"), connecting.Token);
            await socket.SendAsync(Encoding.UTF8.GetBytes(LcuFrames.Subscribe), WebSocketMessageType.Text, true, connecting.Token);

            // Subscribing only reports changes. Ask for the current phase too, so starting (or
            // reconnecting) in the middle of a ready check still alerts.
            report(new ClientState(true, await GetPhaseAsync(lockfile, connecting.Token)));
        }
        log($"League client: connected on port {lockfile.Port}");

        var buffer = new byte[16 * 1024];
        var message = new MemoryStream();
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return;
            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;

            var frame = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            message.SetLength(0);
            if (LcuFrames.TryParsePhase(frame, out var phase)) report(new ClientState(true, phase));
        }
    }

    private static async Task<string?> GetPhaseAsync(Lockfile lockfile, CancellationToken ct)
    {
        using var handler = new SocketsHttpHandler();
        handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{lockfile.Port}{LcuFrames.PhasePath}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", lockfile.BasicAuth);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        return JsonSerializer.Deserialize<string>(await response.Content.ReadAsStringAsync(ct));
    }
}
