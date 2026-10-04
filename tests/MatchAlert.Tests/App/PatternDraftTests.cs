// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.App;
using MatchAlert.Domain;

namespace MatchAlert.Tests.App;

public class PatternDraftTests
{
    private static readonly Pattern Blink = new([
        new Step(Rgb.Parse("#FF0000"), 100, "solid", null, 300),
        new Step(Rgb.Parse("#FFFFFF"), 80, "solid", null, 200),
    ], null);

    [Fact]
    public void Round_trips_a_pattern()
    {
        var draft = PatternDraft.From("blink", Blink);
        Assert.Equal(Blink, draft.ToPattern(), PatternComparer.Instance);
        Assert.Equal("blink", draft.Name);
    }

    [Fact]
    public void A_single_step_has_no_duration_and_gets_one_when_a_second_is_added()
    {
        var draft = PatternDraft.From("one", new Pattern([new Step(Rgb.Parse("#00FF00"), 100, "breathing", 120, 0)], 2));
        Assert.Equal(0, draft.ToPattern().Steps[0].DurationMs);

        int added = draft.Add(after: 0);
        var p = draft.ToPattern();
        Assert.Equal(1, added);
        Assert.Equal(2, p.Steps.Count);
        Assert.All(p.Steps, s => Assert.True(s.DurationMs >= PatternDraft.MinMs));
        Assert.Equal(p.Steps[0].Color, p.Steps[1].Color);   // a new step starts as a copy of the one before
        Assert.Equal(2, p.RepeatCount);
    }

    [Theory]
    [InlineData(10, 50)]
    [InlineData(74, 50)]
    [InlineData(76, 100)]
    [InlineData(333, 350)]
    [InlineData(99999, 10000)]
    public void Durations_snap_to_50_ms_within_limits(int asked, int expected)
    {
        var draft = PatternDraft.From("blink", Blink);
        draft.SetDuration(0, asked);
        Assert.Equal(expected, draft.Steps[0].DurationMs);
    }

    [Fact]
    public void Moves_and_removes_but_never_the_last_step()
    {
        var draft = PatternDraft.From("blink", Blink);
        draft.Move(0, 1);
        Assert.Equal(Rgb.Parse("#FFFFFF"), draft.Steps[0].Color);

        draft.Remove(0);
        Assert.Single(draft.Steps);
        Assert.False(draft.CanRemove);
        Assert.Throws<InvalidOperationException>(() => draft.Remove(0));
    }

    [Fact]
    public void Steps_at_real_time_loop_until_stopped()
    {
        var draft = PatternDraft.From("blink", Blink);
        Assert.Equal(0, draft.StepAt(TimeSpan.FromMilliseconds(0)).Index);
        Assert.Equal(0, draft.StepAt(TimeSpan.FromMilliseconds(299)).Index);
        Assert.Equal(1, draft.StepAt(TimeSpan.FromMilliseconds(300)).Index);
        Assert.Equal(0, draft.StepAt(TimeSpan.FromMilliseconds(500)).Index);   // 300 + 200: round two
        Assert.Equal(0.5, draft.StepAt(TimeSpan.FromMilliseconds(150)).Phase, 3);
    }

    [Fact]
    public void Steps_stretch_to_the_device_minimum_and_a_counted_pattern_holds_its_last_step()
    {
        var draft = PatternDraft.From("blink", Blink with { RepeatCount = 2 });
        Assert.Equal(0, draft.StepAt(TimeSpan.FromMilliseconds(250), minStepMs: 1000).Index);
        Assert.Equal(1, draft.StepAt(TimeSpan.FromMilliseconds(1000), minStepMs: 1000).Index);
        Assert.Equal(1, draft.StepAt(TimeSpan.FromSeconds(60)).Index);   // after 2 rounds: hold the last
    }

    private sealed class PatternComparer : IEqualityComparer<Pattern>
    {
        public static readonly PatternComparer Instance = new();
        public bool Equals(Pattern? a, Pattern? b) => a!.RepeatCount == b!.RepeatCount && a.Steps.SequenceEqual(b.Steps);
        public int GetHashCode(Pattern p) => p.Steps.Count;
    }
}

public class PatternCheckTests
{
    private static readonly ResolvedSettings Settings = SettingsLoader.Load(SettingsSources.BuiltIn());
    private static DeviceProfile Profile(string id) => Settings.Profiles.Single(p => p.Id == id);

    [Fact]
    public void Flags_steps_shorter_than_the_device_shows()
    {
        var pattern = new Pattern([
            new Step(Rgb.Parse("#FF0000"), 100, "solid", null, 300),
            new Step(Rgb.Parse("#000000"), 100, "solid", null, 2000),
        ], null);

        var notes = PatternCheck.For(pattern, Profile("pulsar-pcmk-2he-tkl"));   // minStepMs 1000
        var note = Assert.Single(notes);
        Assert.Equal((PatternNoteKind.StretchedStep, 0), (note.Kind, note.StepIndex));
    }

    [Fact]
    public void Flags_effects_a_device_lacks_and_breathing_shown_steady()
    {
        var breathing = new Pattern([new Step(Rgb.Parse("#FFB000"), 100, "breathing", 200, 0)], null);
        var custom = new Pattern([new Step(Rgb.Parse("#FFB000"), 100, "rainbow", null, 0)], null);

        Assert.Contains(PatternCheck.For(custom, Profile("keychron-q1-he-8k")), n => n.Kind == PatternNoteKind.MissingEffect);
        Assert.Contains(PatternCheck.For(breathing, Profile("razer-chroma")), n => n.Kind == PatternNoteKind.BreathingShownSteady);
        Assert.Empty(PatternCheck.For(breathing, Profile("keychron-q1-he-8k")));
    }

    [Fact]
    public void Only_the_q1_he_is_marked_verified()
    {
        Assert.Equal(["keychron-q1-he-8k"], Settings.Profiles.Where(p => p.Verified).Select(p => p.Id));
    }
}
