// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MatchAlert.Devices.Hid;

/// <summary>HID through SetupAPI and the Windows HID class driver.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsHidBus : IHidBus
{
    public IReadOnlyList<HidDeviceInfo> Enumerate()
    {
        var results = new List<HidDeviceInfo>();
        NativeMethods.HidD_GetHidGuid(out var hidGuid);
        IntPtr set = NativeMethods.SetupDiGetClassDevsW(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
            NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);
        if (set == NativeMethods.INVALID_HANDLE_VALUE)
            throw new IOException($"Could not list HID devices (error {Marshal.GetLastWin32Error()}).");
        try
        {
            for (uint index = 0; ; index++)
            {
                var did = new SpDeviceInterfaceData { CbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
                if (!NativeMethods.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref did)) break;
                if (DevicePath(set, ref did) is { } path && Describe(path) is { } info) results.Add(info);
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(set);
        }
        return results;
    }

    public IRawHid Open(HidDeviceInfo device)
    {
        var handle = NativeMethods.CreateFileW(device.Path,
            NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero, NativeMethods.OPEN_EXISTING, NativeMethods.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new IOException($"Could not open {device.Product} (error {Marshal.GetLastWin32Error()}). Is it plugged in by cable?");
        return new RawHid(handle, device.InputReportLength, device.OutputReportLength);
    }

    private static string? DevicePath(IntPtr set, ref SpDeviceInterfaceData did)
    {
        NativeMethods.SetupDiGetDeviceInterfaceDetailW(set, ref did, IntPtr.Zero, 0, out uint required, IntPtr.Zero);
        if (required == 0) return null;
        IntPtr buffer = Marshal.AllocHGlobal((int)required);
        try
        {
            // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize: 8 on x64, 6 on x86.
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            return NativeMethods.SetupDiGetDeviceInterfaceDetailW(set, ref did, buffer, required, out _, IntPtr.Zero)
                ? Marshal.PtrToStringUni(buffer + 4)
                : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Opened with no access rights, which is enough to read capabilities and never disturbs the device.</summary>
    private static HidDeviceInfo? Describe(string path)
    {
        using var handle = NativeMethods.CreateFileW(path, 0,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid) return null;

        var attributes = new HiddAttributes { Size = Marshal.SizeOf<HiddAttributes>() };
        if (!NativeMethods.HidD_GetAttributes(handle, ref attributes)) return null;
        if (!NativeMethods.HidD_GetPreparsedData(handle, out IntPtr preparsed)) return null;
        try
        {
            var caps = new HidpCaps();
            if (NativeMethods.HidP_GetCaps(preparsed, ref caps) != NativeMethods.HIDP_STATUS_SUCCESS) return null;
            var product = new StringBuilder(256);
            if (!NativeMethods.HidD_GetProductString(handle, product, product.Capacity * 2)) product.Clear();
            return new HidDeviceInfo(path, attributes.VendorId, attributes.ProductId, caps.UsagePage, caps.Usage,
                caps.InputReportByteLength, caps.OutputReportByteLength, product.ToString().Trim());
        }
        finally
        {
            NativeMethods.HidD_FreePreparsedData(preparsed);
        }
    }

    /// <summary>
    /// Report I/O through an async FileStream over the overlapped handle. The runtime pins buffers for
    /// the whole operation and turns a cancelled wait into CancelIoEx, so a read with a timeout never
    /// leaves the kernel writing into memory the GC has moved - the trap with hand-rolled overlapped I/O.
    /// </summary>
    private sealed class RawHid(SafeFileHandle handle, int inputLength, int outputLength) : IRawHid
    {
        private readonly FileStream _stream = new(handle, FileAccess.ReadWrite, bufferSize: 0, isAsync: true);

        public void Write(ReadOnlySpan<byte> payload)
        {
            // Report id 0 first: Windows wants it explicitly even when the device has no report ids.
            var report = new byte[outputLength];
            payload[..Math.Min(payload.Length, outputLength - 1)].CopyTo(report.AsSpan(1));
            using var cts = new CancellationTokenSource(1000);
            _stream.WriteAsync(report, cts.Token).AsTask().GetAwaiter().GetResult();
        }

        public byte[]? Read(int timeoutMs)
        {
            var report = new byte[inputLength];
            using var cts = new CancellationTokenSource(Math.Max(1, timeoutMs));
            try
            {
                int n = _stream.ReadAsync(report, cts.Token).AsTask().GetAwaiter().GetResult();
                return n <= 1 ? null : report[1..n];
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        public void Dispose() => _stream.Dispose();
    }
}
