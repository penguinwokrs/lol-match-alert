// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.Devices.Hid;

namespace MatchAlert.Tests.Devices;

/// <summary>
/// A SayoDevice keyboard at the byte level: report id 0x22, framed and checksummed as Pulsar's configurator
/// library does it (the frame tests pin that to packets it emitted). Answers 0x26 only, read or write;
/// anything else - save all (0x0D), reboot (0x0E), other report ids, a bad crc - throws.
/// </summary>
internal sealed class FakeSayoBoard : IRawHid
{
    private readonly Queue<byte[]> _responses = new();

    public FakeSayoBoard(byte reportId = 0x22)
    {
        ReportId = reportId;
        for (int i = 0; i < 48; i++) Effect[i] = (byte)(0x80 + i);
        Effect[3] = 255;   // on
        Effect[4] = 1;     // Rainbow
        Effect[5] = 2;     // Moving Chevron
        Effect[6] = 40;    // speed
        Effect[7] = 70;    // brightness
    }

    public byte ReportId { get; }
    public byte[] Effect { get; } = new byte[48];
    public List<(byte ReportId, byte[] Payload)> Packets { get; } = [];
    public int Writes { get; private set; }

    public void Write(ReadOnlySpan<byte> payload, byte reportId = 0)
    {
        if (reportId != ReportId) throw new IOException($"no output report {reportId:X2}");   // as Windows refuses it
        var p = payload.ToArray();
        Packets.Add((reportId, p));
        if (p[0] != 0x12) throw new InvalidOperationException("bad echo byte");
        if ((p[1] | p[2] << 8) != Checksum(reportId, p)) throw new InvalidOperationException("bad crc");
        if (p[5] != 0x26 || p[6] != 0) throw new InvalidOperationException($"command {p[5]:X2} must never be sent");
        int len = p[3] | p[4] << 8;
        if (len == 52)
        {
            p.AsSpan(7, 48).CopyTo(Effect);
            Writes++;
        }
        else if (len != 4) throw new InvalidOperationException($"bad length {len}");
        _responses.Enqueue(Reply(Effect));
    }

    public byte[]? Read(int timeoutMs) => _responses.Count > 0 ? _responses.Dequeue() : null;

    public void Dispose() { }

    /// <summary>Another program's report, or a reply to someone else's command, landing in our queue.</summary>
    public void QueueForeign(byte[] payload) => _responses.Enqueue(payload);

    private byte[] Reply(byte[] data)
    {
        var r = new byte[63];
        r[0] = 0x12;
        r[3] = 52;
        r[5] = 0x26;
        data.CopyTo(r.AsSpan(7));
        int crc = Checksum(ReportId, r);
        (r[1], r[2]) = ((byte)crc, (byte)(crc >> 8));
        return r;
    }

    /// <summary>Written independently of the code under test: words of the whole report, crc field skipped.</summary>
    internal static int Checksum(byte reportId, byte[] payload)
    {
        var report = new byte[payload.Length + 1 + 1];
        report[0] = reportId;
        payload.CopyTo(report, 1);
        report[2] = report[3] = 0;
        int sum = 0;
        for (int i = 0; i + 1 < report.Length; i += 2) sum += report[i] | report[i + 1] << 8;
        return sum & 0xFFFF;
    }
}
