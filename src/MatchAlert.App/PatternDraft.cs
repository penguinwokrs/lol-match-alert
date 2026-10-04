// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.Domain;

namespace MatchAlert.App;

/// <summary>One step as the editor holds it: mutable, unlike <see cref="Step"/>.</summary>
public sealed class StepDraft
{
    public Rgb Color { get; set; } = Rgb.Parse("#FF0000");
    public int Brightness { get; set; } = 100;
    public string Effect { get; set; } = Step.Solid;
    public byte? Speed { get; set; }
    public int DurationMs { get; set; } = 300;

    public StepDraft Copy() => (StepDraft)MemberwiseClone();
}

/// <summary>
/// The pattern being edited. Everything the editor window does to a pattern goes through here, so it can
/// be tested without a window: adding, removing, moving and resizing steps, and working out which step
/// shows at a given moment for the on-screen preview.
/// </summary>
public sealed class PatternDraft
{
    public const int SnapMs = 50, MinMs = 50, MaxMs = 10_000, NewStepMs = 300;

    /// <summary>How long one breath of a breathing step takes in the on-screen preview.</summary>
    public static readonly TimeSpan BreathPeriod = TimeSpan.FromSeconds(2);

    public required string Name { get; set; }
    public List<StepDraft> Steps { get; } = [];

    /// <summary>Null: until the alert ends. A count: that many rounds, then the last step holds.</summary>
    public int? RepeatCount { get; set; }

    public bool CanRemove => Steps.Count > 1;

    public static PatternDraft From(string name, Pattern pattern)
    {
        var draft = new PatternDraft { Name = name, RepeatCount = pattern.RepeatCount };
        draft.Steps.AddRange(pattern.Steps.Select(s => new StepDraft
        {
            Color = s.Color,
            Brightness = s.Brightness,
            Effect = s.Effect,
            Speed = s.Speed,
            DurationMs = s.DurationMs > 0 ? s.DurationMs : NewStepMs,
        }));
        return draft;
    }

    public PatternDraft Copy(string name)
    {
        var copy = new PatternDraft { Name = name, RepeatCount = RepeatCount };
        copy.Steps.AddRange(Steps.Select(s => s.Copy()));
        return copy;
    }

    /// <summary>A single step has no duration: the keyboard holds it, so the firmware can animate it.</summary>
    public Pattern ToPattern() => new(
        Steps.Select(s => new Step(s.Color, s.Brightness, s.Effect, s.Speed, Steps.Count == 1 ? 0 : Snap(s.DurationMs))).ToList(),
        RepeatCount);

    /// <summary>Adds a copy of the step at <paramref name="after"/> just after it. Returns the new step's index.</summary>
    public int Add(int after)
    {
        var source = Steps.Count == 0 ? new StepDraft() : Steps[Math.Clamp(after, 0, Steps.Count - 1)].Copy();
        if (source.DurationMs < MinMs) source.DurationMs = NewStepMs;
        int index = Steps.Count == 0 ? 0 : Math.Clamp(after, 0, Steps.Count - 1) + 1;
        Steps.Insert(index, source);
        return index;
    }

    public void Remove(int index)
    {
        if (!CanRemove) throw new InvalidOperationException("A pattern needs at least one step.");
        Steps.RemoveAt(index);
    }

    public void Move(int from, int to)
    {
        var step = Steps[from];
        Steps.RemoveAt(from);
        Steps.Insert(Math.Clamp(to, 0, Steps.Count), step);
    }

    public void SetDuration(int index, int ms) => Steps[index].DurationMs = Snap(ms);

    public static int Snap(int ms) =>
        Math.Clamp((int)Math.Round(ms / (double)SnapMs, MidpointRounding.AwayFromZero) * SnapMs, MinMs, MaxMs);

    /// <summary>
    /// Which step shows <paramref name="elapsed"/> into the pattern, and how far through it (0-1), the way
    /// the player would show it on a device whose shortest step is <paramref name="minStepMs"/>. A single step
    /// reports its phase through one breath, for drawing a breathing effect.
    /// </summary>
    public (int Index, double Phase) StepAt(TimeSpan elapsed, int minStepMs = 0)
    {
        if (Steps.Count == 1)
            return (0, elapsed.TotalMilliseconds % BreathPeriod.TotalMilliseconds / BreathPeriod.TotalMilliseconds);

        var lengths = Steps.Select(s => (double)Math.Max(Snap(s.DurationMs), minStepMs)).ToArray();
        double round = lengths.Sum(), t = elapsed.TotalMilliseconds;
        if (RepeatCount is int count && t >= round * count) return (Steps.Count - 1, 1);

        t %= round;
        for (int i = 0; i < lengths.Length; i++)
        {
            if (t < lengths[i]) return (i, t / lengths[i]);
            t -= lengths[i];
        }
        return (Steps.Count - 1, 1);
    }
}
