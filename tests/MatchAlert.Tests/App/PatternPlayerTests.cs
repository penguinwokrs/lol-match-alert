// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.App;
using MatchAlert.Domain;
using Microsoft.Extensions.Time.Testing;

namespace MatchAlert.Tests.App;

public class PatternPlayerTests
{
    private static readonly Step Red = new(Rgb.Parse("#FF0000"), 100, "solid", null, 300);
    private static readonly Step White = new(Rgb.Parse("#FFFFFF"), 100, "solid", null, 300);

    private readonly FakeTimeProvider _time = new();
    private readonly FakeSession _session = new();
    private readonly CancellationTokenSource _cts = new();

    private Task Play(Pattern p, int minStepMs = 0) =>
        Task.Run(() => PatternPlayer.PlayAsync(_session, p, minStepMs, _time, _cts.Token));

    private async Task Advance(int ms, int expectShown)
    {
        _time.Advance(TimeSpan.FromMilliseconds(ms));
        await Eventually.True(() => _session.Shown.Count >= expectShown, $"{expectShown} steps shown");
    }

    [Fact]
    public async Task Alternates_steps_on_their_durations()
    {
        var play = Play(new Pattern([Red, White], null));
        await Eventually.True(() => _session.Shown.Count == 1);

        await Advance(299, 1);
        Assert.Single(_session.Shown);
        await Advance(1, 2);
        await Advance(300, 3);
        await Advance(300, 4);

        Assert.Equal([Red, White, Red, White], _session.Shown);
        _cts.Cancel();
        await play;
    }

    [Fact]
    public async Task Stretches_steps_shorter_than_the_device_can_show()
    {
        var play = Play(new Pattern([Red, White], null), minStepMs: 500);
        await Eventually.True(() => _session.Shown.Count == 1);

        await Advance(300, 1);
        Assert.Single(_session.Shown);
        await Advance(200, 2);

        _cts.Cancel();
        await play;
    }

    [Fact]
    public async Task A_counted_pattern_holds_its_last_step()
    {
        var play = Play(new Pattern([Red, White], 2));
        await Eventually.True(() => _session.Shown.Count == 1);
        for (int i = 2; i <= 4; i++) await Advance(300, i);

        _time.Advance(TimeSpan.FromSeconds(10));
        await Task.Delay(50);
        Assert.Equal([Red, White, Red, White], _session.Shown);
        Assert.False(play.IsCompleted);

        _cts.Cancel();
        await play;
    }

    [Fact]
    public async Task A_single_step_is_written_once_and_left_to_the_firmware()
    {
        var play = Play(new Pattern([Red with { DurationMs = 0, Effect = "breathing" }], null));
        await Eventually.True(() => _session.Shown.Count == 1);

        _time.Advance(TimeSpan.FromSeconds(10));
        await Task.Delay(50);
        Assert.Single(_session.Shown);

        _cts.Cancel();
        await play;
    }

    [Fact]
    public async Task Cancelling_ends_playback_without_throwing()
    {
        var play = Play(new Pattern([Red, White], null));
        await Eventually.True(() => _session.Shown.Count == 1);
        _cts.Cancel();
        await play;
        Assert.True(play.IsCompletedSuccessfully);
    }
}
