// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Reflection;
using MatchAlert.Domain;

namespace MatchAlert.App;

/// <summary>A JSON document and the name to blame it under.</summary>
public sealed record SourceText(string Name, string Json);

/// <summary>Everything settings are built from, in layer order.</summary>
public sealed record SettingsSources(
    IReadOnlyList<SourceText> BuiltInProfiles,
    SourceText? UserSettings,
    IReadOnlyList<SourceText> UserProfiles)
{
    public const string SettingsFileName = "settings.json";
    public const string DevicesDirectoryName = "devices";

    /// <summary>The built-in layers only: what a fresh install runs with.</summary>
    public static SettingsSources BuiltIn() =>
        new(ReadEmbedded("builtin/devices/").ToList(), null, []);

    /// <summary>The built-in layers plus whatever the user has in <paramref name="settingsDirectory"/>.</summary>
    public static SettingsSources FromDisk(string settingsDirectory)
    {
        var settingsPath = Path.Combine(settingsDirectory, SettingsFileName);
        var devices = Path.Combine(settingsDirectory, DevicesDirectoryName);
        return BuiltIn() with
        {
            UserSettings = File.Exists(settingsPath) ? new SourceText(settingsPath, File.ReadAllText(settingsPath)) : null,
            UserProfiles = Directory.Exists(devices)
                ? Directory.GetFiles(devices, "*.json").Order(StringComparer.Ordinal)
                    .Select(f => new SourceText(f, File.ReadAllText(f))).ToList()
                : [],
        };
    }

    internal static string BuiltInPatterns() => ReadEmbedded("builtin/patterns.json").Single().Json;

    private static IEnumerable<SourceText> ReadEmbedded(string prefix)
    {
        var assembly = typeof(SettingsSources).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
            yield return new SourceText($"built-in {name["builtin/".Length..]}", reader.ReadToEnd());
        }
    }
}

/// <summary>Validated, merged settings. Every pattern a device can be asked to play is known to work on it.</summary>
public sealed class ResolvedSettings
{
    private readonly IReadOnlyDictionary<string, string> _patternByDevice;
    private readonly IReadOnlySet<string> _disabled;

    internal ResolvedSettings(IReadOnlyDictionary<string, Pattern> patterns, IReadOnlyList<DeviceProfile> profiles,
        IReadOnlyDictionary<string, string> patternByDevice, IReadOnlySet<string> disabled, TimeSpan maxAlert)
    {
        Patterns = patterns;
        Profiles = profiles;
        _patternByDevice = patternByDevice;
        _disabled = disabled;
        MaxAlert = maxAlert;
    }

    public IReadOnlyDictionary<string, Pattern> Patterns { get; }
    public IReadOnlyList<DeviceProfile> Profiles { get; }

    /// <summary>The safety stop: an alert never runs longer than this.</summary>
    public TimeSpan MaxAlert { get; }

    public string PatternNameFor(DeviceProfile profile) => _patternByDevice[profile.Id];
    public Pattern PatternFor(DeviceProfile profile) => Patterns[PatternNameFor(profile)];
    public bool IsEnabled(string deviceId) => !_disabled.Contains(deviceId);
}
