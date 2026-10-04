// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Collections.Concurrent;
using System.Text.Json;
using MatchAlert.App;
using MatchAlert.Devices.Hid;
using MatchAlert.Devices.Setup;
using MatchAlert.Domain;

namespace MatchAlert.Devices.Via;

/// <summary>The <c>"via"</c> block of a device profile.</summary>
public sealed record ViaOptions(int Channel = ViaOptions.RgbMatrixChannel, bool ResetOnEffect = false)
{
    /// <summary>QMK's rgb_matrix custom channel, which Keychron and most VIA boards with per-key RGB use.</summary>
    public const int RgbMatrixChannel = 3;

    public static ViaOptions From(DeviceProfile profile)
    {
        if (!profile.Options.TryGetValue("via", out var via) || via.ValueKind != JsonValueKind.Object) return new ViaOptions();
        int channel = via.TryGetProperty("channel", out var c) && c.TryGetInt32(out int n) ? n : RgbMatrixChannel;
        bool reset = via.TryGetProperty("resetOnEffect", out var r) && r.ValueKind == JsonValueKind.True;
        return new ViaOptions(channel, reset);
    }

    public JsonElement ToJson() => JsonSerializer.SerializeToElement(new { channel = Channel, resetOnEffect = ResetOnEffect });
}

/// <summary>Keyboards running QMK or Keychron firmware with VIA support, over raw HID.</summary>
public sealed class ViaDriver(IHidBus bus, PendingSnapshots pending, Action<string> log, ViaTiming? timing = null) : IDeviceDriver
{
    private readonly ViaTiming _timing = timing ?? new ViaTiming();

    /// <summary>
    /// What each board's restore wrote and read back. v3 brightness does not round-trip (a Q1 HE reads
    /// 44 back as 42), so a later snapshot that still reads the read-back means "untouched since",
    /// and the value we wrote is the honest one. Without this a dim backlight walks down to dark one
    /// alert at a time (kbd-signal #58).
    /// </summary>
    private readonly ConcurrentDictionary<string, (byte Written, byte ReadBack)> _brightnessEchoes = new();

    public string Id => "via";

    public bool IsControlInterface(HidDeviceInfo device) =>
        device.UsagePage == ViaKeyboard.UsagePage && device.Usage == ViaKeyboard.Usage;

    public ILightingDevice Create(HidDeviceInfo device, DeviceProfile profile) =>
        new ViaLightingDevice(this, device, profile, ViaOptions.From(profile));

    public ISetupFlow? TrySetup(HidDeviceInfo device) =>
        IsControlInterface(device) ? new ViaSetupFlow(bus, device, _timing) : null;

    public void Recover(HidDeviceInfo device, DeviceProfile profile, PendingEntry entry)
    {
        var saved = entry.State.Deserialize<PendingState>(PendingState.Json)
            ?? throw new InvalidDataException("empty pending state");
        using var kb = Open(device, ViaOptions.From(profile));
        var now = kb.Snapshot();
        if (saved.Shown.Any(s => s.Effect == now.Effect && s.Hue == now.Hue && s.Sat == now.Sat))
        {
            kb.Restore(saved.Snapshot);
            log($"{profile.Name}: restored the lighting an interrupted alert left behind ({saved.Snapshot})");
        }
        else
        {
            log($"{profile.Name}: an interrupted alert's snapshot was discarded; the lighting has changed since ({now})");
        }
    }

    internal ViaKeyboard Open(HidDeviceInfo device, ViaOptions options)
    {
        var raw = bus.Open(device);
        try { return new ViaKeyboard(raw, options.Channel, options.ResetOnEffect, _timing); }
        catch { raw.Dispose(); throw; }
    }

    internal ViaSnapshot CorrectBrightness(string key, ViaSnapshot snapshot) =>
        _brightnessEchoes.TryGetValue(key, out var echo) && snapshot.Brightness == echo.ReadBack
            ? snapshot with { Brightness = echo.Written }
            : snapshot;

    internal void RememberBrightness(string key, byte written, byte readBack) => _brightnessEchoes[key] = (written, readBack);

    internal PendingSnapshots Pending => pending;
    internal Action<string> Log => log;

    /// <summary>What goes in a pending file: the lighting to restore, and what we showed instead.</summary>
    internal sealed record PendingState(ViaSnapshot Snapshot, List<Shown> Shown)
    {
        public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    }

    internal sealed record Shown(byte Effect, byte Hue, byte Sat);
}
