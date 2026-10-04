// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

namespace MatchAlert.Devices.Logitech;

/// <summary>
/// The slice of Logitech's LED SDK (LogitechLEDLib.h) this app uses. Colors are percentages, 0-100.
/// The SDK talks to G HUB, which owns the devices: the app never touches a Logitech keyboard directly.
/// </summary>
public interface ILogiLed : IDisposable
{
    /// <summary>Connects to G HUB. False when G HUB is not running or refuses.</summary>
    bool Init(string appName);

    bool SetTargetDevice(int deviceTypes);
    bool SaveCurrentLighting();
    bool SetLighting(int redPercent, int greenPercent, int bluePercent);

    /// <summary>A breathing effect G HUB animates itself. Duration 0 means until stopped.</summary>
    bool PulseLighting(int redPercent, int greenPercent, int bluePercent, int durationMs, int intervalMs);

    bool StopEffects();
    bool RestoreLighting();

    /// <summary>Disconnects; G HUB takes the lighting back.</summary>
    void Shutdown();
}
