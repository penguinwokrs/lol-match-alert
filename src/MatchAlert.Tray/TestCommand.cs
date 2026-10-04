// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Diagnostics;
using MatchAlert.App;
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

        var (source, _) = Program.Devices(() => settings, log);
        source.RecoverInterruptedSessions();
        var found = source.Recognised();
        if (found.Count == 0)
        {
            log.Write("No set-up keyboard is connected.");
            foreach (var d in source.Unrecognised()) log.Write($"  could be set up: {d}");
            return 2;
        }

        int failures = 0;
        foreach (var (hid, profile) in found)
        {
            if (source.DriverFor(profile) is not { } driver) continue;
            var before = driver.ReadState(hid, profile);
            log.Write($"{profile.Name}: driver {driver.Id}, {hid}");
            log.Write($"{profile.Name}: before  {before}");

            var pattern = settings.PatternFor(profile);
            var writes = new List<double>();
            using (var session = driver.Create(hid, profile).OpenSession())
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

            var after = driver.ReadState(hid, profile);
            log.Write($"{profile.Name}: after   {after}");
            bool same = before.Matches(after);
            log.Write($"{profile.Name}: {(same ? "restored" : "NOT restored")}");
            if (!same) failures++;
        }
        // G HUB keeps the lighting itself and the SDK cannot read it back, so there is nothing to compare:
        // this checks that the SDK connects, shows the pattern and hands the lighting back without an error.
        foreach (var device in Program.GHub(() => settings, log).Discover())
        {
            try
            {
                using (var session = device.OpenSession())
                {
                    var steps = settings.PatternFor(settings.Profiles.Single(p => p.Id == device.Id)).Steps;
                    var clock = Stopwatch.StartNew();
                    for (int i = 0; clock.Elapsed < TimeSpan.FromSeconds(seconds); i++)
                    {
                        session.Show(steps[i % steps.Count]);
                        Thread.Sleep(Math.Max(steps[i % steps.Count].DurationMs, device.MinStepMs));
                        if (steps.Count == 1) { Thread.Sleep(TimeSpan.FromSeconds(seconds)); break; }
                    }
                }
                log.Write($"{device.Name}: played and handed the lighting back to G HUB (it cannot be read back to compare)");
            }
            catch (IOException e)
            {
                log.Write($"{device.Name}: skipped: {e.Message}");
            }
        }
        return failures == 0 ? 0 : 1;
    }
}
