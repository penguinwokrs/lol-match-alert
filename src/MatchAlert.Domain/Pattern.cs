// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

namespace MatchAlert.Domain;

/// <summary>
/// One frame of a pattern. <paramref name="Brightness"/> is a percentage; <paramref name="Effect"/>
/// is a logical name ("solid", "breathing") that each device profile maps to its own number.
/// </summary>
public sealed record Step(Rgb Color, int Brightness, string Effect, byte? Speed, int DurationMs)
{
    public const string Solid = "solid";

    /// <summary>The 0-255 brightness to write: the percentage, scaled by the color's own value.</summary>
    public byte DeviceBrightness =>
        (byte)Math.Round(Brightness / 100.0 * Color.ToHsv().V, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Steps played in order. <paramref name="RepeatCount"/> null loops until the alert stops; a count
/// plays the steps that many times and then holds the last one.
/// </summary>
public sealed record Pattern(IReadOnlyList<Step> Steps, int? RepeatCount);

/// <summary>What the League client is doing, as far as the alert cares.</summary>
public sealed record ClientState(bool Connected, string? Phase)
{
    public const string ReadyCheckPhase = "ReadyCheck";

    public static ClientState Disconnected { get; } = new(false, null);

    /// <summary>The ready-check dialog is up: the moment to alert.</summary>
    public bool IsReadyCheck => Connected && Phase == ReadyCheckPhase;
}
