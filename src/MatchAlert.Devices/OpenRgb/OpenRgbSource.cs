// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text.Json;
using MatchAlert.App;
using MatchAlert.Domain;

namespace MatchAlert.Devices.OpenRgb;

/// <summary>
/// Devices OpenRGB drives, for people who already run it: hundreds of models from every maker, through its
/// SDK server. Off by default (<c>"devices": { "openrgb": { "enabled": true } }</c> turns it on), because a
/// keyboard driven both by OpenRGB and by one of this app's own drivers would be fought over, and only the
/// person who set OpenRGB up knows which devices it has.
/// <para>
/// What is put back afterwards is what OpenRGB was showing: its active mode, with that mode's settings, and
/// its colors. For most devices OpenRGB cannot read the hardware, so that is OpenRGB's own last state.
/// </para>
/// </summary>
public sealed class OpenRgbSource(Func<ResolvedSettings> settings, Action<string> log) : IDeviceSource
{
    public const string ProfileId = "openrgb";
    private const string ClientName = "lol-match-alert";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    /// <summary>OpenRGB's device_type values (RGBController.h), by the names settings use.</summary>
    public static readonly IReadOnlyDictionary<string, int> DeviceTypes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["motherboard"] = 0, ["dram"] = 1, ["gpu"] = 2, ["cooler"] = 3, ["ledstrip"] = 4, ["keyboard"] = 5,
        ["mouse"] = 6, ["mousemat"] = 7, ["headset"] = 8, ["headsetstand"] = 9, ["gamepad"] = 10, ["light"] = 11,
        ["speaker"] = 12, ["virtual"] = 13, ["storage"] = 14, ["case"] = 15, ["microphone"] = 16,
        ["accessory"] = 17, ["keypad"] = 18, ["laptop"] = 19, ["monitor"] = 20,
    };

    private string? _lastProblem;

    public IReadOnlyList<ILightingDevice> Discover()
    {
        var current = settings();
        if (current.Profiles.FirstOrDefault(p => p.Id == ProfileId) is not { } profile || !current.IsEnabled(ProfileId)) return [];
        var options = Options.From(profile);
        try
        {
            using var client = OpenRgbClient.Connect(options.Host, options.Port, ClientName, Timeout);
            var devices = new List<ILightingDevice>();
            for (uint i = 0, n = client.ControllerCount(); i < n; i++)
            {
                var c = client.Controller(i);
                if (options.Types.Contains(c.Type)) devices.Add(new Device(this, profile, options, c.Index, c.Name));
            }
            Report(null);
            return devices;
        }
        catch (Exception e) when (e is IOException or InvalidDataException)
        {
            Report(e.Message);
            return [];
        }
    }

    /// <summary>
    /// ponytail: nothing kept on disk, so a crash mid alert leaves OpenRGB showing the alert until its profile
    /// is loaded again. Pending snapshots like the HID drivers' would fix it if that is ever reported.
    /// </summary>
    public void RecoverInterruptedSessions() { }

    /// <summary>Says when OpenRGB becomes unreachable or reachable again, not on every alert.</summary>
    private void Report(string? problem)
    {
        if (problem == _lastProblem) return;
        log(problem is null ? "OpenRGB: connected" : $"OpenRGB: {problem}");
        _lastProblem = problem;
    }

    private sealed record Options(string Host, int Port, IReadOnlySet<int> Types)
    {
        public static Options From(DeviceProfile profile)
        {
            string host = "127.0.0.1";
            int port = 6742;
            var types = new HashSet<int> { DeviceTypes["keyboard"] };
            if (profile.Options.TryGetValue("openrgb", out var o) && o.ValueKind == JsonValueKind.Object)
            {
                if (o.TryGetProperty("host", out var h) && h.GetString() is { Length: > 0 } hs) host = hs;
                if (o.TryGetProperty("port", out var p) && p.TryGetInt32(out int pi)) port = pi;
                if (o.TryGetProperty("deviceTypes", out var t) && t.ValueKind == JsonValueKind.Array)
                    types = t.EnumerateArray().Select(x => x.GetString() ?? "")
                        .SelectMany(x => x == "all" ? DeviceTypes.Values : DeviceTypes.TryGetValue(x, out int v) ? [v] : Array.Empty<int>())
                        .ToHashSet();
            }
            return new Options(host, port, types);
        }
    }

    private sealed class Device(OpenRgbSource source, DeviceProfile profile, Options options, uint index, string deviceName) : ILightingDevice
    {
        public string Id => profile.Id;
        public string Name => $"{deviceName} (OpenRGB)";
        public int MinStepMs => profile.MinStepMs;

        public ILightingSession OpenSession()
        {
            var client = OpenRgbClient.Connect(options.Host, options.Port, ClientName, Timeout);
            try
            {
                var before = client.Controller(index);
                // Indexes shift when OpenRGB rescans; never light a different device than was listed.
                if (before.Name != deviceName) throw new IOException($"OpenRGB's device {index} is now {before.Name}, not {deviceName}");
                client.SetCustomMode(index);
                return new Session(client, before, source.Log, Name);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
    }

    private Action<string> Log => log;

    private sealed class Session(OpenRgbClient client, OpenRgbController before, Action<string> log, string name) : ILightingSession
    {
        private bool _failed;
        private bool _disposed;

        /// <summary>Every LED in one color. OpenRGB has no generic breathing, so a breathing step shows steady.</summary>
        public void Show(Step step)
        {
            byte Scale(byte c) => (byte)Math.Round(c * step.Brightness / 100.0, MidpointRounding.AwayFromZero);
            uint color = OpenRgbProtocol.Color(Scale(step.Color.R), Scale(step.Color.G), Scale(step.Color.B));
            try
            {
                client.UpdateLeds(before.Index, Enumerable.Repeat(color, before.Colors.Length).ToArray());
                _failed = false;
            }
            catch (IOException e)
            {
                if (!_failed) log($"{name}: update failed: {e.Message}");
                _failed = true;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (before.ActiveMode >= 0 && before.ActiveMode < before.Modes.Count)
                    client.UpdateMode(before.Index, before.ActiveMode, before.Modes[before.ActiveMode].Raw);
                client.UpdateLeds(before.Index, before.Colors);
            }
            catch (IOException e)
            {
                log($"{name}: restore failed: {e.Message}");
            }
            finally
            {
                client.Dispose();
            }
        }
    }
}
