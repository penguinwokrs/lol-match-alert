// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.App;
using MatchAlert.Domain;
using Microsoft.Extensions.Time.Testing;

namespace MatchAlert.Tests.App;

public class AlertServiceTests : IAsyncLifetime
{
    private const string Q1 = "keychron-q1-he-8k";
    private const string Other = "other-board";

    private readonly FakeTimeProvider _time = new();
    private readonly FakeGameEvents _events = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly List<AlertStatus> _statuses = [];
    private ResolvedSettings _settings = Settings();
    private Task _run = Task.CompletedTask;

    private static ResolvedSettings Settings(string? user = null) => SettingsLoader.Load(SettingsSources.BuiltIn() with
    {
        UserSettings = user is null ? null : new SourceText("settings.json", user),
        UserProfiles = [new SourceText("other.json", $$"""
            { "id": "{{Other}}", "driver": "via", "match": { "vendorId": "0x1234" }, "effects": { "solid": 1, "breathing": 2 } }
            """)],
    });

    private AlertService Start(params ILightingDevice[] devices)
    {
        var service = new AlertService(_events, new FakeDeviceSource(devices), () => _settings, _time, _ => { });
        service.StatusChanged += s => { lock (_statuses) _statuses.Add(s); };
        _run = Task.Run(() => service.RunAsync(_cts.Token));
        return service;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _cts.Cancel();
        try { await _run; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task Nothing_lights_up_outside_a_ready_check()
    {
        var q1 = new FakeDevice(Q1);
        Start(q1);
        _events.Push("Lobby");
        _events.Push("Matchmaking");
        await Eventually.True(() => _statuses.Contains(AlertStatus.Connected));
        await Task.Delay(50);
        Assert.Empty(q1.Sessions);
    }

    [Fact]
    public async Task A_ready_check_lights_every_device_and_leaving_it_restores_them()
    {
        var q1 = new FakeDevice(Q1);
        var other = new FakeDevice(Other);
        Start(q1, other);

        _events.Push("ReadyCheck");
        await Eventually.True(() => q1.Last?.Shown.Count > 0 && other.Last?.Shown.Count > 0, "both lit");
        Assert.Equal(Rgb.Parse("#FF0000"), q1.Last!.Shown.First().Color);

        _events.Push("ChampSelect");
        await Eventually.True(() => q1.Last!.Disposed && other.Last!.Disposed, "both restored");
    }

    [Fact]
    public async Task Losing_the_client_mid_alert_restores()
    {
        var q1 = new FakeDevice(Q1);
        Start(q1);
        _events.Push("ReadyCheck");
        await Eventually.True(() => q1.Last?.Shown.Count > 0);

        _events.Push(ClientState.Disconnected);
        await Eventually.True(() => q1.Last!.Disposed);
    }

    [Fact]
    public async Task The_safety_stop_restores_even_if_the_ready_check_never_ends()
    {
        var q1 = new FakeDevice(Q1);
        Start(q1);
        _events.Push("ReadyCheck");
        await Eventually.True(() => q1.Last?.Shown.Count > 0);

        _time.Advance(TimeSpan.FromSeconds(29));
        await Task.Delay(50);
        Assert.False(q1.Last!.Disposed);

        _time.Advance(TimeSpan.FromSeconds(1));
        await Eventually.True(() => q1.Last!.Disposed, "safety stop");
        await Eventually.True(() => _statuses.Last() == AlertStatus.Connected, "status back to connected");
    }

    [Fact]
    public async Task A_disabled_device_is_left_alone()
    {
        _settings = Settings($$"""{ "devices": { "{{Other}}": { "enabled": false } } }""");
        var q1 = new FakeDevice(Q1);
        var other = new FakeDevice(Other);
        Start(q1, other);

        _events.Push("ReadyCheck");
        await Eventually.True(() => q1.Last?.Shown.Count > 0);
        Assert.Empty(other.Sessions);
    }

    [Fact]
    public async Task A_device_that_fails_to_open_does_not_stop_the_others()
    {
        var broken = new FakeDevice(Other, fails: true);
        var q1 = new FakeDevice(Q1);
        Start(broken, q1);

        _events.Push("ReadyCheck");
        await Eventually.True(() => q1.Last?.Shown.Count > 0);
    }

    [Fact]
    public async Task Each_device_plays_its_own_pattern()
    {
        _settings = Settings($$"""{ "devices": { "{{Other}}": { "pattern": "steady" } } }""");
        var q1 = new FakeDevice(Q1);
        var other = new FakeDevice(Other);
        Start(q1, other);

        _events.Push("ReadyCheck");
        await Eventually.True(() => q1.Last?.Shown.Count > 0 && other.Last?.Shown.Count > 0);
        Assert.Equal(Rgb.Parse("#FF0000"), q1.Last!.Shown.First().Color);
        Assert.Equal(Rgb.Parse("#FFB000"), other.Last!.Shown.First().Color);
    }

    [Fact]
    public async Task Status_follows_the_client()
    {
        var q1 = new FakeDevice(Q1);
        Start(q1);
        _events.Push(ClientState.Disconnected);
        _events.Push("Lobby");
        _events.Push("ReadyCheck");
        await Eventually.True(() => q1.Last?.Shown.Count > 0);
        _events.Push("Lobby");
        await Eventually.True(() => q1.Last!.Disposed);
        await Eventually.True(() => _statuses.Count >= 4);

        Assert.Equal([AlertStatus.WaitingForClient, AlertStatus.Connected, AlertStatus.Alerting, AlertStatus.Connected], _statuses);
    }

    [Fact]
    public async Task Stopping_the_service_restores()
    {
        var q1 = new FakeDevice(Q1);
        Start(q1);
        _events.Push("ReadyCheck");
        await Eventually.True(() => q1.Last?.Shown.Count > 0);

        _cts.Cancel();
        try { await _run; } catch (OperationCanceledException) { }
        Assert.True(q1.Last!.Disposed);
    }

    [Fact]
    public async Task The_test_button_lights_and_restores_without_a_client()
    {
        var q1 = new FakeDevice(Q1);
        var service = new AlertService(_events, new FakeDeviceSource(q1), () => _settings, _time, _ => { });

        var test = service.TestAsync(TimeSpan.FromSeconds(3), CancellationToken.None);
        await Eventually.True(() => q1.Last?.Shown.Count > 0);
        // The test's own delay may not exist yet; keep the clock moving until it is over.
        while (!test.IsCompleted)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(10);
        }
        await test;

        Assert.True(q1.Last!.Disposed);
    }
}
