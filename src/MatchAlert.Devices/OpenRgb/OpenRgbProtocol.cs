// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Buffers.Binary;
using System.Text;

namespace MatchAlert.Devices.OpenRgb;

/// <summary>One lighting mode of an OpenRGB device, with its serialised form kept to send back unchanged.</summary>
public sealed record OpenRgbMode(string Name, uint Flags, uint ColorMode, byte[] Raw);

/// <summary>An OpenRGB device as the server described it: what to show it on, and what to put back.</summary>
public sealed record OpenRgbController(uint Index, int Type, string Name, string Vendor, int ActiveMode, IReadOnlyList<OpenRgbMode> Modes, uint[] Colors)
{
    public override string ToString() =>
        $"mode {(ActiveMode >= 0 && ActiveMode < Modes.Count ? Modes[ActiveMode].Name : ActiveMode.ToString())}, {Colors.Length} LEDs";
}

/// <summary>
/// The OpenRGB SDK wire format (Documentation/OpenRGBSDK.md in OpenRGB): a 16-byte header, "ORGB", device,
/// packet id and size, all little-endian, then the packet data. This client speaks protocol 4 at most: the
/// version OpenRGB 0.9 ships, and one OpenRGB 1.0 (protocol 6) still serves to older clients by index.
/// </summary>
public static class OpenRgbProtocol
{
    public const uint ClientVersion = 4;

    public const uint RequestControllerCount = 0;
    public const uint RequestControllerData = 1;
    public const uint RequestProtocolVersion = 40;
    public const uint SetClientName = 50;
    public const uint UpdateLeds = 1050;
    public const uint SetCustomMode = 1100;
    public const uint UpdateMode = 1101;

    public const int HeaderSize = 16;
    private static readonly byte[] Magic = "ORGB"u8.ToArray();

    public static byte[] Packet(uint device, uint id, ReadOnlySpan<byte> data)
    {
        var p = new byte[HeaderSize + data.Length];
        Magic.CopyTo(p, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4), device);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(8), id);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(12), (uint)data.Length);
        data.CopyTo(p.AsSpan(HeaderSize));
        return p;
    }

    public static (uint Device, uint Id, uint Size) Header(ReadOnlySpan<byte> h)
    {
        if (!h[..4].SequenceEqual(Magic)) throw new InvalidDataException("not an OpenRGB SDK packet");
        return (BinaryPrimitives.ReadUInt32LittleEndian(h[4..]), BinaryPrimitives.ReadUInt32LittleEndian(h[8..]), BinaryPrimitives.ReadUInt32LittleEndian(h[12..]));
    }

    public static byte[] U32(uint value)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, value);
        return b;
    }

    /// <summary>RGBColor is 0x00BBGGRR: on the wire, R, G, B, 0.</summary>
    public static uint Color(byte r, byte g, byte b) => (uint)(b << 16 | g << 8 | r);

    /// <summary>data_size (counting itself, as OpenRGB's own client does), LED count, colors.</summary>
    public static byte[] LedsData(IReadOnlyList<uint> colors)
    {
        var d = new byte[4 + 2 + 4 * colors.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(d, (uint)d.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(4), (ushort)colors.Count);
        for (int i = 0; i < colors.Count; i++) BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(6 + 4 * i), colors[i]);
        return d;
    }

    /// <summary>data_size, mode index, then the mode exactly as the server described it.</summary>
    public static byte[] ModeData(int index, byte[] rawMode)
    {
        var d = new byte[8 + rawMode.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(d, (uint)d.Length);
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(4), index);
        rawMode.CopyTo(d, 8);
        return d;
    }

    /// <summary>Parses a REQUEST_CONTROLLER_DATA reply at the negotiated <paramref name="version"/> (0-5).</summary>
    public static OpenRgbController Controller(uint index, byte[] data, uint version)
    {
        var r = new Reader(data);
        r.U32();   // data_size
        int type = r.I32();
        string name = r.Text();
        string vendor = version >= 1 ? r.Text() : "";
        r.Text(); r.Text(); r.Text(); r.Text();   // description, version, serial, location

        int modeCount = r.U16();
        int active = r.I32();
        var modes = new List<OpenRgbMode>(modeCount);
        for (int m = 0; m < modeCount; m++)
        {
            int start = r.Position;
            string modeName = r.Text();
            r.I32();                               // value (internal)
            uint flags = r.U32();
            r.U32(); r.U32();                      // speed min, max
            if (version >= 3) { r.U32(); r.U32(); } // brightness min, max
            r.U32(); r.U32();                      // colors min, max
            r.U32();                               // speed
            if (version >= 3) r.U32();             // brightness
            r.U32();                               // direction
            uint colorMode = r.U32();
            r.Skip(4 * r.U16());                   // mode colors
            modes.Add(new OpenRgbMode(modeName, flags, colorMode, data[start..r.Position]));
        }

        int zoneCount = r.U16();
        for (int z = 0; z < zoneCount; z++)
        {
            r.Text(); r.I32(); r.U32(); r.U32(); r.U32();   // name, type, leds min, max, count
            r.Skip(r.U16());                                // matrix map
            if (version >= 4)
            {
                int segments = r.U16();
                for (int s = 0; s < segments; s++) { r.Text(); r.I32(); r.U32(); r.U32(); }
            }
            if (version >= 5) r.U32();                      // zone flags
        }

        int ledCount = r.U16();
        for (int l = 0; l < ledCount; l++) { r.Text(); r.U32(); }

        var colors = new uint[r.U16()];
        for (int c = 0; c < colors.Length; c++) colors[c] = r.U32();

        return new OpenRgbController(index, type, name, vendor, active, modes, colors);
    }

    private sealed class Reader(byte[] data)
    {
        public int Position { get; private set; }

        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public void Skip(int n) => Take(n);

        /// <summary>Length-prefixed, null-terminated.</summary>
        public string Text()
        {
            var bytes = Take(U16());
            return Encoding.UTF8.GetString(bytes.TrimEnd((byte)0));
        }

        private ReadOnlySpan<byte> Take(int n)
        {
            if (n < 0 || Position + n > data.Length) throw new InvalidDataException("OpenRGB device data ended early");
            var span = data.AsSpan(Position, n);
            Position += n;
            return span;
        }
    }
}
