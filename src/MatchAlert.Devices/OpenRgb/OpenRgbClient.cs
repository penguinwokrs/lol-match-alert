// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Net.Sockets;
using System.Text;

namespace MatchAlert.Devices.OpenRgb;

/// <summary>
/// A connection to OpenRGB's SDK server (OpenRGB with "Start Server" on, port 6742 by default). One request
/// at a time; unrelated packets the server sends on its own, such as device-list updates, are skipped.
/// </summary>
public sealed class OpenRgbClient : IDisposable
{
    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;

    private OpenRgbClient(TcpClient tcp, TimeSpan timeout)
    {
        _tcp = tcp;
        _stream = tcp.GetStream();
        _stream.ReadTimeout = _stream.WriteTimeout = (int)timeout.TotalMilliseconds;
    }

    /// <summary>The protocol both sides speak: the lower of theirs and <see cref="OpenRgbProtocol.ClientVersion"/>.</summary>
    public uint Protocol { get; private set; }

    public static OpenRgbClient Connect(string host, int port, string clientName, TimeSpan timeout)
    {
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            if (!tcp.ConnectAsync(host, port).Wait(timeout)) throw new IOException($"OpenRGB did not answer on {host}:{port}");
            var client = new OpenRgbClient(tcp, timeout);
            client.Send(0, OpenRgbProtocol.SetClientName, Encoding.UTF8.GetBytes(clientName + "\0"));
            client.Send(0, OpenRgbProtocol.RequestProtocolVersion, OpenRgbProtocol.U32(OpenRgbProtocol.ClientVersion));
            // A protocol-0 server never answers this; the timeout means "speak 0".
            client.Protocol = client.TryReceive(OpenRgbProtocol.RequestProtocolVersion) is { Length: >= 4 } v
                ? Math.Min(BitConverter.ToUInt32(v), OpenRgbProtocol.ClientVersion)
                : 0;
            return client;
        }
        catch (Exception e) when (e is SocketException or AggregateException)
        {
            tcp.Dispose();
            throw new IOException($"OpenRGB is not reachable on {host}:{port} (is OpenRGB running with its SDK server on?)", e);
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    public uint ControllerCount()
    {
        Send(0, OpenRgbProtocol.RequestControllerCount, []);
        return BitConverter.ToUInt32(Receive(OpenRgbProtocol.RequestControllerCount));
    }

    public OpenRgbController Controller(uint index)
    {
        Send(index, OpenRgbProtocol.RequestControllerData, Protocol == 0 ? [] : OpenRgbProtocol.U32(Protocol));
        return OpenRgbProtocol.Controller(index, Receive(OpenRgbProtocol.RequestControllerData), Protocol);
    }

    public void SetCustomMode(uint index) => Send(index, OpenRgbProtocol.SetCustomMode, []);

    public void UpdateLeds(uint index, IReadOnlyList<uint> colors) => Send(index, OpenRgbProtocol.UpdateLeds, OpenRgbProtocol.LedsData(colors));

    public void UpdateMode(uint index, int mode, byte[] rawMode) => Send(index, OpenRgbProtocol.UpdateMode, OpenRgbProtocol.ModeData(mode, rawMode));

    private void Send(uint device, uint id, ReadOnlySpan<byte> data) => _stream.Write(OpenRgbProtocol.Packet(device, id, data));

    private byte[] Receive(uint id) => TryReceive(id) ?? throw new IOException("OpenRGB did not reply");

    private byte[]? TryReceive(uint id)
    {
        try
        {
            while (true)
            {
                var header = new byte[OpenRgbProtocol.HeaderSize];
                _stream.ReadExactly(header);
                var (_, packetId, size) = OpenRgbProtocol.Header(header);
                var data = new byte[size];
                _stream.ReadExactly(data);
                if (packetId == id) return data;
            }
        }
        catch (IOException e) when (e.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut })
        {
            return null;
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
        _tcp.Dispose();
    }
}
