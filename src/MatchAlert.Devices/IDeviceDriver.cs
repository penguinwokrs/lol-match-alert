// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.App;
using MatchAlert.Devices.Hid;
using MatchAlert.Devices.Setup;
using MatchAlert.Domain;

namespace MatchAlert.Devices;

/// <summary>
/// One way of talking to keyboards: a protocol, not a model. Models are data (device profiles).
/// Supporting another maker's protocol means one more implementation of this, and nothing else.
/// </summary>
public interface IDeviceDriver
{
    /// <summary>The <c>driver</c> value device profiles use to pick this driver, e.g. "via".</summary>
    string Id { get; }

    /// <summary>Whether this HID collection is the one the driver sends commands to.</summary>
    bool IsControlInterface(HidDeviceInfo device);

    ILightingDevice Create(HidDeviceInfo device, DeviceProfile profile);

    /// <summary>A setup flow for a device no profile knows yet, or null if this driver cannot talk to it.</summary>
    ISetupFlow? TrySetup(HidDeviceInfo device);

    /// <summary>
    /// Puts back lighting a session left changed (the app crashed mid alert), but only if the device
    /// still shows what that session wrote. Anything else means the user has changed it since.
    /// </summary>
    void Recover(HidDeviceInfo device, DeviceProfile profile, PendingEntry entry);

    /// <summary>The lighting as the device reports it, for <c>--test</c> to compare before and after an alert.</summary>
    DeviceState ReadState(HidDeviceInfo device, DeviceProfile profile);
}
