// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.Domain;

namespace MatchAlert.App;

public enum PatternNoteKind
{
    /// <summary>Shorter than the device's minStepMs: it plays, stretched.</summary>
    StretchedStep,

    /// <summary>The device has no such effect: it will not load for this device, and previews steady.</summary>
    MissingEffect,

    /// <summary>The integration has no breathing (Razer, OpenRGB): it shows steady.</summary>
    BreathingShownSteady,
}

public sealed record PatternNote(PatternNoteKind Kind, int StepIndex);

/// <summary>What the editor should say about a pattern on a particular device, before anyone sees it play.</summary>
public static class PatternCheck
{
    /// <summary>Integrations whose "breathing" is mapped to a steady color.</summary>
    private static readonly HashSet<string> NoBreathing = new(StringComparer.Ordinal) { "razer-chroma", "openrgb" };

    public static IReadOnlyList<PatternNote> For(Pattern pattern, DeviceProfile profile)
    {
        var notes = new List<PatternNote>();
        for (int i = 0; i < pattern.Steps.Count; i++)
        {
            var step = pattern.Steps[i];
            if (!profile.Effects.ContainsKey(step.Effect)) notes.Add(new(PatternNoteKind.MissingEffect, i));
            else if (step.Effect == "breathing" && NoBreathing.Contains(profile.Driver)) notes.Add(new(PatternNoteKind.BreathingShownSteady, i));
            if (pattern.Steps.Count > 1 && step.DurationMs < profile.MinStepMs) notes.Add(new(PatternNoteKind.StretchedStep, i));
        }
        return notes;
    }
}
