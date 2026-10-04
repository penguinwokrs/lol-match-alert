// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.Devices.Hid;

namespace MatchAlert.Tests.Devices;

/// <summary>
/// A Pulsar keyboard at the byte level, as Pulsar's web configurator (bbb.pulsar.gg, "nKey") talks to it:
/// <c>24</c> answers the active profile, <c>22 02 id P</c> answers a value from byte 4, <c>23 02 id P …</c>
/// sets it. Nothing here was observed on hardware.
/// <para>
/// Strict, and an allowlist: any command this app has no business sending throws - reset (0A), bootloader
/// (0B), firmware update (A0-A5), save (23 FE), NKRO (23 01), polling rate (23 04), and anything unknown.
/// A request naming another profile than the active one throws too.
/// </para>
/// </summary>
internal sealed class FakePulsarBoard(byte profile = 1, bool echoHeader = true) : IRawHid
{
    private readonly Queue<byte[]> _responses = new();
    private readonly Queue<byte[]> _foreign = new();

    public byte ActiveProfile { get; } = profile;
    public byte Brightness { get; set; } = 180;
    public byte Effect { get; set; } = 13;
    public byte Speed { get; set; } = 90;
    public byte Hue { get; set; } = 140;
    public byte Sat { get; set; } = 200;
    public bool Enabled { get; set; } = true;
    public bool Silent { get; set; }

    public List<byte[]> Packets { get; } = [];
    public int EffectWrites { get; private set; }

    public (byte, byte, byte, byte, byte, bool) State => (Brightness, Effect, Speed, Hue, Sat, Enabled);

    public void QueueForeign(params byte[] payload) => _foreign.Enqueue(Pad(payload));

    public void Write(ReadOnlySpan<byte> payload)
    {
        if (payload.Length is 0 or > 64) throw new InvalidOperationException($"bad report length {payload.Length}");
        var p = Pad(payload.ToArray());
        Packets.Add(p);
        if (Silent) return;
        while (_foreign.Count > 0) _responses.Enqueue(_foreign.Dequeue());

        switch (p[0])
        {
            case 0x24:
                _responses.Enqueue(Pad([0x24, ActiveProfile, 3]));
                return;
            case 0x22 when p[1] == 0x02 && p[2] is >= 1 and <= 5:
                RequireProfile(p);
                var reply = echoHeader ? Pad([0x22, 0x02, p[2], p[3]]) : Pad([0x22]);
                switch (p[2])
                {
                    case 1: reply[4] = Brightness; break;
                    case 2: reply[4] = Effect; break;
                    case 3: reply[4] = Speed; break;
                    case 4: reply[4] = Hue; reply[5] = Sat; break;
                    case 5: reply[4] = (byte)(Enabled ? 1 : 0); break;
                }
                _responses.Enqueue(reply);
                return;
            case 0x23 when p[1] == 0x02 && p[2] is >= 1 and <= 5:
                RequireProfile(p);
                switch (p[2])
                {
                    case 1: Brightness = p[4]; break;
                    case 2: Effect = p[4]; EffectWrites++; break;
                    case 3: Speed = p[4]; break;
                    case 4: Hue = p[4]; Sat = p[5]; break;
                    case 5: Enabled = p[4] == 1; break;
                }
                _responses.Enqueue(echoHeader ? p : Pad([0x23]));
                return;
            default:
                throw new InvalidOperationException($"command {Convert.ToHexString(p.AsSpan(0, 4))} must never be sent");
        }
    }

    public byte[]? Read(int timeoutMs) => _responses.Count > 0 ? _responses.Dequeue() : null;

    public void Dispose() { }

    private void RequireProfile(byte[] p)
    {
        if (p[3] != ActiveProfile) throw new InvalidOperationException($"profile {p[3]} named, {ActiveProfile} active");
    }

    private static byte[] Pad(byte[] payload)
    {
        var p = new byte[64];
        payload.AsSpan(0, Math.Min(64, payload.Length)).CopyTo(p);
        return p;
    }
}
