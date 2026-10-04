// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text.Json;

namespace MatchAlert.App;

/// <summary>Which USB device a profile is for. Empty <see cref="ProductIds"/> means any product of the vendor.</summary>
public sealed record DeviceMatch(ushort VendorId, IReadOnlyList<ushort> ProductIds, string? ProductString);

/// <summary>
/// Everything needed to drive one keyboard model: which device it is, which driver speaks to it,
/// what its effect numbers mean, and driver-specific options the core never reads.
/// </summary>
public sealed class DeviceProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Driver { get; init; }
    public required DeviceMatch Match { get; init; }

    /// <summary>Logical effect name to the number this device's firmware uses. Always has "solid".</summary>
    public required IReadOnlyDictionary<string, int> Effects { get; init; }

    public int MinStepMs { get; init; } = SettingsLoader.DefaultMinStepMs;

    /// <summary>False for integrations that must be asked for, such as OpenRGB: <c>devices.&lt;id&gt;.enabled</c> turns them on.</summary>
    public bool EnabledByDefault { get; init; } = true;
    public string? DefaultPattern { get; init; }

    /// <summary>Driver-specific blocks, such as <c>"via"</c>. Only the named driver reads them.</summary>
    public IReadOnlyDictionary<string, JsonElement> Options { get; init; } = new Dictionary<string, JsonElement>();

    /// <summary>The file this profile came from, for error messages.</summary>
    public string Source { get; init; } = "";
}
