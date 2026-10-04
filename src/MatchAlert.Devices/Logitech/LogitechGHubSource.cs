// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.App;
using MatchAlert.Domain;

namespace MatchAlert.Devices.Logitech;

/// <summary>
/// Every Logitech G device G HUB lights, as one device. Not HID: G HUB owns the hardware and arbitrates, so
/// this goes through Logitech's own LED SDK. That makes it the safest integration here: nothing is ever
/// written to a keyboard's memory by this app, and G HUB itself saves and restores the lighting.
/// </summary>
/// <param name="installed">Whether G HUB's library is there at all: checked on every listing, so the device
/// appears once G HUB is installed and never shows up on a machine without it.</param>
public sealed class LogitechGHubSource(Func<bool> installed, Func<ILogiLed?> load, Func<ResolvedSettings> settings, Action<string> log) : IDeviceSource
{
    public const string ProfileId = "logitech-g-hub";

    /// <summary>LOGI_DEVICETYPE_ALL: monochrome, RGB and per-key RGB devices alike.</summary>
    private const int AllDevices = 7;

    private Func<ILogiLed?> Load { get; } = load;
    private Action<string> Log { get; } = log;

    public IReadOnlyList<ILightingDevice> Discover()
    {
        if (!installed()) return [];
        var current = settings();
        if (current.Profiles.FirstOrDefault(p => p.Id == ProfileId) is not { } profile || !current.IsEnabled(ProfileId)) return [];
        return [new Device(this, profile)];
    }

    /// <summary>Nothing to do: when this app stops, its SDK connection closes and G HUB takes the lighting back.</summary>
    public void RecoverInterruptedSessions() { }

    private sealed class Device(LogitechGHubSource source, DeviceProfile profile) : ILightingDevice
    {
        public string Id => profile.Id;
        public string Name => profile.Name;
        public int MinStepMs => profile.MinStepMs;

        public ILightingSession OpenSession()
        {
            var sdk = source.Load() ?? throw new IOException("G HUB's LED SDK is not available (is G HUB installed?)");
            try
            {
                if (!sdk.Init("lol-match-alert"))
                    throw new IOException("G HUB did not accept the connection (is it running, and allowed to let apps control lighting?)");
                sdk.SetTargetDevice(AllDevices);
                sdk.SaveCurrentLighting();
                return new Session(sdk, source.Log, Name);
            }
            catch
            {
                sdk.Dispose();
                throw;
            }
        }
    }

    private sealed class Session(ILogiLed sdk, Action<string> log, string name) : ILightingSession
    {
        private bool _pulsing;
        private bool _disposed;

        public void Show(Step step)
        {
            int Percent(byte channel) => (int)Math.Round(channel * step.Brightness / 255.0, MidpointRounding.AwayFromZero);
            var (r, g, b) = (Percent(step.Color.R), Percent(step.Color.G), Percent(step.Color.B));

            bool ok;
            if (step.Effect == "breathing")
            {
                // G HUB animates it; faster speed, shorter period. 0 duration: until stopped.
                int interval = step.Speed is { } s ? Math.Max(300, 3000 - s * 10) : 2000;
                ok = sdk.PulseLighting(r, g, b, 0, interval);
                _pulsing = true;
            }
            else
            {
                if (_pulsing) sdk.StopEffects();
                _pulsing = false;
                ok = sdk.SetLighting(r, g, b);
            }
            if (!ok) log($"{name}: G HUB refused the lighting change");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                sdk.StopEffects();
                sdk.RestoreLighting();
                sdk.Shutdown();
            }
            finally
            {
                sdk.Dispose();
            }
        }
    }
}
