// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.App;
using MatchAlert.Domain;

namespace MatchAlert.Tests.App;

public class PreviewTests : IAsyncLifetime
{
    private const string Q1 = "keychron-q1-he-8k";
    private readonly CountingTimeProvider _time = new();
    private readonly FakeGameEvents _events = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly FakeDevice _q1 = new(Q1);
    private readonly ResolvedSettings _settings = SettingsLoader.Load(SettingsSources.BuiltIn());
    private AlertService _service = null!;
    private Task _run = Task.CompletedTask;

    private static Pattern Solid(string hex) => new([new Step(Rgb.Parse(hex), 100, "solid", null, 0)], null);

    public Task InitializeAsync()
    {
        _service = new AlertService(_events, new FakeDeviceSource(_q1), () => _settings, _time, _ => { });
        _run = Task.Run(() => _service.RunAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _cts.Cancel();
        try { await _run; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task Plays_on_one_session_through_every_edit_and_restores_when_stopped()
    {
        var preview = await _service.StartPreviewAsync(_q1, Solid("#FF0000"));
        Assert.NotNull(preview);
        await Eventually.True(() => _q1.Last?.Shown.Count == 1);

        preview.Update(Solid("#00FF00"));
        preview.Update(Solid("#0000FF"));
        await Eventually.True(() => _q1.Last!.Shown.LastOrDefault()?.Color == Rgb.Parse("#0000FF"), "last edit shown");
        Assert.Single(_q1.Sessions);
        Assert.False(_q1.Last!.Disposed);

        await preview.StopAsync();
        Assert.True(_q1.Last!.Disposed);
    }

    [Fact]
    public async Task A_real_match_ends_the_preview_and_plays_the_alert()
    {
        var preview = await _service.StartPreviewAsync(_q1, Solid("#00FF00"));
        var ended = new TaskCompletionSource();
        preview!.Ended += () => ended.TrySetResult();
        await Eventually.True(() => _q1.Last?.Shown.Count == 1);

        _events.Push("ReadyCheck");

        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Eventually.True(() => _q1.Sessions.Count == 2 && _q1.Sessions[0].Disposed && _q1.Sessions[1].Shown.Count > 0, "alert took over");
        Assert.Equal(Rgb.Parse("#FF0000"), _q1.Sessions[1].Shown.First().Color);   // the real pattern, not the preview
    }

    [Fact]
    public async Task No_preview_starts_while_an_alert_plays()
    {
        _events.Push("ReadyCheck");
        await Eventually.True(() => _q1.Last?.Shown.Count > 0);
        Assert.Null(await _service.StartPreviewAsync(_q1, Solid("#00FF00")));
        Assert.Single(_q1.Sessions);
    }

    [Fact]
    public async Task An_effect_the_device_lacks_previews_steady()
    {
        var rainbow = new Pattern([new Step(Rgb.Parse("#FF00FF"), 100, "rainbow", null, 0)], null);
        var preview = await _service.StartPreviewAsync(_q1, rainbow);
        await Eventually.True(() => _q1.Last?.Shown.Count == 1);
        Assert.Equal("solid", _q1.Last!.Shown.Single().Effect);
        await preview!.StopAsync();
    }

    [Fact]
    public async Task Stopping_twice_or_after_the_alert_took_over_is_harmless()
    {
        var preview = await _service.StartPreviewAsync(_q1, Solid("#00FF00"));
        await preview!.StopAsync();
        await preview.StopAsync();
        preview.Update(Solid("#FFFFFF"));   // ignored once stopped
        await Task.Delay(50);
        Assert.Single(_q1.Last!.Shown);
    }
}
