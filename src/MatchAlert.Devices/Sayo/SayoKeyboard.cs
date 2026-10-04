// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.Devices.Hid;
using MatchAlert.Domain;

namespace MatchAlert.Devices.Sayo;

/// <summary>
/// The 48-byte LED effect block exactly as read, so a restore writes back what was there, byte for byte.
/// Layout: 0-2 r g b, 3 on (255) / off (0), 4 mode, 5 sub-mode, 6 speed (1-100), 7 brightness (1-100),
/// 8-23 profile colors, 24-47 indicator colors.
/// </summary>
public sealed record SayoSnapshot(byte[] Effect)
{
    public byte Mode => Effect[4];
    public byte SubMode => Effect[5];

    public override string ToString() =>
        $"mode {Mode}.{SubMode}, rgb {Effect[0]:X2}{Effect[1]:X2}{Effect[2]:X2}, speed {Effect[6]}, brightness {Effect[7]}, {(Effect[3] != 0 ? "on" : "off")}";
}

public sealed record SayoTiming(int ReplyTimeoutMs = 250, int FastReplyTimeoutMs = 50);

/// <summary>
/// Lighting on keyboards with SayoDevice firmware, which is what Pulsar's PCMK 2 HE family runs. NOT
/// VERIFIED ON HARDWARE: built from Pulsar's configurator, whose library was run against an emulated
/// device to capture its packets.
/// <para>
/// One command does it all: 0x26 with no data reads the 48-byte effect block, 0x26 with the block writes it.
/// The configurator follows every write with 0x0D, "save all"; this class never sends it, nor anything
/// but 0x26. Whether a 0x26 without the save takes effect at all is unknown: if it does not, the keyboard
/// simply does not light, and nothing is harmed.
/// </para>
/// <para>
/// Effects are numbered mode * 16 + sub-mode in profiles: Basic (5) / Solid Color (0) is 80, Breath (3) /
/// Breathing (0) is 48.
/// </para>
/// </summary>
public sealed class SayoKeyboard : IKeyboardLink<SayoSnapshot>
{
    private const byte CmdLedEffect = 0x26;
    private const int EffectLength = 48;

    /// <summary>The configurator uses 0x22 when the device has it and 0x21 otherwise.</summary>
    private static readonly byte[] ReportIds = [0x22, 0x21];

    private readonly IRawHid _hid;
    private readonly SayoTiming _timing;
    private byte[]? _current;

    public SayoKeyboard(IRawHid hid, SayoTiming? timing = null)
    {
        _hid = hid;
        _timing = timing ?? new SayoTiming();
        foreach (var id in ReportIds)
        {
            try
            {
                ReportId = id;
                if (TryReadEffect() is not null) return;
            }
            catch (IOException)
            {
                // Windows refuses a report id the device does not have; try the other.
            }
        }
        throw new IOException("The keyboard did not answer as a SayoDevice keyboard.");
    }

    public byte ReportId { get; private set; }

    public SayoSnapshot Snapshot() => new(ReadEffect());

    public ShownColor Showing()
    {
        var e = ReadEffect();
        var hsv = new Rgb(e[0], e[1], e[2]).ToHsv();
        return new ShownColor((byte)(e[4] << 4 | e[5] & 0x0F), hsv.H, hsv.S);
    }

    public bool Apply(byte effect, byte hue, byte sat, byte? speed, byte brightness)
    {
        var e = ReadEffect();
        e[3] = 255;                         // on
        e[4] = (byte)(effect >> 4);         // mode
        e[5] = (byte)(effect & 0x0F);       // sub-mode
        Fill(e, hue, sat, brightness, speed);
        WriteEffect(e, _timing.ReplyTimeoutMs);
        _current = e;
        return true;
    }

    public void ShowColor(byte hue, byte sat, byte brightness, byte? speed)
    {
        var e = _current ?? ReadEffect();
        Fill(e, hue, sat, brightness, speed);
        WriteEffect(e, _timing.FastReplyTimeoutMs);
        _current = e;
    }

    public bool Restore(SayoSnapshot snapshot)
    {
        WriteEffect(snapshot.Effect, _timing.ReplyTimeoutMs);
        return ReadEffect().AsSpan().SequenceEqual(snapshot.Effect);
    }

    /// <summary>The color at full value (brightness carries the intensity), speed and brightness as 1-100.</summary>
    private static void Fill(byte[] e, byte hue, byte sat, byte brightness, byte? speed)
    {
        var rgb = new Hsv(hue, sat, 255).ToRgb();
        (e[0], e[1], e[2]) = (rgb.R, rgb.G, rgb.B);
        if (speed is { } s) e[6] = Percent(s);
        e[7] = Percent(brightness);
    }

    private static byte Percent(byte value) => (byte)Math.Max(1, (int)Math.Round(value * 100 / 255.0, MidpointRounding.AwayFromZero));

    private byte[] ReadEffect() => TryReadEffect() ?? throw new IOException("The keyboard did not report its lighting.");

    private byte[]? TryReadEffect() =>
        Request([], _timing.ReplyTimeoutMs, tries: 4) is { Length: >= EffectLength } data ? data[..EffectLength] : null;

    private void WriteEffect(byte[] effect, int timeoutMs) => Request(effect, timeoutMs, tries: 2);

    /// <summary>
    /// Replies are matched on command and index and checked by crc and status, as the configurator does; other
    /// programs' reports are skipped and stale ones drained first. Null on timeout.
    /// </summary>
    private byte[]? Request(ReadOnlySpan<byte> data, int timeoutMs, int tries)
    {
        while (_hid.Read(1) is not null) { }
        _hid.Write(SayoFrame.Request(ReportId, CmdLedEffect, 0, data), ReportId);
        for (int i = 0; i < tries; i++)
        {
            if (_hid.Read(timeoutMs) is { } reply && SayoFrame.Data(ReportId, reply, CmdLedEffect, 0) is { } d) return d;
        }
        return null;
    }

    public void Dispose() => _hid.Dispose();
}
