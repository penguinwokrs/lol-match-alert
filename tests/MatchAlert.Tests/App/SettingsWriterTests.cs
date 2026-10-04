// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text.Json.Nodes;
using MatchAlert.App;
using MatchAlert.Domain;

namespace MatchAlert.Tests.App;

public class SettingsWriterTests : IDisposable
{
    private const string Q1 = "keychron-q1-he-8k";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lma-writer-" + Guid.NewGuid().ToString("N"));
    private string File_ => Path.Combine(_dir, "settings.json");

    public SettingsWriterTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly Pattern Blue = new([
        new Step(Rgb.Parse("#0080FF"), 100, "solid", null, 250),
        new Step(Rgb.Parse("#000000"), 100, "solid", null, 250),
    ], 3);

    private static SettingsEdit Edit(IReadOnlyDictionary<string, Pattern>? patterns = null, string? @default = null,
        IReadOnlyDictionary<string, DeviceChoice>? devices = null) =>
        new(patterns ?? new Dictionary<string, Pattern>(), @default, devices ?? new Dictionary<string, DeviceChoice>());

    private ResolvedSettings Reload() => SettingsLoader.Load(SettingsSources.FromDisk(_dir));

    [Fact]
    public void Writes_a_user_pattern_and_chooses_it()
    {
        SettingsWriter.Save(_dir, Edit(new Dictionary<string, Pattern> { ["blue-blink"] = Blue }, "blue-blink"));

        var s = Reload();
        var q1 = s.Profiles.Single(p => p.Id == Q1);
        Assert.Equal("blue-blink", s.PatternNameFor(q1));
        Assert.Equal(Blue.Steps, s.Patterns["blue-blink"].Steps);
        Assert.Equal(3, s.Patterns["blue-blink"].RepeatCount);
        Assert.Contains("blue-blink", s.UserPatterns);
    }

    [Fact]
    public void Keys_it_does_not_own_survive()
    {
        File.WriteAllText(File_, """
            {
              // a comment that will not survive
              "maxAlertSeconds": 12,
              "language": "ja",
              "devices": { "openrgb": { "enabled": true }, "keychron-q1-he-8k": { "pattern": "pulse", "note": 1 } },
            }
            """);
        Assert.Throws<SettingsException>(() => Reload());   // "note" is not a field: the writer must not care

        File.WriteAllText(File_, """{ "maxAlertSeconds": 12, "language": "ja", "devices": { "openrgb": { "enabled": true }, "keychron-q1-he-8k": { "pattern": "pulse" } } }""");
        SettingsWriter.Save(_dir, Edit(devices: new Dictionary<string, DeviceChoice> { [Q1] = new("steady", null) }));

        var s = Reload();
        Assert.Equal((12, "ja"), ((int)s.MaxAlert.TotalSeconds, s.Language));
        Assert.True(s.IsEnabled("openrgb"));
        Assert.Equal("steady", s.PatternNameFor(s.Profiles.Single(p => p.Id == Q1)));
    }

    [Fact]
    public void Editing_a_built_in_saves_an_override_and_leaving_it_out_resets_it()
    {
        var mine = new Pattern([new Step(Rgb.Parse("#00FF00"), 100, "solid", null, 0)], null);
        SettingsWriter.Save(_dir, Edit(new Dictionary<string, Pattern> { ["match-found"] = mine }));
        Assert.Equal(Rgb.Parse("#00FF00"), Reload().Patterns["match-found"].Steps[0].Color);

        SettingsWriter.Save(_dir, Edit());
        var s = Reload();
        Assert.Equal(s.BuiltInPatterns["match-found"].Steps, s.Patterns["match-found"].Steps);
        Assert.DoesNotContain("match-found", s.UserPatterns);
    }

    [Fact]
    public void Deleting_a_pattern_clears_whatever_used_it()
    {
        SettingsWriter.Save(_dir, Edit(new Dictionary<string, Pattern> { ["blue-blink"] = Blue }, "blue-blink",
            new Dictionary<string, DeviceChoice> { [Q1] = new("blue-blink", null), ["razer-chroma"] = new("blue-blink", null) }));

        SettingsWriter.Save(_dir, Edit(devices: new Dictionary<string, DeviceChoice> { [Q1] = new("blue-blink", null) }));

        var s = Reload();
        Assert.Equal("match-found", s.PatternNameFor(s.Profiles.Single(p => p.Id == Q1)));
        Assert.Equal("match-found", s.PatternNameFor(s.Profiles.Single(p => p.Id == "razer-chroma")));
    }

    [Fact]
    public void Switches_devices_off_and_back_to_their_default()
    {
        SettingsWriter.Save(_dir, Edit(devices: new Dictionary<string, DeviceChoice> { [Q1] = new(null, false), ["openrgb"] = new(null, true) }));
        var s = Reload();
        Assert.False(s.IsEnabled(Q1));
        Assert.True(s.IsEnabled("openrgb"));

        SettingsWriter.Save(_dir, Edit(devices: new Dictionary<string, DeviceChoice> { [Q1] = new(null, null) }));
        Assert.True(Reload().IsEnabled(Q1));
        Assert.DoesNotContain("keychron", File.ReadAllText(File_));   // an empty device entry is removed
    }

    [Fact]
    public void Refuses_a_result_that_would_not_load_and_writes_nothing()
    {
        File.WriteAllText(File_, """{ "maxAlertSeconds": 12 }""");
        var rainbow = new Pattern([new Step(Rgb.Parse("#FF0000"), 100, "rainbow", null, 0)], null);

        Assert.Throws<SettingsException>(() => SettingsWriter.Save(_dir, Edit(new Dictionary<string, Pattern> { ["r"] = rainbow }, "r")));
        Assert.Equal("""{ "maxAlertSeconds": 12 }""", File.ReadAllText(File_));
    }

    [Fact]
    public void Keeps_the_previous_file_as_bak()
    {
        File.WriteAllText(File_, """{ /* hand-written */ "maxAlertSeconds": 12 }""");
        SettingsWriter.Save(_dir, Edit(@default: "steady"));
        Assert.Equal("""{ /* hand-written */ "maxAlertSeconds": 12 }""", File.ReadAllText(File_ + ".bak"));
        Assert.NotNull(JsonNode.Parse(File.ReadAllText(File_)));
    }
}

public class SettingsAsWrittenTests
{
    [Fact]
    public void Reports_the_default_and_device_choices_as_written()
    {
        var s = SettingsLoader.Load(SettingsSources.BuiltIn() with
        {
            UserSettings = new SourceText("settings.json", """{ "pattern": "pulse", "devices": { "keychron-q1-he-8k": { "enabled": false } } }"""),
        });
        Assert.Equal("pulse", s.UserDefaultPattern);
        Assert.Equal(new DeviceChoice(null, false), s.UserDeviceChoices["keychron-q1-he-8k"]);
        Assert.Null(SettingsLoader.Load(SettingsSources.BuiltIn()).UserDefaultPattern);
    }
}
