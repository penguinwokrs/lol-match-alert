// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.Devices.Hid;

namespace MatchAlert.Devices.Pulsar;

/// <summary>The lighting a Pulsar keyboard reports, as raw bytes so a restore replays exactly what was read.</summary>
public sealed record PulsarSnapshot(byte Profile, byte Brightness, byte Effect, byte Speed, byte Hue, byte Sat, bool Enabled)
{
    public override string ToString() =>
        $"profile {Profile}, effect {Effect}, speed {Speed}, brightness {Brightness}, hue {Hue}, sat {Sat}, {(Enabled ? "on" : "off")}";
}

public sealed record PulsarTiming(int ReplyTimeoutMs = 250, int FastReplyTimeoutMs = 20);

/// <summary>
/// The lighting protocol of Pulsar's QMK-based keyboards (PCMK 2HE TKL, XBOARD MS), as Pulsar's own web
/// configurator Bibimbap (bbb.pulsar.gg, the "nKey" app) speaks it. NOT VERIFIED ON HARDWARE: every byte
/// here is read from that configurator's code, not observed on a board.
/// <para>
/// 64-byte reports on the raw HID interface, command first. Lighting is not VIA's 0x07/0x08: it is
/// <c>22 02 id P</c> to read (value from reply byte 4) and <c>23 02 id P value…</c> to write, where P is the
/// active profile from <c>24</c>. The configurator follows some writes with <c>23 FE</c>, a separate save;
/// this class never sends it, nor anything outside <c>24</c>, <c>22 02 01-05</c> and <c>23 02 01-05</c>.
/// Whether a write without the save still reaches flash is unknown; profiles keep writes rare for that reason.
/// </para>
/// </summary>
public sealed class PulsarKeyboard : IKeyboardLink<PulsarSnapshot>
{
    public const ushort VendorId = 0x3710;
    public const ushort UsagePage = 0xFF60;
    public const ushort Usage = 0x61;

    private const byte CmdGet = 0x22, CmdSet = 0x23, CmdActiveProfile = 0x24, GroupRgb = 0x02;
    private const int ValueOffset = 4;

    private readonly IRawHid _hid;
    private readonly PulsarTiming _timing;

    public PulsarKeyboard(IRawHid hid, PulsarTiming? timing = null)
    {
        _hid = hid;
        _timing = timing ?? new PulsarTiming();
        var reply = Request([CmdActiveProfile], tries: 4, _timing.ReplyTimeoutMs)
            ?? throw new IOException("The keyboard did not answer as a Pulsar keyboard.");
        Profile = reply[1];
    }

    /// <summary>The on-board profile in use. Every read and write names it.</summary>
    public byte Profile { get; }

    public PulsarSnapshot Snapshot()
    {
        var color = Read(Value.Color, 2);
        return new PulsarSnapshot(Profile, Read(Value.Brightness, 1)[0], Read(Value.Effect, 1)[0], Read(Value.Speed, 1)[0],
            color[0], color[1], Read(Value.Enabled, 1)[0] == 1);
    }

    public ShownColor Showing()
    {
        var color = Read(Value.Color, 2);
        return new ShownColor(Read(Value.Effect, 1)[0], color[0], color[1]);
    }

    /// <summary>
    /// The configurator converts hue with a /365 scale in its color picker; the firmware is QMK rgb_matrix
    /// (its 44 effects are QMK's, in QMK's order), so the byte is the ordinary 0-255 wheel and goes through as is.
    /// </summary>
    public bool Apply(byte effect, byte hue, byte sat, byte? speed, byte brightness)
    {
        Write(Value.Effect, [effect]);
        if (speed is { } s) Write(Value.Speed, [s]);
        Write(Value.Color, [hue, sat]);
        Write(Value.Brightness, [brightness]);
        Write(Value.Enabled, [1]);   // the user may have switched the LEDs off
        return Read(Value.Effect, 1)[0] == effect;
    }

    public void ShowColor(byte hue, byte sat, byte brightness, byte? speed)
    {
        if (speed is { } s) Write(Value.Speed, [s], fast: true);
        Write(Value.Color, [hue, sat], fast: true);
        Write(Value.Brightness, [brightness], fast: true);
    }

    public bool Restore(PulsarSnapshot s)
    {
        Write(Value.Effect, [s.Effect]);
        Write(Value.Speed, [s.Speed]);
        Write(Value.Color, [s.Hue, s.Sat]);
        Write(Value.Brightness, [s.Brightness]);
        Write(Value.Enabled, [(byte)(s.Enabled ? 1 : 0)]);
        var color = Read(Value.Color, 2);
        return Read(Value.Effect, 1)[0] == s.Effect && color[0] == s.Hue && color[1] == s.Sat;
    }

    private byte[] Read(Value value, int length)
    {
        var reply = Request([CmdGet, GroupRgb, (byte)value, Profile], tries: 4, _timing.ReplyTimeoutMs)
            ?? throw new IOException($"The keyboard did not report its {value.ToString().ToLowerInvariant()}.");
        return reply.AsSpan(ValueOffset, length).ToArray();
    }

    /// <summary>A write's reply carries nothing the configurator uses; it is waited for only to keep the queue clean.</summary>
    private void Write(Value value, byte[] data, bool fast = false) =>
        Request([CmdSet, GroupRgb, (byte)value, Profile, .. data], tries: fast ? 1 : 2,
            fast ? _timing.FastReplyTimeoutMs : _timing.ReplyTimeoutMs);

    /// <summary>
    /// Matched on the command byte only: the configurator itself matches nothing (one request at a time),
    /// and whether the firmware echoes the rest of the header is inferred, not known. Stale reports are
    /// drained first. Null on timeout - a board that does not speak this protocol fails here, cleanly.
    /// </summary>
    private byte[]? Request(byte[] payload, int tries, int timeoutMs)
    {
        while (_hid.Read(1) is not null) { }
        _hid.Write(payload);
        for (int i = 0; i < tries; i++)
        {
            if (_hid.Read(timeoutMs) is { Length: > ValueOffset } reply && reply[0] == payload[0]) return reply;
        }
        return null;
    }

    private enum Value : byte
    {
        Brightness = 1,
        Effect = 2,
        Speed = 3,
        Color = 4,
        Enabled = 5,
    }

    public void Dispose() => _hid.Dispose();
}
