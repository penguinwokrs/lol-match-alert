// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Diagnostics;
using MatchAlert.App;
using MatchAlert.Devices.Via;
using MatchAlert.Domain;

namespace MatchAlert.Tray;

/// <summary>
/// <c>lol-match-alert.exe --test [seconds]</c>: plays each connected keyboard's pattern headless, restores
/// it, reads the lighting back and compares it with what was there before. Exit code 0 when every board
/// came back as it was. How a restore is verified without looking at the keyboard.
/// </summary>
internal static class TestCommand
{
    public static int Run(string[] args, FileLog log)
    {
        int seconds = args.SkipWhile(a => !a.Equals("--test", StringComparison.OrdinalIgnoreCase)).Skip(1)
            .Select(a => int.TryParse(a, out int n) ? n : 0).FirstOrDefault() is > 0 and var s ? s : 3;

        ResolvedSettings settings;
        try { settings = SettingsLoader.Load(SettingsSources.FromDisk(AppPaths.SettingsDirectory)); }
        catch (SettingsException e)
        {
            log.Write($"Settings: {e.Message}");
            return 3;
        }

        var (source, via) = Program.Devices(() => settings, log);
        source.RecoverInterruptedSessions();
        var found = source.Recognised();
        if (found.Count == 0)
        {
            log.Write("No set-up keyboard is connected.");
            foreach (var d in source.Unrecognised()) log.Write($"  could be set up: {d}");
            return 2;
        }

        int failures = 0;
        foreach (var (hid, profile) in found.Where(f => f.Profile.Driver == "via"))
        {
            var options = ViaOptions.From(profile);
            ViaSnapshot before;
            using (var kb = via.Open(hid, options))
            {
                before = kb.Snapshot();
                log.Write($"{profile.Name}: VIA protocol {kb.Protocol}, channel {options.Channel}, resetOnEffect {options.ResetOnEffect}");
            }
            log.Write($"{profile.Name}: before  {before}");

            var pattern = settings.PatternFor(profile);
            var writes = new List<double>();
            using (var session = via.Create(hid, profile).OpenSession())
            {
                var clock = Stopwatch.StartNew();
                for (int i = 0; clock.Elapsed < TimeSpan.FromSeconds(seconds); i++)
                {
                    var step = pattern.Steps[i % pattern.Steps.Count];
                    var t = Stopwatch.StartNew();
                    session.Show(step);
                    writes.Add(t.Elapsed.TotalMilliseconds);
                    var wait = Math.Max(step.DurationMs, profile.MinStepMs) - (int)t.ElapsedMilliseconds;
                    if (pattern.Steps.Count == 1) wait = (int)(TimeSpan.FromSeconds(seconds) - clock.Elapsed).TotalMilliseconds;
                    if (wait > 0) Thread.Sleep(wait);
                }
            }
            log.Write($"{profile.Name}: played {settings.PatternNameFor(profile)} for {seconds} s, {writes.Count} steps; " +
                $"step write ms first {writes[0]:F0}, then min {writes.Skip(1).DefaultIfEmpty().Min():F0} " +
                $"avg {writes.Skip(1).DefaultIfEmpty().Average():F0} max {writes.Skip(1).DefaultIfEmpty().Max():F0}");

            ViaSnapshot after;
            using (var kb = via.Open(hid, options)) after = kb.Snapshot();
            log.Write($"{profile.Name}: after   {after}");

            // v3 brightness does not round-trip exactly (44 reads back as 42); everything else must.
            bool same = after.Effect == before.Effect && after.Speed == before.Speed
                && after.Hue == before.Hue && after.Sat == before.Sat
                && Math.Abs(after.Brightness - before.Brightness) <= 2;
            log.Write($"{profile.Name}: {(same ? "restored" : "NOT restored")}");
            if (!same) failures++;
        }
        return failures == 0 ? 0 : 1;
    }
}
