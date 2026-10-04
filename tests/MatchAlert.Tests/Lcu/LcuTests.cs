// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

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
