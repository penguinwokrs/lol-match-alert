// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

namespace MatchAlert.Devices.Sayo;

/// <summary>
/// SayoDevice framing, as Pulsar's configurator Bibimbap (bbb.pulsar.gg, the "sKey" app) builds it in its
/// Rust/wasm library. Offsets are within the payload that follows the report id:
/// <code>
/// [0] 0x12   [1..2] crc (u16 LE)   [3..4] len | status &lt;&lt; 10 (u16 LE)   [5] cmd   [6] index   [7..] data
/// </code>
/// len is the data length plus 4. The crc is the 16-bit wrapping sum of the little-endian words of the
/// whole report, report id included, with the crc field taken as zero. Checked against packets the
/// official library emitted, in the tests.
/// </summary>
internal static class SayoFrame
{
    public const byte Echo = 0x12;
    private const int Header = 7;

    public static byte[] Request(byte reportId, byte cmd, byte index, ReadOnlySpan<byte> data)
    {
        var p = new byte[Header + data.Length];
        p[0] = Echo;
        int len = data.Length + 4;
        p[3] = (byte)len;
        p[4] = (byte)(len >> 8);
        p[5] = cmd;
        p[6] = index;
        data.CopyTo(p.AsSpan(Header));
        ushort crc = Crc(reportId, p);
        p[1] = (byte)crc;
        p[2] = (byte)(crc >> 8);
        return p;
    }

    /// <summary>The data of a reply to (cmd, index), or null if it is not one, is broken, or reports a failure.</summary>
    public static byte[]? Data(byte reportId, byte[] reply, byte cmd, byte index)
    {
        if (reply.Length < Header || reply[5] != cmd || reply[6] != index) return null;
        if ((ushort)(reply[1] | reply[2] << 8) != Crc(reportId, reply)) return null;
        int word = reply[3] | reply[4] << 8;
        int len = word & 0x3FF, status = word >> 10;
        if (status != 0 || len < 4 || Header + len - 4 > reply.Length) return null;
        return reply.AsSpan(Header, len - 4).ToArray();
    }

    /// <summary>Sum of little-endian words over report id + payload, with the crc bytes counted as zero.</summary>
    public static ushort Crc(byte reportId, ReadOnlySpan<byte> payload)
    {
        // Byte i of the payload is byte i+1 of the report: even report offsets are low bytes.
        int sum = reportId;
        for (int i = 0; i < payload.Length; i++)
        {
            if (i is 1 or 2) continue;
            sum += (i + 1) % 2 == 0 ? payload[i] : payload[i] << 8;
        }
        return (ushort)sum;
    }
}
