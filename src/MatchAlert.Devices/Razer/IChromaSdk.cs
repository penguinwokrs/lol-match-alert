// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

namespace MatchAlert.Devices.Razer;

/// <summary>The device categories of Razer's Chroma SDK, each with its own Create*Effect function.</summary>
public enum ChromaCategory
{
    Keyboard,
    Mouse,
    Headset,
    Mousepad,
    Keypad,
    ChromaLink,
}

/// <summary>
/// The slice of Razer's Chroma SDK (RzChromaSDK.h) this app uses. Results are RZRESULT: 0 is success.
/// Synapse owns the devices and arbitrates; the app never touches a Razer device directly.
/// </summary>
public interface IChromaSdk : IDisposable
{
    int Init();

    /// <summary>Releases control; Synapse goes back to the user's own lighting.</summary>
    int UnInit();

    /// <summary>Create&lt;Category&gt;Effect: <paramref name="param"/> is that category's effect struct, as bytes.</summary>
    int CreateEffect(ChromaCategory category, int effectType, ReadOnlySpan<byte> param, out Guid effectId);

    int SetEffect(Guid effectId);
    int DeleteEffect(Guid effectId);
}
