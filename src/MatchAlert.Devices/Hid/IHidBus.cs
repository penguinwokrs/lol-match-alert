// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

namespace MatchAlert.Devices.Hid;

/// <summary>Raw reports to and from one HID collection, without the report id byte. Report length is the device's own (32 bytes for VIA, 64 for Pulsar).</summary>
public interface IRawHid : IDisposable
{
    /// <summary>
    /// Sends one output report. Short payloads are padded with zeros to the report length. Most devices
    /// here use no report ids (0); SayoDevice firmware uses 0x22.
    /// </summary>
    void Write(ReadOnlySpan<byte> payload, byte reportId = 0);

    /// <summary>The next input report, or null if none arrived within <paramref name="timeoutMs"/>.</summary>
    byte[]? Read(int timeoutMs);
}

/// <summary>The machine's HID collections. The Windows implementation is the only one that touches hardware.</summary>
public interface IHidBus
{
    IReadOnlyList<HidDeviceInfo> Enumerate();
    IRawHid Open(HidDeviceInfo device);
}
