// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MatchAlert.Devices.Razer;

/// <summary>
/// The Chroma SDK library Razer Synapse installs in System32. Nothing of Razer's is shipped with this app.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class ChromaNative : IChromaSdk
{
    private readonly nint _library;
    private readonly delegate* unmanaged[Cdecl]<int> _init;
    private readonly delegate* unmanaged[Cdecl]<int> _unInit;
    private readonly delegate* unmanaged[Cdecl]<Guid, int> _setEffect;
    private readonly delegate* unmanaged[Cdecl]<Guid, int> _deleteEffect;
    private readonly Dictionary<ChromaCategory, nint> _create = [];

    private ChromaNative(nint library)
    {
        _library = library;
        _init = (delegate* unmanaged[Cdecl]<int>)Export("Init");
        _unInit = (delegate* unmanaged[Cdecl]<int>)Export("UnInit");
        _setEffect = (delegate* unmanaged[Cdecl]<Guid, int>)Export("SetEffect");
        _deleteEffect = (delegate* unmanaged[Cdecl]<Guid, int>)Export("DeleteEffect");
        foreach (var c in Enum.GetValues<ChromaCategory>()) _create[c] = Export($"Create{c}Effect");
    }

    /// <summary>Where Synapse put the library, or null when Synapse (with Chroma) is not installed.</summary>
    public static string? FindLibrary()
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        return new[] { "RzChromaSDK64.dll", "RzChromaSDK.dll" }.Select(n => Path.Combine(system, n)).FirstOrDefault(File.Exists);
    }

    public static ChromaNative? TryLoad(out string reason)
    {
        if (FindLibrary() is not { } path)
        {
            reason = "Razer Synapse's Chroma SDK is not installed";
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
            return new ChromaNative(library);
        }
        catch (EntryPointNotFoundException e)
        {
            NativeLibrary.Free(library);
            reason = $"{path} is not the Chroma SDK ({e.Message})";
            return null;
        }
    }

    public int Init() => _init();
    public int UnInit() => _unInit();
    public int SetEffect(Guid effectId) => _setEffect(effectId);
    public int DeleteEffect(Guid effectId) => _deleteEffect(effectId);

    public int CreateEffect(ChromaCategory category, int effectType, ReadOnlySpan<byte> param, out Guid effectId)
    {
        Guid id = default;
        int result;
        fixed (byte* p = param)
            result = ((delegate* unmanaged[Cdecl]<int, byte*, Guid*, int>)_create[category])(effectType, p, &id);
        effectId = id;
        return result;
    }

    public void Dispose() => NativeLibrary.Free(_library);

    private nint Export(string name) =>
        NativeLibrary.TryGetExport(_library, name, out var address) ? address : throw new EntryPointNotFoundException(name);
}
