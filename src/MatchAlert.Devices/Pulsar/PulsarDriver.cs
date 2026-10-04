// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.App;
using MatchAlert.Devices.Hid;
using MatchAlert.Devices.Setup;
using MatchAlert.Domain;

namespace MatchAlert.Devices.Pulsar;

/// <summary>
/// Pulsar keyboards that speak the configurator's 0x22/0x23 lighting protocol on the QMK raw HID interface.
/// Listed before the VIA driver, so a Pulsar keyboard no profile knows is set up here and never gets the VIA
/// wizard's channel writes, which this firmware is not known to accept.
/// </summary>
public sealed class PulsarDriver(IHidBus bus, PendingSnapshots pending, Action<string> log, PulsarTiming? timing = null) : IDeviceDriver
{
    public string Id => "pulsar";

    /// <summary>Only the raw HID interface. The same keyboards also expose 0xFF1C / 0x1C, their bootloader, which is never opened.</summary>
    public bool IsControlInterface(HidDeviceInfo device) =>
        device.UsagePage == PulsarKeyboard.UsagePage && device.Usage == PulsarKeyboard.Usage;

    public ILightingDevice Create(HidDeviceInfo device, DeviceProfile profile) =>
        new RestoringDevice<PulsarKeyboard, PulsarSnapshot>(Id, device, profile, pending, log, open: () => Open(device));

    public ISetupFlow? TrySetup(HidDeviceInfo device) =>
        device.VendorId == PulsarKeyboard.VendorId && IsControlInterface(device) ? new PulsarSetupFlow(this, device) : null;

    public void Recover(HidDeviceInfo device, DeviceProfile profile, PendingEntry entry)
    {
        using var kb = Open(device);
        RestoringDevice<PulsarKeyboard, PulsarSnapshot>.Recover(kb, entry, profile.Name, log);
    }

    public DeviceState ReadState(HidDeviceInfo device, DeviceProfile profile)
    {
        using var kb = Open(device);
        var s = kb.Snapshot();
        return new DeviceState(
        [
            new("profile", s.Profile),
            new("on", s.Enabled ? 1 : 0),
            new("effect", s.Effect),
            new("speed", s.Speed),
            // Unverified whether brightness round-trips exactly (VIA's does not); allow what VIA needs.
            new("brightness", s.Brightness, Tolerance: 2),
            new("hue", s.Hue),
            new("sat", s.Sat),
        ]);
    }

    public PulsarKeyboard Open(HidDeviceInfo device)
    {
        var raw = bus.Open(device);
        try { return new PulsarKeyboard(raw, timing); }
        catch { raw.Dispose(); throw; }
    }
}

/// <summary>
/// A Pulsar keyboard no profile knows: reads only, asks nothing. The effect numbers come from QMK's
/// rgb_matrix order, which the configurator's effect list follows (1 solid, 5 breathing).
/// </summary>
internal sealed class PulsarSetupFlow(PulsarDriver driver, HidDeviceInfo hid) : ISetupFlow
{
    public string DeviceName => string.IsNullOrWhiteSpace(hid.Product) ? $"Pulsar {hid.ProductId:X4}" : hid.Product;

    public DeviceProfile? Run(IUserPrompt prompt)
    {
        using (var kb = driver.Open(hid)) kb.Showing();   // proves it speaks the protocol; throws if not

        return new DeviceProfile
        {
            Id = ProfileIds.Slug(DeviceName),
            Name = DeviceName,
            Driver = driver.Id,
            Match = new DeviceMatch(hid.VendorId, [hid.ProductId], null),
            Effects = new Dictionary<string, int> { ["solid"] = 1, ["breathing"] = 5 },
            // Unverified flash behaviour: keep writes rare (see the built-in Pulsar profiles).
            MinStepMs = 1000,
            DefaultPattern = "pulse",
        };
    }
}
