// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MatchAlert.Tests.Devices;

/// <summary>
/// An OpenRGB SDK server on loopback, written from OpenRGB's protocol documentation independently of the
/// client under test: it serialises device data per protocol version and applies the client's updates.
/// </summary>
internal sealed class FakeOpenRgbServer : IDisposable
{
    internal sealed class Mode(string name, uint flags, uint colorMode)
    {
        public string Name = name;
        public uint Flags = flags, ColorMode = colorMode, Speed = 1, Brightness = 100, Direction;
        public uint[] Colors = [];
    }

    internal sealed class Controller(int type, string name, int leds, params Mode[] modes)
    {
        public int Type = type;
        public string Name = name;
        public List<Mode> Modes = [.. modes];
        public int ActiveMode = modes.Length - 1;
        public uint[] Colors = Enumerable.Range(0, leds).Select(i => (uint)(0x010101 * (i + 1))).ToArray();
        public int CustomModeCalls;
    }

    public static Controller Keyboard(string name = "Fake Keyboard") => new(5, name, 4,
        new Mode("Direct", 1 << 5, 1), new Mode("Breathing", 1 << 0 | 1 << 6, 2) { Speed = 7, Colors = [0x00FF00] });

    public static Controller Mouse() => new(6, "Fake Mouse", 2, new Mode("Static", 1 << 6, 2) { Colors = [0x0000FF] });

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();

    public FakeOpenRgbServer(uint version = 4, params Controller[] controllers)
    {
        Version = version;
        Controllers.AddRange(controllers);
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    public uint Version { get; }
    public List<Controller> Controllers { get; } = [];
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public List<string> ClientNames { get; } = [];
    public bool AnnounceListChanges { get; set; }

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch { return; }
            _ = Task.Run(() => Handle(client));
        }
    }

    private void Handle(TcpClient client)
    {
        using var _ = client;
        var s = client.GetStream();
        uint version = 0;
        try
        {
            while (true)
            {
                var h = new byte[16];
                s.ReadExactly(h);
                if (Encoding.ASCII.GetString(h, 0, 4) != "ORGB") throw new InvalidDataException("magic");
                uint dev = U32(h, 4), id = U32(h, 8), size = U32(h, 12);
                var data = new byte[size];
                s.ReadExactly(data);
                if (AnnounceListChanges) s.Write(Packet(0, 100, []));   // unsolicited DEVICE_LIST_UPDATED
                lock (Controllers)
                {
                    switch (id)
                    {
                        case 50: ClientNames.Add(Encoding.UTF8.GetString(data).TrimEnd('\0')); break;
                        case 40:
                            version = Math.Min(U32(data, 0), Version);
                            if (Version > 0) s.Write(Packet(0, 40, BitConverter.GetBytes(Version)));
                            break;
                        case 0: s.Write(Packet(0, 0, BitConverter.GetBytes((uint)Controllers.Count))); break;
                        case 1:
                            uint asked = data.Length >= 4 ? U32(data, 0) : 0;
                            s.Write(Packet(dev, 1, Describe(Controllers[(int)dev], Math.Min(asked, Version))));
                            break;
                        case 1100:
                            var c = Controllers[(int)dev];
                            c.ActiveMode = c.Modes.FindIndex(m => m.Name == "Direct");
                            c.CustomModeCalls++;
                            break;
                        case 1050:
                            int n = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4));
                            for (int i = 0; i < n; i++) Controllers[(int)dev].Colors[i] = U32(data, 6 + 4 * i);
                            break;
                        case 1101: ApplyMode(Controllers[(int)dev], data, version); break;
                        default: throw new InvalidOperationException($"unexpected packet {id}");
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or EndOfStreamException or ObjectDisposedException) { }
    }

    private static void ApplyMode(Controller c, byte[] d, uint version)
    {
        int idx = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(4));
        int p = 8;
        var m = c.Modes[idx];
        int nameLen = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(p)); p += 2 + nameLen;
        p += 4;                                              // value
        m.Flags = U32(d, p); p += 4;
        p += 8;                                              // speed min/max
        if (version >= 3) p += 8;                            // brightness min/max
        p += 8;                                              // colors min/max
        m.Speed = U32(d, p); p += 4;
        if (version >= 3) { m.Brightness = U32(d, p); p += 4; }
        m.Direction = U32(d, p); p += 4;
        m.ColorMode = U32(d, p); p += 4;
        int colors = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(p)); p += 2;
        m.Colors = Enumerable.Range(0, colors).Select(i => U32(d, p + 4 * i)).ToArray();
        c.ActiveMode = idx;
    }

    /// <summary>Device Data, per the protocol version asked for.</summary>
    private static byte[] Describe(Controller c, uint v)
    {
        var w = new List<byte>();
        void U16(int x) => w.AddRange(BitConverter.GetBytes((ushort)x));
        void I32(int x) => w.AddRange(BitConverter.GetBytes(x));
        void W32(uint x) => w.AddRange(BitConverter.GetBytes(x));
        void Text(string s) { var b = Encoding.UTF8.GetBytes(s + "\0"); U16(b.Length); w.AddRange(b); }

        I32(c.Type); Text(c.Name);
        if (v >= 1) Text("Fake Vendor");
        Text("description"); Text("1.0"); Text("serial"); Text("HID: fake");
        U16(c.Modes.Count); I32(c.ActiveMode);
        foreach (var m in c.Modes)
        {
            Text(m.Name); I32(0); W32(m.Flags); W32(0); W32(10);
            if (v >= 3) { W32(0); W32(100); }
            W32(0); W32(1); W32(m.Speed);
            if (v >= 3) W32(m.Brightness);
            W32(m.Direction); W32(m.ColorMode);
            U16(m.Colors.Length); foreach (var col in m.Colors) W32(col);
        }
        U16(1);                                              // one zone, with a 1x2 matrix map
        Text("Keys"); I32(2); W32(1); W32((uint)c.Colors.Length); W32((uint)c.Colors.Length);
        U16(16); W32(1); W32(2); W32(0); W32(1);
        if (v >= 4) { U16(1); Text("Seg"); I32(0); W32(0); W32((uint)c.Colors.Length); }
        if (v >= 5) W32(0);
        U16(c.Colors.Length); for (int i = 0; i < c.Colors.Length; i++) { Text($"Key {i}"); W32((uint)i); }
        U16(c.Colors.Length); foreach (var col in c.Colors) W32(col);

        var body = w.ToArray();
        return [.. BitConverter.GetBytes((uint)(body.Length + 4)), .. body];
    }

    private static byte[] Packet(uint dev, uint id, byte[] data) =>
        [.. "ORGB"u8.ToArray(), .. BitConverter.GetBytes(dev), .. BitConverter.GetBytes(id), .. BitConverter.GetBytes((uint)data.Length), .. data];

    private static uint U32(byte[] b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }
}
