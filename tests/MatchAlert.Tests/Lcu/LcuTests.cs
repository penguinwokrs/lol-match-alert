// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.Domain;
using MatchAlert.Lcu;

namespace MatchAlert.Tests.Lcu;

public class LockfileTests
{
    [Fact]
    public void Reads_the_port_and_password()
    {
        var lf = Lockfile.Parse("LeagueClient:1234:52364:not-a-real-password:https");
        Assert.Equal(52364, lf.Port);
        Assert.Equal("not-a-real-password", lf.Password);
        Assert.Equal("cmlvdDpub3QtYS1yZWFsLXBhc3N3b3Jk", lf.BasicAuth);
    }

    [Theory]
    [InlineData("")]
    [InlineData("LeagueClient:1234:notaport:pw:https")]
    [InlineData("LeagueClient:1234:52364")]
    [InlineData("LeagueClient:1234:52364::https")]
    public void Rejects_a_lockfile_it_cannot_use(string content)
    {
        Assert.Throws<FormatException>(() => Lockfile.Parse(content));
    }
}

public class LcuFramesTests
{
    // Recorded from a live client on 2026-10-04 (ranked solo queue, then a declined ready check).
    [Theory]
    [InlineData("""[8,"OnJsonApiEvent_lol-gameflow_v1_gameflow-phase",{"data":"Matchmaking","eventType":"Update","uri":"/lol-gameflow/v1/gameflow-phase"}]""", "Matchmaking")]
    [InlineData("""[8,"OnJsonApiEvent_lol-gameflow_v1_gameflow-phase",{"data":"ReadyCheck","eventType":"Update","uri":"/lol-gameflow/v1/gameflow-phase"}]""", "ReadyCheck")]
    [InlineData("""[8,"OnJsonApiEvent_lol-gameflow_v1_gameflow-phase",{"data":"Lobby","eventType":"Update","uri":"/lol-gameflow/v1/gameflow-phase"}]""", "Lobby")]
    public void Reads_the_phase_from_a_recorded_event(string frame, string phase)
    {
        Assert.True(LcuFrames.TryParsePhase(frame, out var parsed));
        Assert.Equal(phase, parsed);
    }

    [Theory]
    [InlineData("""[8,"OnJsonApiEvent_lol-matchmaking_v1_ready-check",{"data":{"declinerIds":[],"dodgeWarning":"None","playerResponse":"None","state":"InProgress","suppressUx":false,"timer":0.0},"eventType":"Update","uri":"/lol-matchmaking/v1/ready-check"}]""")]
    [InlineData("""[8,"OnJsonApiEvent_lol-gameflow_v1_gameflow-phase",{"data":null,"eventType":"Delete","uri":"/lol-gameflow/v1/gameflow-phase"}]""")]
    [InlineData("""[5,"OnJsonApiEvent_lol-gameflow_v1_gameflow-phase"]""")]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"data":"ReadyCheck"}""")]
    public void Ignores_everything_else(string frame)
    {
        Assert.False(LcuFrames.TryParsePhase(frame, out _));
    }

    [Fact]
    public void Subscribes_to_the_phase_event()
    {
        Assert.Equal("""[5,"OnJsonApiEvent_lol-gameflow_v1_gameflow-phase"]""", LcuFrames.Subscribe);
    }
}

public class LcuGameEventsTests
{
    [Fact]
    public async Task A_client_that_refuses_connections_is_retried_not_given_up_on()
    {
        // A lockfile whose port nothing listens on: the client is starting up, or has just crashed.
        var lockfile = Path.GetTempFileName();
        File.WriteAllText(lockfile, "LeagueClient:1234:1:not-a-real-password:https");
        var attempts = 0;
        var events = new LcuGameEvents(() => { Interlocked.Increment(ref attempts); return lockfile; }, _ => { }, TimeSpan.FromMilliseconds(20));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var states = new System.Collections.Concurrent.ConcurrentQueue<ClientState>();

        var watching = Task.Run(async () => { await foreach (var s in events.WatchAsync(cts.Token)) states.Enqueue(s); });
        while (Volatile.Read(ref attempts) < 3 && !cts.IsCancellationRequested) await Task.Delay(20);

        Assert.True(attempts >= 3, "kept retrying");
        Assert.False(watching.IsCompleted);
        Assert.Equal([ClientState.Disconnected], states);   // said once, not on every retry
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watching);
        File.Delete(lockfile);
    }

    [Fact]
    public async Task A_client_that_accepts_but_never_answers_is_timed_out_and_retried()
    {
        // Accepts TCP and then says nothing: a hung client. Without a timeout the connect waits forever.
        using var silent = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        silent.Start();
        var port = ((System.Net.IPEndPoint)silent.LocalEndpoint).Port;
        var lockfile = Path.GetTempFileName();
        File.WriteAllText(lockfile, $"LeagueClient:1234:{port}:not-a-real-password:https");
        var attempts = 0;
        var events = new LcuGameEvents(() => { Interlocked.Increment(ref attempts); return lockfile; }, _ => { },
            retryDelay: TimeSpan.FromMilliseconds(20), connectTimeout: TimeSpan.FromMilliseconds(200));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var watching = Task.Run(async () => { await foreach (var _ in events.WatchAsync(cts.Token)) { } });
        while (Volatile.Read(ref attempts) < 3 && !cts.IsCancellationRequested) await Task.Delay(20);

        Assert.True(attempts >= 3, "kept retrying after a connect that never completed");
        Assert.False(watching.IsCompleted);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watching);
        File.Delete(lockfile);
    }
}
