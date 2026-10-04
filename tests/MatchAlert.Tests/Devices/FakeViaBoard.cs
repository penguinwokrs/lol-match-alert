// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.Devices.Hid;

namespace MatchAlert.Tests.Devices;

/// <summary>
/// A VIA keyboard at the byte level, standing in for the HID handle. Strict on purpose: a malformed
/// packet throws, so any framing regression fails whatever test happens to be running.
/// Modelled on kbd-signal's simulator and the behaviour it measured on real boards.
/// </summary>
internal sealed class FakeViaBoard : IRawHid
{
    private readonly Queue<byte[]> _responses = new();
    private readonly Queue<byte[]> _foreign = new();
    private int? _resetIn;

    /// <param name="protocol">9 for VIA v2 framing, 11 and up for v3 (channel byte).</param>
    /// <param name="channel">The v3 custom channel that drives the lights.</param>
    /// <param name="resetAfterWrites">Arms the post-effect reset: that many commands (sets or gets) after an
    /// effect change, color snaps to hue 0 and brightness to 255, once. Real firmware does this on a timer;
    /// counting commands stands in for time passing without a clock.</param>
    /// <param name="maxBrightness">Models v3's lossy brightness: stored as value*max/256, read back as stored*255/max.</param>
    public FakeViaBoard(int protocol = 13, int channel = 3, int? resetAfterWrites = null, int? maxBrightness = null)
    {
        Protocol = protocol;
        Channel = channel;
        ResetAfterWrites = resetAfterWrites;
        MaxBrightness = maxBrightness;
    }

    public int Protocol { get; }
    public int Channel { get; }
    public int? ResetAfterWrites { get; }
    public int? MaxBrightness { get; }

    public byte StoredBrightness { get; set; } = 200;
    public byte Effect { get; set; } = 6;
    public byte Speed { get; set; } = 128;
    public byte Hue { get; set; } = 142;
    public byte Sat { get; set; } = 255;

    /// <summary>Brightness as a GET reports it.</summary>
    public byte Brightness => MaxBrightness is { } max ? (byte)(StoredBrightness * 255 / max) : StoredBrightness;

    public List<byte[]> Packets { get; } = [];
    public int EffectWrites { get; private set; }
    public bool Disposed { get; private set; }
    public bool Unplugged { get; set; }

    /// <summary>Delivered just before the next response, as another program's echo would be.</summary>
    public void QueueForeignEcho(params byte[] payload) => _foreign.Enqueue(Pad(payload));

    public (byte Effect, byte Hue, byte Sat, byte Brightness, byte Speed) State => (Effect, Hue, Sat, Brightness, Speed);

    public void Write(ReadOnlySpan<byte> payload, byte reportId = 0)
    {
        if (reportId != 0) throw new InvalidOperationException($"this board has no report id {reportId}");
        if (Unplugged) throw new IOException("device not connected");
        if (payload.Length is 0 or > 32) throw new InvalidOperationException($"bad report length {payload.Length}");
        var p = Pad(payload.ToArray());
        Packets.Add(p);
        while (_foreign.Count > 0) _responses.Enqueue(_foreign.Dequeue());

        switch (p[0])
        {
            case 0x01:
                _responses.Enqueue(Pad([0x01, (byte)(Protocol >> 8), (byte)Protocol]));
                break;
            case 0x07:
                HandleSet(p);
                break;
            case 0x08:
                HandleGet(p);
                break;
            case 0x09:
                throw new InvalidOperationException("save (0x09) must never be sent");
            default:
                throw new InvalidOperationException($"unexpected command 0x{p[0]:X2}");
        }
    }

    public byte[]? Read(int timeoutMs) => Unplugged ? throw new IOException("device not connected")
        : _responses.Count > 0 ? _responses.Dequeue() : null;

    public void Dispose() => Disposed = true;

    private bool IsV3 => Protocol >= 11;

    private (int ValueId, int DataOffset)? Address(byte[] p)
    {
        if (!IsV3) return (p[1] switch { 0x80 => 1, 0x81 => 2, 0x82 => 3, 0x83 => 4, _ => -1 }, 2);
        if (p[1] != Channel) return null;   // a channel this board does not implement
        return (p[2], 3);
    }

    private void HandleSet(byte[] p)
    {
        if (Address(p) is not var (id, at))
        {
            _responses.Enqueue(Pad([0xFF]));   // id_unhandled
            return;
        }
        switch (id)
        {
            case 1: StoredBrightness = MaxBrightness is { } max ? (byte)(p[at] * max / 256) : p[at]; break;
            case 2: Effect = p[at]; EffectWrites++; _resetIn = ResetAfterWrites; break;
            case 3: Speed = p[at]; break;
            case 4: Hue = p[at]; Sat = p[at + 1]; break;
            default: throw new InvalidOperationException($"unknown value id {id}");
        }
        _responses.Enqueue(p);   // QMK echoes the command
        if (id != 2) Tick();
    }

    private void Tick()
    {
        if (_resetIn is not { } n) return;
        if (n > 1)
        {
            _resetIn = n - 1;
            return;
        }
        _resetIn = null;
        Hue = 0;
        StoredBrightness = MaxBrightness is { } max ? (byte)max : (byte)255;
    }

    private void HandleGet(byte[] p)
    {
        if (Address(p) is not var (id, at))
        {
            _responses.Enqueue(Pad([0xFF]));
            return;
        }
        var r = (byte[])p.Clone();
        switch (id)
        {
            case 1: r[at] = Brightness; break;
            case 2: r[at] = Effect; break;
            case 3: r[at] = Speed; break;
            case 4: r[at] = Hue; r[at + 1] = Sat; break;
            default: throw new InvalidOperationException($"unknown value id {id}");
        }
        _responses.Enqueue(r);
        Tick();
    }

    private static byte[] Pad(byte[] payload)
    {
        var p = new byte[32];
        payload.AsSpan(0, Math.Min(32, payload.Length)).CopyTo(p);
        return p;
    }
}
