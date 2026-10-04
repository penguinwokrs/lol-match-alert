// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

namespace MatchAlert.Devices.Hid;

/// <summary>One HID collection. A keyboard exposes several; drivers pick the one they speak to.</summary>
public sealed record HidDeviceInfo(
    string Path,
    ushort VendorId,
    ushort ProductId,
    ushort UsagePage,
    ushort Usage,
    int InputReportLength,
    int OutputReportLength,
    string Product)
{
    public override string ToString() =>
        $"{Product} (VID 0x{VendorId:X4}, PID 0x{ProductId:X4}, usage page 0x{UsagePage:X4}, usage 0x{Usage:X2})";
}
