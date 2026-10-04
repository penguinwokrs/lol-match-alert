// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Diagnostics;
using MatchAlert.Devices.Hid;

namespace MatchAlert.Devices.Via;

/// <summary>The four lighting values VIA exposes, as read from the board.</summary>
public sealed record ViaSnapshot(byte Brightness, byte Effect, byte Speed, byte Hue, byte Sat)
{
    public override string ToString() => $"effect {Effect}, speed {Speed}, brightness {Brightness}, hue {Hue}, sat {Sat}";
}

/// <summary>
/// Timings learned on real boards (see <see cref="ViaKeyboard"/>). Tests pass zeros so the byte-level
/// simulator runs without waiting.
/// </summary>
public sealed record ViaTiming(
    int ColorHoldMs = 200,
    int BrightnessHoldMs = 400,
    int SettleMs = 30,
    int BudgetMs = 1500,
    int EchoTimeoutMs = 250,
    int FastEchoTimeoutMs = 20,
    int ProbeWindowMs = 300);

/// <summary>
/// VIA lighting over raw HID: protocol detection, get/set, and the workarounds real firmware needs.
/// <para>
/// A C# reimplementation of what kbd-signal (github.com/Sora-bluesky/kbd-signal, MIT) measured on
/// hardware, including the Keychron Q1 HE 8K this project ships a profile for.
/// </para>
/// <para>
/// Only set (0x07), get (0x08) and protocol (0x01) are ever sent. Save (0x09) never is, so every
/// change lives in RAM and a power cycle brings back the user's own lighting.
/// </para>
/// </summary>
public sealed class ViaKeyboard : IKeyboardLink<ViaSnapshot>
{
    public const ushort UsagePage = 0xFF60;
    public const ushort Usage = 0x61;

    private const byte CmdProtocol = 0x01;
    private const byte CmdSet = 0x07;
    private const byte CmdGet = 0x08;

    private readonly IRawHid _hid;
    private readonly byte _channel;
    private readonly ViaTiming _timing;

    public ViaKeyboard(IRawHid hid, int channel, bool resetOnEffect, ViaTiming? timing = null)
    {
        _hid = hid;
        _channel = (byte)channel;
        _timing = timing ?? new ViaTiming();
        ResetOnEffect = resetOnEffect;
        var response = Request([CmdProtocol], match: 1, tries: 6, _timing.EchoTimeoutMs)
            ?? throw new IOException("The keyboard did not answer the VIA protocol query.");
        Protocol = (response[1] << 8) | response[2];
    }

    /// <summary>VIA protocol version. 11 and above put a channel byte in every lighting command.</summary>
    public int Protocol { get; }

    public bool IsV3 => Protocol >= 11;

    /// <summary>
    /// Some firmware forces color to hue 0 and brightness to full ~50-150 ms after an effect change.
    /// When set, color is settled with the LEDs dark across that window so the red never shows.
    /// </summary>
    public bool ResetOnEffect { get; set; }

    public ViaSnapshot Snapshot()
    {
        var color = Get(ViaValue.Color, 2);
        return new ViaSnapshot(Get(ViaValue.Brightness, 1)[0], Get(ViaValue.Effect, 1)[0], Get(ViaValue.Speed, 1)[0], color[0], color[1]);
    }

    public ShownColor Showing()
    {
        var color = Get(ViaValue.Color, 2);
        return new ShownColor(Get(ViaValue.Effect, 1)[0], color[0], color[1]);
    }

    /// <summary>
    /// Writes everything, effect included, the careful way. On a reset-prone board this takes ~700 ms
    /// (the dark hold plus the brightness rewrite window), so the first step of an alert shows late.
    /// ponytail: once per alert by design; later steps are color-only writes of ~3 ms. Returns false if the color could not be
    /// confirmed; on a reset-prone board the LEDs are then left dark rather than showing the reset's red.
    /// </summary>
    public bool Apply(byte effect, byte hue, byte sat, byte? speed, byte brightness)
    {
        if (!ResetOnEffect)
        {
            Set(ViaValue.Effect, effect);
            if (speed is { } s) Set(ViaValue.Speed, s);
            Set(ViaValue.Color, hue, sat);
            SettleBrightness(brightness);
            return true;
        }

        // Dark before the effect change, so the reset can never land while the LEDs are bright.
        Set(ViaValue.Brightness, 0);
        Set(ViaValue.Effect, effect);
        if (speed is { } sp) Set(ViaValue.Speed, sp);
        if (!SettleColorDark(hue, sat)) return false;
        SettleBrightness(brightness);
        return true;
    }

    public bool Restore(ViaSnapshot s) => Apply(s.Effect, s.Hue, s.Sat, s.Speed, s.Brightness);

    /// <summary>
    /// The playback path when the effect is not changing: no reset to fight, and a short echo wait so
    /// one lost echo cannot stall a fast blink.
    /// </summary>
    public void ShowColor(byte hue, byte sat, byte brightness, byte? speed = null)
    {
        if (speed is { } s) SetFast(ViaValue.Speed, s);
        SetFast(ViaValue.Color, hue, sat);
        SetFast(ViaValue.Brightness, brightness);
    }

    /// <summary>
    /// Does a write on the configured channel actually land? A GET being answered is not proof: a Q1 HE
    /// answers channel 0 too. Probes speed, which round-trips exactly (brightness does not on v3) and is
    /// invisible on a static effect. Puts the original speed back on every path that read it.
    /// </summary>
    public bool VerifyChannel()
    {
        byte? before = null;
        try
        {
            before = Get(ViaValue.Speed, 1, tries: 2)[0];
            // Never 0: a channel that echoes requests back reads as zeros and would confirm itself.
            byte probe = before == 1 ? (byte)2 : (byte)1;
            Set(ViaValue.Speed, probe);
            return Get(ViaValue.Speed, 1, tries: 2)[0] == probe;
        }
        catch (IOException)
        {
            return false;
        }
        finally
        {
            if (before is { } b)
            {
                try { Set(ViaValue.Speed, b); } catch (IOException) { }
            }
        }
    }

    /// <summary>
    /// Whether this firmware resets color and brightness shortly after an effect change. Seeds a known
    /// green at medium brightness, switches to <paramref name="otherEffect"/>, and watches for the
    /// reset's signature (hue 0 or brightness 255). The caller restores the lighting afterwards.
    /// </summary>
    public bool ProbeResetOnEffect(byte otherEffect)
    {
        const byte probeHue = 85, probeBrightness = 120;
        Set(ViaValue.Brightness, probeBrightness);
        Set(ViaValue.Color, probeHue, 255);
        Set(ViaValue.Effect, otherEffect);
        var clock = Stopwatch.StartNew();
        do
        {
            Sleep(_timing.SettleMs / 2);
            try
            {
                if (Get(ViaValue.Color, 2, tries: 2)[0] == 0 || Get(ViaValue.Brightness, 1, tries: 2)[0] == 255) return true;
            }
            catch (IOException)
            {
                // A dropped read is not evidence either way.
            }
        }
        while (clock.ElapsedMilliseconds < _timing.ProbeWindowMs);
        return false;
    }

    public byte[] Get(ViaValue value, int length, int tries = 6)
    {
        var (v2, v3) = Ids(value);
        byte[] request = IsV3 ? [CmdGet, _channel, v3] : [CmdGet, v2];
        var response = Request(request, request.Length, tries, _timing.EchoTimeoutMs)
            ?? throw new IOException($"The keyboard did not report its {value.ToString().ToLowerInvariant()}.");
        return response.AsSpan(request.Length, length).ToArray();
    }

    public void Set(ViaValue value, params byte[] data) => Set(value, data, tries: 2, _timing.EchoTimeoutMs);

    private void SetFast(ViaValue value, params byte[] data) => Set(value, data, tries: 1, _timing.FastEchoTimeoutMs);

    private void Set(ViaValue value, byte[] data, int tries, int timeoutMs)
    {
        var (v2, v3) = Ids(value);
        byte[] header = IsV3 ? [CmdSet, _channel, v3] : [CmdSet, v2];
        // Waiting for the echo keeps the input queue clean for later reads. A missed echo is not fatal:
        // the write itself has gone out.
        Request([.. header, .. data], header.Length, tries, timeoutMs);
    }

    /// <summary>
    /// Keeps rewriting brightness 0 and the color while the reset window passes, then confirms the
    /// color by read-back. Never raises: a failed write or read counts as a miss.
    /// </summary>
    private bool SettleColorDark(byte hue, byte sat)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < _timing.BudgetMs)
        {
            try
            {
                Set(ViaValue.Brightness, 0);
                Set(ViaValue.Color, hue, sat);
            }
            catch (IOException) { }
            if (clock.ElapsedMilliseconds < _timing.ColorHoldMs) continue;
            Sleep(_timing.SettleMs);
            try
            {
                var color = Get(ViaValue.Color, 2, tries: 2);
                if (color[0] == hue && color[1] == sat) return true;
            }
            catch (IOException) { }
        }
        return false;
    }

    /// <summary>
    /// Some firmware silently reverts a brightness write that follows an effect change, as late as
    /// ~300 ms after. So brightness is rewritten across that window. On v3 a read-back cannot confirm
    /// it anyway: VIA scales brightness differently in each direction, so 44 reads back as 42.
    /// </summary>
    private void SettleBrightness(byte brightness)
    {
        var clock = Stopwatch.StartNew();
        do
        {
            try { Set(ViaValue.Brightness, brightness); } catch (IOException) { }
            Sleep(_timing.SettleMs * 3);
        }
        while (clock.ElapsedMilliseconds < _timing.BrightnessHoldMs);
    }

    /// <summary>
    /// Sends a command and reads until a response echoing its first <paramref name="match"/> bytes
    /// arrives. Windows delivers input reports to every open handle, so echoes of commands another
    /// program sent (the VIA app, kbd-signal) arrive here too and are skipped. Null on timeout.
    /// </summary>
    private byte[]? Request(byte[] payload, int match, int tries, int timeoutMs)
    {
        while (_hid.Read(1) is not null) { }   // drain anything stale
        _hid.Write(payload);
        for (int i = 0; i < tries; i++)
        {
            var response = _hid.Read(timeoutMs);
            if (response is not null && response.Length >= match && response.AsSpan(0, match).SequenceEqual(payload.AsSpan(0, match)))
                return response;
        }
        return null;
    }

    private static (byte V2, byte V3) Ids(ViaValue value) => value switch
    {
        ViaValue.Brightness => (0x80, 1),
        ViaValue.Effect => (0x81, 2),
        ViaValue.Speed => (0x82, 3),
        ViaValue.Color => (0x83, 4),
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static void Sleep(int ms)
    {
        if (ms > 0) Thread.Sleep(ms);
    }

    public void Dispose() => _hid.Dispose();
}

public enum ViaValue
{
    Brightness,
    Effect,
    Speed,
    Color,
}
