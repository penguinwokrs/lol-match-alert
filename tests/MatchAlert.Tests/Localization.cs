// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MatchAlert.Tests;

internal static class EnglishForTests
{
    /// <summary>Tests assert English text; a Japanese dev machine must not change what they see.</summary>
    [ModuleInitializer]
    internal static void Pin()
    {
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }
}

public partial class TranslationTests
{
    public static TheoryData<string> Projects => new() { "MatchAlert.Tray", "MatchAlert.Devices" };

    [Theory]
    [MemberData(nameof(Projects))]
    public void Every_english_string_has_a_japanese_one_with_the_same_placeholders(string project)
    {
        var english = Read(project, "Strings.resx");
        var japanese = Read(project, "Strings.ja.resx");

        Assert.Equal(english.Keys.Order(), japanese.Keys.Order());
        foreach (var (key, text) in english)
        {
            Assert.True(Placeholders(text).SetEquals(Placeholders(japanese[key])), $"{project} {key}: placeholders differ");
            Assert.False(string.IsNullOrWhiteSpace(japanese[key]), $"{project} {key}: empty");
        }
    }

    [Fact]
    public void Japanese_windows_gets_the_japanese_wizard()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
            Assert.Equal("保存", MatchAlert.Devices.Resources.Strings.Choice_Save);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("Save", MatchAlert.Devices.Resources.Strings.Choice_Save);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Both_settings_templates_are_valid_settings()
    {
        foreach (var file in new[] { "Strings.resx", "Strings.ja.resx" })
        {
            var template = Read("MatchAlert.Tray", file)["SettingsTemplate"];
            var settings = MatchAlert.App.SettingsLoader.Load(MatchAlert.App.SettingsSources.BuiltIn() with
            {
                UserSettings = new MatchAlert.App.SourceText(file, template),
            });
            Assert.Equal("auto", settings.Language);
        }
    }

    private static Dictionary<string, string> Read(string project, string file)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "MatchAlert.sln"))) dir = Path.GetDirectoryName(dir)!;
        return XDocument.Load(Path.Combine(dir, "src", project, "Resources", file)).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string)d.Element("value")!);
    }

    private static HashSet<string> Placeholders(string text) => [.. PlaceholderPattern().Matches(text).Select(m => m.Value)];

    [GeneratedRegex(@"\{\d+(:[^}]*)?\}")]
    private static partial Regex PlaceholderPattern();
}
