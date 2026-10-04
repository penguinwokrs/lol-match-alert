// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MatchAlert.Domain;

namespace MatchAlert.App;

/// <summary>
/// Merges the settings layers and validates the result. Later layers win: built-in patterns,
/// built-in device profiles, user device profiles (same id replaces), then settings.json.
/// Strict on purpose: a typo that silently kept the old value would look like "my setting does
/// nothing", so every mistake is reported with its file and field.
/// </summary>
public static class SettingsLoader
{
    public const string DefaultPattern = "match-found";
    public const int DefaultMinStepMs = 100;
    public const int DefaultMaxAlertSeconds = 30;

    /// <summary>The UI languages there are translations for. "auto" follows the Windows display language.</summary>
    public static readonly IReadOnlyList<string> Languages = ["auto", "en", "ja"];

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static ResolvedSettings Load(SettingsSources sources)
    {
        // Patterns: built-in, then the user's by name.
        var patternSources = new Dictionary<string, (PatternDto Dto, string Source)>(StringComparer.Ordinal);
        var builtIn = Parse<Dictionary<string, PatternDto>>(new SourceText("built-in patterns", SettingsSources.BuiltInPatterns()));
        foreach (var (name, dto) in builtIn) patternSources[name] = (dto, "built-in patterns");

        var user = sources.UserSettings is null ? new SettingsDto() : Parse<SettingsDto>(sources.UserSettings);
        var userSource = sources.UserSettings?.Name ?? SettingsSources.SettingsFileName;
        foreach (var (name, dto) in user.Patterns ?? []) patternSources[name] = (dto, userSource);

        var patterns = patternSources.ToDictionary(p => p.Key, p => ToPattern(p.Key, p.Value.Dto, p.Value.Source), StringComparer.Ordinal);

        // Profiles: built-in, then the user's by id.
        var profiles = new Dictionary<string, DeviceProfile>(StringComparer.Ordinal);
        foreach (var text in sources.BuiltInProfiles.Concat(sources.UserProfiles))
        {
            var profile = ToProfile(Parse<ProfileDto>(text), text.Name);
            profiles[profile.Id] = profile;
        }

        // Which pattern each device plays, and whether it plays at all.
        string Require(string name, string source, string path) => patterns.ContainsKey(name)
            ? name
            : throw Error(source, path, $"there is no pattern named \"{name}\" (known: {string.Join(", ", patterns.Keys.Order())})");

        if (user.Pattern is not null) Require(user.Pattern, userSource, "pattern");
        foreach (var (id, device) in user.Devices ?? [])
            if (device.Pattern is not null) Require(device.Pattern, userSource, $"devices.{id}.pattern");

        var patternByDevice = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var profile in profiles.Values)
        {
            var device = user.Devices?.GetValueOrDefault(profile.Id);
            var (name, source, path) =
                device?.Pattern is { } d ? (d, userSource, $"devices.{profile.Id}.pattern") :
                user.Pattern is { } g ? (g, userSource, "pattern") :
                profile.DefaultPattern is { } p ? (Require(p, profile.Source, "defaultPattern"), profile.Source, "defaultPattern") :
                (DefaultPattern, profile.Source, "pattern");

            var steps = patterns[name].Steps;
            for (int i = 0; i < steps.Count; i++)
            {
                if (!profile.Effects.ContainsKey(steps[i].Effect))
                    throw Error(source, $"patterns.{name}.steps[{i}].effect",
                        $"\"{steps[i].Effect}\" is not an effect {profile.Name} has (it has: {string.Join(", ", profile.Effects.Keys.Order())}); " +
                        $"chosen by {path}");
            }
            patternByDevice[profile.Id] = name;
        }

        int maxSeconds = user.MaxAlertSeconds ?? DefaultMaxAlertSeconds;
        if (maxSeconds is < 1 or > 600) throw Error(userSource, "maxAlertSeconds", "must be 1-600 seconds");

        var language = user.Language ?? "auto";
        if (!Languages.Contains(language))
            throw Error(userSource, "language", $"must be one of {string.Join(", ", Languages.Select(l => $"\"{l}\""))}");

        var disabled = (user.Devices ?? []).Where(d => d.Value.Enabled == false).Select(d => d.Key).ToHashSet(StringComparer.Ordinal);
        return new ResolvedSettings(patterns, profiles.Values.ToList(), patternByDevice, disabled, TimeSpan.FromSeconds(maxSeconds), language);
    }

    /// <summary>Writes a profile in the same shape <see cref="Load"/> reads, for the setup wizard to save.</summary>
    public static string Serialize(DeviceProfile profile)
    {
        var match = new JsonObject
        {
            ["vendorId"] = Hex(profile.Match.VendorId),
            ["productIds"] = new JsonArray(profile.Match.ProductIds.Select(p => (JsonNode)Hex(p)).ToArray()),
        };
        if (profile.Match.ProductString is { } ps) match["productString"] = ps;

        var root = new JsonObject
        {
            ["id"] = profile.Id,
            ["name"] = profile.Name,
            ["driver"] = profile.Driver,
            ["match"] = match,
            ["effects"] = new JsonObject(profile.Effects.Select(e => KeyValuePair.Create(e.Key, (JsonNode?)e.Value))),
            ["minStepMs"] = profile.MinStepMs,
        };
        if (profile.DefaultPattern is { } dp) root["defaultPattern"] = dp;
        foreach (var (key, value) in profile.Options) root[key] = JsonNode.Parse(value.GetRawText());

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;

        static string Hex(ushort v) => $"0x{v:X4}";
    }

    private static T Parse<T>(SourceText text) where T : new()
    {
        try
        {
            return JsonSerializer.Deserialize<T>(text.Json, Json) ?? new T();
        }
        catch (JsonException e)
        {
            var path = e.Path is { Length: > 2 } p ? p[2..] : "";
            var reason = e.Message.Contains("could not be mapped", StringComparison.Ordinal)
                ? "unknown field (check the spelling)"
                : $"not valid here (line {e.LineNumber + 1})";
            throw new SettingsException($"{text.Name}: {path}: {reason}", e);
        }
    }

    private static Pattern ToPattern(string name, PatternDto dto, string source)
    {
        string at = $"patterns.{name}";
        if (dto.Steps is not { Count: > 0 } steps) throw Error(source, $"{at}.steps", "needs at least one step");

        int? repeat = dto.Repeat switch
        {
            null => null,
            { ValueKind: JsonValueKind.String } s when s.GetString() == "untilStopped" => null,
            { ValueKind: JsonValueKind.Number } n when n.TryGetInt32(out int count) && count >= 1 => count,
            _ => throw Error(source, $"{at}.repeat", "must be \"untilStopped\" or a count of 1 or more"),
        };

        var result = new List<Step>();
        for (int i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            string sp = $"{at}.steps[{i}]";
            Rgb color;
            try { color = Rgb.Parse(s.Color ?? throw Error(source, $"{sp}.color", "is required")); }
            catch (FormatException e) { throw Error(source, $"{sp}.color", e.Message); }

            int brightness = s.Brightness ?? 100;
            if (brightness is < 0 or > 100) throw Error(source, $"{sp}.brightness", "must be 0-100 (percent)");
            if (s.Speed is < 0 or > 255) throw Error(source, $"{sp}.speed", "must be 0-255");

            int duration = s.DurationMs ?? 0;
            if (steps.Count > 1 && s.DurationMs is null)
                throw Error(source, $"{sp}.durationMs", "is required when a pattern has more than one step");
            if (duration < 0) throw Error(source, $"{sp}.durationMs", "cannot be negative");

            result.Add(new Step(color, brightness, s.Effect ?? Step.Solid, (byte?)s.Speed, duration));
        }
        return new Pattern(result, repeat);
    }

    private static DeviceProfile ToProfile(ProfileDto dto, string source)
    {
        string id = string.IsNullOrWhiteSpace(dto.Id) ? throw Error(source, "id", "is required") : dto.Id;
        if (string.IsNullOrWhiteSpace(dto.Driver)) throw Error(source, "driver", "is required");
        if (dto.Match is null) throw Error(source, "match", "is required");
        if (dto.Effects is null || !dto.Effects.ContainsKey(Step.Solid))
            throw Error(source, "effects", "must at least say which number is \"solid\"");
        if (dto.MinStepMs is < 0) throw Error(source, "minStepMs", "cannot be negative");

        ushort vendor = UInt16(dto.Match.VendorId, source, "match.vendorId")
            ?? throw Error(source, "match.vendorId", "is required");
        var products = (dto.Match.ProductIds ?? []).Select((p, i) => UInt16(p, source, $"match.productIds[{i}]")!.Value).ToList();

        return new DeviceProfile
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(dto.Name) ? id : dto.Name,
            Driver = dto.Driver,
            Match = new DeviceMatch(vendor, products, dto.Match.ProductString),
            Effects = new Dictionary<string, int>(dto.Effects, StringComparer.Ordinal),
            MinStepMs = dto.MinStepMs ?? DefaultMinStepMs,
            DefaultPattern = dto.DefaultPattern,
            Options = dto.Options ?? [],
            Source = source,
        };
    }

    /// <summary>USB ids are written either way in the wild: "0x3434" or 13364.</summary>
    private static ushort? UInt16(JsonElement? value, string source, string path)
    {
        if (value is not { } v || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetUInt16(out ushort n)) return n;
        if (v.ValueKind == JsonValueKind.String && v.GetString() is { } s)
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? ushort.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n)
                    : ushort.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out n))
                return n;
        }
        throw Error(source, path, "must be a USB id such as \"0x3434\"");
    }

    private static SettingsException Error(string source, string path, string reason) => new($"{source}: {path}: {reason}");

    private sealed class SettingsDto
    {
        public string? Pattern { get; set; }
        public Dictionary<string, PatternDto>? Patterns { get; set; }
        public Dictionary<string, DeviceSettingsDto>? Devices { get; set; }
        public int? MaxAlertSeconds { get; set; }
        public string? Language { get; set; }
    }

    private sealed class DeviceSettingsDto
    {
        public string? Pattern { get; set; }
        public bool? Enabled { get; set; }
    }

    private sealed class PatternDto
    {
        public JsonElement? Repeat { get; set; }
        public List<StepDto>? Steps { get; set; }
    }

    private sealed class StepDto
    {
        public string? Color { get; set; }
        public int? Brightness { get; set; }
        public string? Effect { get; set; }
        public int? Speed { get; set; }
        public int? DurationMs { get; set; }
    }

    private sealed class ProfileDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Driver { get; set; }
        public MatchDto? Match { get; set; }
        public Dictionary<string, int>? Effects { get; set; }
        public int? MinStepMs { get; set; }
        public string? DefaultPattern { get; set; }

        /// <summary>Anything else is a driver's own block, such as "via".</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Options { get; set; }
    }

    private sealed class MatchDto
    {
        public JsonElement? VendorId { get; set; }
        public List<JsonElement?>? ProductIds { get; set; }
        public string? ProductString { get; set; }
    }
}
