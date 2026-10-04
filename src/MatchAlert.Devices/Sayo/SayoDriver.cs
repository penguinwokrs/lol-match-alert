// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.App;
using MatchAlert.Devices.Hid;
using MatchAlert.Devices.Setup;
using MatchAlert.Domain;

namespace MatchAlert.Devices.Sayo;

/// <summary>
/// Keyboards running SayoDevice firmware. Pulsar's PCMK 3 HE series is the one known: its product ids are the
/// ones Pulsar's configurator asks the browser for, and the configurator's firmware logs name 0x2404 the
/// PCMK 3 HE 60 and 0x2502 / 0x2504 the PCMK 3 TKL, which have built-in profiles. The rest (0x2506, 0x2507
/// "XPad Mini", 0xF003) are set up from the device's own product name, by reads only.
/// </summary>
public sealed class SayoDriver(IHidBus bus, PendingSnapshots pending, Action<string> log, SayoTiming? timing = null) : IDeviceDriver
{
    public const ushort PulsarVendorId = 0x3710;

    /// <summary>The product ids Pulsar's configurator opens with its SayoDevice ("sKey") app.</summary>
    public static readonly IReadOnlySet<ushort> PulsarProductIds = new HashSet<ushort> { 0x2404, 0x2502, 0x2504, 0x2506, 0x2507, 0xF003 };

    public string Id => "sayo";

    /// <summary>
    /// The configurator opens the whole device and picks the collection carrying output report 0x22 or 0x21;
    /// Windows splits collections, so this takes vendor-defined ones with 64-byte (or 1024-byte high-speed)
    /// reports. Never QMK's raw HID page (0xFF60, another driver's) or the bootloader page (0xFF1C).
    /// ponytail: a guess at the Windows shape of a device nobody here has; if a board exposes two such
    /// collections, the one that does not answer just fails to open on each alert. Narrow it once someone
    /// reports their collections.
    /// </summary>
    public bool IsControlInterface(HidDeviceInfo device) =>
        device.UsagePage >= 0xFF00 && device.UsagePage is not 0xFF60 and not 0xFF1C
        && device.OutputReportLength is 64 or 1024;

    public ILightingDevice Create(HidDeviceInfo device, DeviceProfile profile) =>
        new RestoringDevice<SayoKeyboard, SayoSnapshot>(Id, device, profile, pending, log, open: () => Open(device));

    public ISetupFlow? TrySetup(HidDeviceInfo device) =>
        device.VendorId == PulsarVendorId && PulsarProductIds.Contains(device.ProductId) && IsControlInterface(device)
            ? new SayoSetupFlow(this, device)
            : null;

    public void Recover(HidDeviceInfo device, DeviceProfile profile, PendingEntry entry)
    {
        using var kb = Open(device);
        RestoringDevice<SayoKeyboard, SayoSnapshot>.Recover(kb, entry, profile.Name, log);
    }

    public DeviceState ReadState(HidDeviceInfo device, DeviceProfile profile)
    {
        using var kb = Open(device);
        var e = kb.Snapshot().Effect;
        // Everything a session touches, plus the rest of the block as one checksum so nothing else moved either.
        return new DeviceState(
        [
            new("on", e[3]),
            new("mode", e[4]),
            new("sub-mode", e[5]),
            new("rgb", e[0] << 16 | e[1] << 8 | e[2]),
            new("speed", e[6]),
            new("brightness", e[7]),
            new("rest", e.Skip(8).Select((b, i) => b * (i + 1)).Sum()),
        ]);
    }

    public SayoKeyboard Open(HidDeviceInfo device)
    {
        var raw = bus.Open(device);
        try { return new SayoKeyboard(raw, timing); }
        catch { raw.Dispose(); throw; }
    }
}

/// <summary>Reads only, asks nothing: effect numbers are fixed by the firmware (see <see cref="SayoKeyboard"/>).</summary>
internal sealed class SayoSetupFlow(SayoDriver driver, HidDeviceInfo hid) : ISetupFlow
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
            Effects = new Dictionary<string, int> { ["solid"] = 5 << 4 | 0, ["breathing"] = 3 << 4 | 0 },
            // Unverified: keep writes rare, and let the firmware animate.
            MinStepMs = 1000,
            DefaultPattern = "pulse",
        };
    }
}
