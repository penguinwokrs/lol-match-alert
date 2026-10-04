// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.Domain;

namespace MatchAlert.App;

/// <summary>Plays a pattern on one device until cancelled.</summary>
public static class PatternPlayer
{
    /// <summary>
    /// Shows each step for its duration, stretched to <paramref name="minStepMs"/> when the device
    /// cannot go faster. A single-step pattern is written once and left to the firmware (that is
    /// how a breathing effect animates). A counted pattern holds its last step once done.
    /// Returns normally when cancelled.
    /// </summary>
    public static async Task PlayAsync(ILightingSession session, Pattern pattern, int minStepMs,
        TimeProvider time, CancellationToken cancellationToken)
    {
        try
        {
            if (pattern.Steps.Count == 1)
            {
                session.Show(pattern.Steps[0]);
                await HoldAsync(time, cancellationToken);
                return;
            }

            for (int round = 1; ; round++)
            {
                foreach (var step in pattern.Steps)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    session.Show(step);
                    var duration = TimeSpan.FromMilliseconds(Math.Max(step.DurationMs, minStepMs));
                    await Task.Delay(duration, time, cancellationToken);
                }
                if (pattern.RepeatCount is int count && round >= count)
                {
                    await HoldAsync(time, cancellationToken);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static Task HoldAsync(TimeProvider time, CancellationToken cancellationToken) =>
        Task.Delay(Timeout.InfiniteTimeSpan, time, cancellationToken);
}
