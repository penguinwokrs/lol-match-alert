// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text.Json;
using System.Text.Json.Nodes;
using MatchAlert.Domain;

namespace MatchAlert.App;

/// <summary>What a device should play: a pattern name, or null for the default; enabled, or null for its profile's default.</summary>
public sealed record DeviceChoice(string? Pattern, bool? Enabled);

/// <summary>
/// Everything the editor owns in settings.json. <see cref="UserPatterns"/> is the complete set of patterns the
/// user defines - a built-in left out goes back to how it ships. <see cref="Devices"/> lists only the
/// devices the editor shows; others are left as they are.
/// </summary>
public sealed record SettingsEdit(
    IReadOnlyDictionary<string, Pattern> UserPatterns,
    string? DefaultPattern,
    IReadOnlyDictionary<string, DeviceChoice> Devices);

/// <summary>
/// Writes the editor's changes into settings.json. Only <c>pattern</c>, <c>patterns</c> and
/// <c>devices.&lt;id&gt;.pattern</c> / <c>.enabled</c> are touched; every other key survives. Comments do
/// not: the file is rewritten, and the previous one is kept as <c>settings.json.bak</c>.
/// </summary>
public static class SettingsWriter
{
    private static readonly JsonDocumentOptions Read = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private static readonly JsonSerializerOptions Write = new() { WriteIndented = true };

    /// <summary>
    /// Applies <paramref name="edit"/>, checks the result loads with the profiles on disk, and only then
    /// writes it. Throws <see cref="SettingsException"/> and writes nothing if it would not load.
    /// </summary>
    public static ResolvedSettings Save(string settingsDirectory, SettingsEdit edit)
    {
        var path = Path.Combine(settingsDirectory, SettingsSources.SettingsFileName);
        string? current = File.Exists(path) ? File.ReadAllText(path) : null;
        string updated = Apply(current, edit);

        var resolved = SettingsLoader.Load(SettingsSources.FromDisk(settingsDirectory) with { UserSettings = new SourceText(path, updated) });

        Directory.CreateDirectory(settingsDirectory);
        if (current is not null) File.Copy(path, path + ".bak", overwrite: true);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, updated);
        File.Move(tmp, path, overwrite: true);
        return resolved;
    }

    public static string Apply(string? currentJson, SettingsEdit edit)
    {
        var root = string.IsNullOrWhiteSpace(currentJson)
            ? new JsonObject()
            : JsonNode.Parse(currentJson, documentOptions: Read) as JsonObject ?? new JsonObject();

        // Patterns: exactly the user's, built-ins left out go back to how they ship.
        if (edit.UserPatterns.Count == 0) root.Remove("patterns");
        else root["patterns"] = new JsonObject(edit.UserPatterns.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)PatternNode(p.Value))));

        var builtIn = SettingsLoader.Load(SettingsSources.BuiltIn()).BuiltInPatterns.Keys;
        bool Known(string? name) => name is not null && (edit.UserPatterns.ContainsKey(name) || builtIn.Contains(name));

        if (Known(edit.DefaultPattern)) root["pattern"] = edit.DefaultPattern;
        else root.Remove("pattern");

        var devices = root["devices"] as JsonObject ?? new JsonObject();
        foreach (var (id, choice) in edit.Devices)
        {
            var device = devices[id] as JsonObject ?? new JsonObject();
            if (Known(choice.Pattern)) device["pattern"] = choice.Pattern; else device.Remove("pattern");
            if (choice.Enabled is bool on) device["enabled"] = on; else device.Remove("enabled");
            devices[id] = device;
        }
        // A deleted pattern must not leave a device pointing at it, shown in the editor or not.
        foreach (var (id, node) in devices.ToList())
        {
            if (node is not JsonObject device) continue;
            if (device["pattern"] is JsonValue v && v.TryGetValue(out string? name) && !Known(name)) device.Remove("pattern");
            if (device.Count == 0) devices.Remove(id);
        }
        if (devices.Count == 0) root.Remove("devices"); else root["devices"] = devices;

        return root.ToJsonString(Write) + Environment.NewLine;
    }

    /// <summary>A pattern in the shape settings.json takes, leaving out what is already the default.</summary>
    private static JsonObject PatternNode(Pattern pattern)
    {
        var steps = new JsonArray();
        foreach (var s in pattern.Steps)
        {
            var step = new JsonObject { ["color"] = s.Color.ToString() };
            if (s.Brightness != 100) step["brightness"] = s.Brightness;
            if (s.Effect != Step.Solid) step["effect"] = s.Effect;
            if (s.Speed is { } speed) step["speed"] = speed;
            if (pattern.Steps.Count > 1) step["durationMs"] = s.DurationMs;
            steps.Add(step);
        }
        var node = new JsonObject();
        if (pattern.RepeatCount is int count) node["repeat"] = count;
        node["steps"] = steps;
        return node;
    }
}
