// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.App;
using MatchAlert.Domain;

namespace MatchAlert.Tests.App;

public class SettingsLoaderTests
{
    private const string Q1 = "keychron-q1-he-8k";

    private static ResolvedSettings Load(string? settings = null, params string[] profiles) =>
        SettingsLoader.Load(SettingsSources.BuiltIn() with
        {
            UserSettings = settings is null ? null : new SourceText("settings.json", settings),
            UserProfiles = profiles.Select((p, i) => new SourceText($"devices/{i}.json", p)).ToList(),
        });

    private static DeviceProfile Profile(ResolvedSettings s, string id) => s.Profiles.Single(p => p.Id == id);

    [Fact]
    public void Defaults_give_the_q1_he_a_red_white_blink()
    {
        var s = Load();
        var pattern = s.PatternFor(Profile(s, Q1));

        Assert.Null(pattern.RepeatCount);
        Assert.Equal([Rgb.Parse("#FF0000"), Rgb.Parse("#FFFFFF")], pattern.Steps.Select(x => x.Color));
        Assert.All(pattern.Steps, x => Assert.Equal((300, 100, "solid"), (x.DurationMs, x.Brightness, x.Effect)));
        Assert.Equal(TimeSpan.FromSeconds(30), s.MaxAlert);
        Assert.True(s.IsEnabled(Q1));
    }

    [Fact]
    public void The_built_in_q1_he_profile_matches_every_layout()
    {
        var p = Profile(Load(), Q1);

        Assert.Equal("via", p.Driver);
        Assert.Equal(0x3434, p.Match.VendorId);
        Assert.Equal([0x1010, 0x1011, 0x1012], p.Match.ProductIds.Select(x => (int)x));
        Assert.Equal(1, p.Effects["solid"]);
        Assert.Equal(2, p.Effects["breathing"]);
        Assert.True(p.Options.ContainsKey("via"));
    }

    [Fact]
    public void The_pattern_setting_selects_a_built_in()
    {
        var s = Load("""{ "pattern": "pulse" }""");
        var step = Assert.Single(s.PatternFor(Profile(s, Q1)).Steps);
        Assert.Equal("breathing", step.Effect);
    }

    [Fact]
    public void A_user_pattern_replaces_the_built_in_of_the_same_name()
    {
        var s = Load("""{ "patterns": { "match-found": { "steps": [ { "color": "#00FF00" } ] } } }""");
        var step = Assert.Single(s.PatternFor(Profile(s, Q1)).Steps);
        Assert.Equal(Rgb.Parse("#00FF00"), step.Color);
    }

    [Fact]
    public void The_device_setting_beats_the_global_one()
    {
        var s = Load($$"""{ "pattern": "pulse", "devices": { "{{Q1}}": { "pattern": "steady" } } }""");
        var step = Assert.Single(s.PatternFor(Profile(s, Q1)).Steps);
        Assert.Equal("solid", step.Effect);
    }

    [Fact]
    public void Repeat_takes_a_count()
    {
        var s = Load("""{ "pattern": "x", "patterns": { "x": { "repeat": 3, "steps": [ { "color": "#FF0000" } ] } } }""");
        Assert.Equal(3, s.PatternFor(Profile(s, Q1)).RepeatCount);
    }

    [Theory]
    [InlineData(null, "auto")]
    [InlineData("""{ "language": "ja" }""", "ja")]
    [InlineData("""{ "language": "en" }""", "en")]
    public void Language_defaults_to_following_windows(string? json, string expected)
    {
        Assert.Equal(expected, Load(json).Language);
    }

    [Fact]
    public void An_opt_in_profile_stays_off_until_switched_on()
    {
        const string optIn = """
            { "id": "opt", "driver": "x", "match": { "vendorId": "0x0000" }, "effects": { "solid": 0 }, "enabledByDefault": false }
            """;
        Assert.False(Load(null, optIn).IsEnabled("opt"));
        Assert.True(Load("""{ "devices": { "opt": { "enabled": true } } }""", optIn).IsEnabled("opt"));
    }

    [Fact]
    public void Devices_can_be_switched_off()
    {
        Assert.False(Load($$"""{ "devices": { "{{Q1}}": { "enabled": false } } }""").IsEnabled(Q1));
    }

    [Fact]
    public void A_user_profile_replaces_the_built_in_one_with_the_same_id()
    {
        var s = Load(null, $$"""
            { "id": "{{Q1}}", "name": "Mine", "driver": "via",
              "match": { "vendorId": "0x3434", "productIds": [ "0x1012" ] },
              "effects": { "solid": 7, "breathing": 8 }, "via": { "channel": 3 } }
            """);
        var p = Profile(s, Q1);
        Assert.Equal(("Mine", 7), (p.Name, p.Effects["solid"]));
        Assert.Single(s.Profiles, x => x.Id == Q1);
    }

    [Fact]
    public void Hand_edited_json_may_have_comments_and_trailing_commas()
    {
        var s = Load("""
            {
              // what plays everywhere
              "pattern": "steady",
            }
            """);
        Assert.Equal("solid", Assert.Single(s.PatternFor(Profile(s, Q1)).Steps).Effect);
    }

    [Fact]
    public void A_saved_profile_loads_back_the_same()
    {
        var original = Profile(Load(), Q1);
        var reloaded = Profile(Load(null, SettingsLoader.Serialize(original)), Q1);

        Assert.Equal(original.Match.ProductIds, reloaded.Match.ProductIds);
        Assert.Equal(original.Effects, reloaded.Effects);
        Assert.True(System.Text.Json.JsonElement.DeepEquals(original.Options["via"], reloaded.Options["via"]));
        Assert.Equal((original.Name, original.MinStepMs), (reloaded.Name, reloaded.MinStepMs));
    }

    [Theory]
    [InlineData("""{ "pattern": "nope" }""", "pattern")]
    [InlineData("""{ "patterns": { "p": { "steps": [ { "color": "#FF0000", "effect": "rainbow" } ] } }, "pattern": "p" }""", "patterns.p.steps[0].effect")]
    [InlineData("""{ "patterns": { "p": { "steps": [ { "color": "#FF0000", "brightness": 150 } ] } } }""", "patterns.p.steps[0].brightness")]
    [InlineData("""{ "patterns": { "p": { "steps": [ { "color": "#FF0000" }, { "color": "#FFFFFF" } ] } } }""", "patterns.p.steps[0].durationMs")]
    [InlineData("""{ "patterns": { "p": { "steps": [ { "color": "red" } ] } } }""", "patterns.p.steps[0].color")]
    [InlineData("""{ "patterns": { "p": { "steps": [] } } }""", "patterns.p.steps")]
    [InlineData("""{ "patterns": { "p": { "repeat": 0, "steps": [ { "color": "#FF0000" } ] } } }""", "patterns.p.repeat")]
    [InlineData("""{ "patterns": { "p": { "repeat": "forever", "steps": [ { "color": "#FF0000" } ] } } }""", "patterns.p.repeat")]
    [InlineData("""{ "devices": { "keychron-q1-he-8k": { "pattern": "nope" } } }""", "devices.keychron-q1-he-8k.pattern")]
    [InlineData("""{ "maxAlertSeconds": 0 }""", "maxAlertSeconds")]
    [InlineData("""{ "language": "fr" }""", "language")]
    [InlineData("""{ "patern": "steady" }""", "patern")]
    public void Mistakes_name_the_file_and_the_field(string json, string path)
    {
        var e = Assert.Throws<SettingsException>(() => Load(json));
        Assert.StartsWith("settings.json", e.Message);
        Assert.Contains(path, e.Message);
    }

    [Fact]
    public void An_effect_the_device_lacks_is_reported_against_the_device()
    {
        var e = Assert.Throws<SettingsException>(() => Load(null, """
            { "id": "tiny", "name": "Tiny", "driver": "via",
              "match": { "vendorId": "0x1234", "productIds": [ "0x0001" ] },
              "effects": { "solid": 1 }, "defaultPattern": "pulse" }
            """));
        Assert.Contains("breathing", e.Message);
        Assert.Contains("Tiny", e.Message);
    }

    [Fact]
    public void A_profile_without_solid_is_rejected()
    {
        var e = Assert.Throws<SettingsException>(() => Load(null, """
            { "id": "x", "driver": "via", "match": { "vendorId": "0x1234" }, "effects": { "breathing": 2 } }
            """));
        Assert.StartsWith("devices/0.json", e.Message);
        Assert.Contains("solid", e.Message);
    }
}
