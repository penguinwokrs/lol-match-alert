// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.App;
using MatchAlert.Devices.Hid;
using MatchAlert.Devices.Setup;
using MatchAlert.Domain;

namespace MatchAlert.Devices;

/// <summary>
/// Matches the HID collections that are plugged in right now against the device profiles, and hands
/// each match to the driver its profile names. Enumerated fresh on every call, so plugging a keyboard
/// in needs nothing more than the next alert.
/// </summary>
public sealed class HidDeviceSource(
    IHidBus bus,
    IReadOnlyList<IDeviceDriver> drivers,
    Func<ResolvedSettings> settings,
    PendingSnapshots pending,
    Action<string> log) : IDeviceSource
{
    public IReadOnlyList<ILightingDevice> Discover() =>
        Claim(bus.Enumerate()).Select(c => c.Driver.Create(c.Hid, c.Profile)).ToList();

    /// <summary>
    /// Collections no profile describes that some driver offers to set up. Asked of the drivers' setup, not
    /// of IsControlInterface: a driver may drive any profile it is given but only volunteer for its own
    /// family, so a vendor-defined collection on a mouse or headset is not offered as a keyboard.
    /// </summary>
    public IReadOnlyList<HidDeviceInfo> Unrecognised()
    {
        var all = bus.Enumerate();
        var claimed = Claim(all).Select(c => c.Hid.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return all.Where(h => !claimed.Contains(h.Path) && SetupFor(h) is not null).ToList();
    }

    /// <summary>The recognised keyboards that are plugged in, for the tray to list.</summary>
    public IReadOnlyList<(HidDeviceInfo Hid, DeviceProfile Profile)> Recognised() =>
        Claim(bus.Enumerate()).Select(c => (c.Hid, c.Profile)).ToList();

    /// <summary>The driver a profile names, or null if no such driver is loaded.</summary>
    public IDeviceDriver? DriverFor(DeviceProfile profile) => drivers.FirstOrDefault(d => d.Id == profile.Driver);

    /// <summary>Drivers are asked in order; the first that can talk to the device runs its setup.</summary>
    public ISetupFlow? SetupFor(HidDeviceInfo device) =>
        drivers.Select(d => d.TrySetup(device)).FirstOrDefault(f => f is not null);

    public void RecoverInterruptedSessions()
    {
        var current = settings();
        foreach (var entry in pending.All())
        {
            try
            {
                var profile = current.Profiles.FirstOrDefault(p => p.Id == entry.ProfileId);
                var driver = drivers.FirstOrDefault(d => d.Id == entry.Driver);
                var hid = driver is null ? null : bus.Enumerate().FirstOrDefault(h =>
                    h.VendorId == entry.VendorId && h.ProductId == entry.ProductId && driver.IsControlInterface(h));
                if (profile is null || driver is null || hid is null)
                    log($"Interrupted session on {entry.ProfileId}: the device is gone; a replug restores its own lighting");
                else
                    driver.Recover(hid, profile, entry);
            }
            catch (Exception e)
            {
                log($"Interrupted session on {entry.ProfileId}: could not restore: {e.Message}");
            }
            finally
            {
                pending.Delete(entry.Key);
            }
        }
    }

    /// <summary>
    /// Each collection goes to one profile. User profiles come after built-in ones in the settings, and
    /// later profiles claim first, so a user's own profile wins over a built-in one for the same board.
    /// </summary>
    private List<(HidDeviceInfo Hid, DeviceProfile Profile, IDeviceDriver Driver)> Claim(IReadOnlyList<HidDeviceInfo> hids)
    {
        var claims = new List<(HidDeviceInfo, DeviceProfile, IDeviceDriver)>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in settings().Profiles.Reverse())
        {
            var driver = drivers.FirstOrDefault(d => d.Id == profile.Driver);
            if (driver is null) continue;
            foreach (var hid in hids.Where(h => Matches(profile.Match, h) && driver.IsControlInterface(h)))
                if (taken.Add(hid.Path)) claims.Add((hid, profile, driver));
        }
        return claims;
    }

    private static bool Matches(DeviceMatch match, HidDeviceInfo hid) =>
        hid.VendorId == match.VendorId
        && (match.ProductIds.Count == 0 || match.ProductIds.Contains(hid.ProductId))
        && (match.ProductString is null || hid.Product.Contains(match.ProductString, StringComparison.OrdinalIgnoreCase));
}
