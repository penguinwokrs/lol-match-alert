// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.Domain;

namespace MatchAlert.App;

/// <summary>
/// Several device sources seen as one: keyboards driven directly over HID, keyboards reached through a
/// maker's own software, and whatever comes next. A source that fails is logged and skipped, so one broken
/// integration never costs the others their alert.
/// </summary>
public sealed class DeviceSources(IReadOnlyList<IDeviceSource> sources, Action<string> log) : IDeviceSource
{
    public IReadOnlyList<ILightingDevice> Discover()
    {
        var all = new List<ILightingDevice>();
        foreach (var source in sources)
        {
            try { all.AddRange(source.Discover()); }
            catch (Exception e) { log($"Could not list devices from {source.GetType().Name}: {e.Message}"); }
        }
        return all;
    }

    public void RecoverInterruptedSessions()
    {
        foreach (var source in sources)
        {
            try { source.RecoverInterruptedSessions(); }
            catch (Exception e) { log($"Recovery failed in {source.GetType().Name}: {e.Message}"); }
        }
    }
}
