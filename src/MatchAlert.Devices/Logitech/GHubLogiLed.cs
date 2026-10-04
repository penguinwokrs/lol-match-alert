// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

namespace MatchAlert.Devices.Logitech;

/// <summary>
/// G HUB's own LED SDK library, found where G HUB registers it: the ServerBinary of the LED SDK's CLSID,
/// normally <c>C:\Program Files\LGHUB\sdk_legacy_led_x64.dll</c>. Logitech's static SDK library looks it up
/// the same way. Nothing of Logitech's is shipped with this app: no G HUB, no library, no Logitech support.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class GHubLogiLed : ILogiLed
{
    private const string ServerBinaryKey = @"SOFTWARE\Classes\CLSID\{a6519e67-7632-4375-afdf-caa889744403}\ServerBinary";

    private readonly nint _library;
    // The SDK's functions return C++ bool, one byte: read as byte rather than trust bool marshalling.
    private readonly delegate* unmanaged[Cdecl]<byte*, byte> _initWithName;   // null on SDKs that predate it
    private readonly delegate* unmanaged[Cdecl]<byte> _init;
    private readonly delegate* unmanaged[Cdecl]<int, byte> _setTargetDevice;
    private readonly delegate* unmanaged[Cdecl]<byte> _saveCurrentLighting;
    private readonly delegate* unmanaged[Cdecl]<int, int, int, byte> _setLighting;
    private readonly delegate* unmanaged[Cdecl]<int, int, int, int, int, byte> _pulseLighting;
    private readonly delegate* unmanaged[Cdecl]<byte> _stopEffects;
    private readonly delegate* unmanaged[Cdecl]<byte> _restoreLighting;
    private readonly delegate* unmanaged[Cdecl]<void> _shutdown;

    private GHubLogiLed(nint library)
    {
        _library = library;
        _init = (delegate* unmanaged[Cdecl]<byte>)Export("LogiLedInit");
        _initWithName = NativeLibrary.TryGetExport(library, "LogiLedInitWithName", out var named)
            ? (delegate* unmanaged[Cdecl]<byte*, byte>)named
            : null;
        _setTargetDevice = (delegate* unmanaged[Cdecl]<int, byte>)Export("LogiLedSetTargetDevice");
        _saveCurrentLighting = (delegate* unmanaged[Cdecl]<byte>)Export("LogiLedSaveCurrentLighting");
        _setLighting = (delegate* unmanaged[Cdecl]<int, int, int, byte>)Export("LogiLedSetLighting");
        _pulseLighting = (delegate* unmanaged[Cdecl]<int, int, int, int, int, byte>)Export("LogiLedPulseLighting");
        _stopEffects = (delegate* unmanaged[Cdecl]<byte>)Export("LogiLedStopEffects");
        _restoreLighting = (delegate* unmanaged[Cdecl]<byte>)Export("LogiLedRestoreLighting");
        _shutdown = (delegate* unmanaged[Cdecl]<void>)Export("LogiLedShutdown");
    }

    /// <summary>Where G HUB's library is, or null when G HUB is not installed.</summary>
    public static string? FindLibrary()
    {
        using (var key = Registry.LocalMachine.OpenSubKey(ServerBinaryKey))
        {
            if (key?.GetValue(null) is string registered && File.Exists(registered)) return registered;
        }
        var usual = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LGHUB", "sdk_legacy_led_x64.dll");
        return File.Exists(usual) ? usual : null;
    }

    /// <summary>Loads G HUB's library, or returns null with the reason when it cannot.</summary>
    public static GHubLogiLed? TryLoad(out string reason)
    {
        if (FindLibrary() is not { } path)
        {
            reason = "G HUB is not installed";
            return null;
        }
        if (!NativeLibrary.TryLoad(path, out var library))
        {
            reason = $"could not load {path}";
            return null;
        }
        try
        {
            reason = path;
            return new GHubLogiLed(library);
        }
        catch (EntryPointNotFoundException e)
        {
            NativeLibrary.Free(library);
            reason = $"{path} is not the LED SDK G HUB normally installs ({e.Message})";
            return null;
        }
    }

    /// <summary>With a name G HUB can show, where the SDK supports one (it takes a narrow char*).</summary>
    public bool Init(string appName)
    {
        if (_initWithName == null) return _init() != 0;
        var name = Encoding.ASCII.GetBytes(appName + "\0");
        fixed (byte* p = name) return _initWithName(p) != 0;
    }

    public bool SetTargetDevice(int deviceTypes) => _setTargetDevice(deviceTypes) != 0;
    public bool SaveCurrentLighting() => _saveCurrentLighting() != 0;
    public bool SetLighting(int r, int g, int b) => _setLighting(r, g, b) != 0;
    public bool PulseLighting(int r, int g, int b, int durationMs, int intervalMs) => _pulseLighting(r, g, b, durationMs, intervalMs) != 0;
    public bool StopEffects() => _stopEffects() != 0;
    public bool RestoreLighting() => _restoreLighting() != 0;
    public void Shutdown() => _shutdown();

    public void Dispose() => NativeLibrary.Free(_library);

    private nint Export(string name) =>
        NativeLibrary.TryGetExport(_library, name, out var address) ? address : throw new EntryPointNotFoundException(name);
}
