// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.App;
using MatchAlert.Devices.Razer;
using MatchAlert.Domain;

namespace MatchAlert.Tests.Devices;

public class RazerChromaTests
{
    /// <summary>Records SDK calls as Synapse would receive them. Categories in <c>absent</c> have no device.</summary>
    private sealed class FakeChroma(int initResult = 0, params ChromaCategory[] absent) : IChromaSdk
    {
        public List<string> Calls { get; } = [];
        public HashSet<Guid> Live { get; } = [];
        public bool Disposed { get; private set; }

        public int Init() { Calls.Add("init"); return initResult; }
        public int UnInit() { Calls.Add("uninit"); return 0; }

        public int CreateEffect(ChromaCategory category, int effectType, ReadOnlySpan<byte> param, out Guid effectId)
        {
            Calls.Add($"create {category} {effectType} {Convert.ToHexString(param)}");
            effectId = Guid.NewGuid();
            if (absent.Contains(category)) return 1167;   // RZRESULT_DEVICE_NOT_CONNECTED
            Live.Add(effectId);
            return 0;
        }

        public int SetEffect(Guid effectId) => Live.Contains(effectId) ? 0 : 1168;
        public int DeleteEffect(Guid effectId) { Live.Remove(effectId); return 0; }
        public void Dispose() => Disposed = true;
    }

    private static ResolvedSettings Settings(string? json = null) => SettingsLoader.Load(SettingsSources.BuiltIn() with
    {
        UserSettings = json is null ? null : new SourceText("settings.json", json),
    });

    private static RazerChromaSource Source(FakeChroma sdk, bool installed = true, string? settings = null) =>
        new(() => installed, () => sdk, () => Settings(settings), _ => { });

    private static readonly Step Red = new(Rgb.Parse("#FF0000"), 100, "solid", null, 300);

    [Fact]
    public void Shows_a_static_color_on_every_category_with_razers_own_effect_numbers()
    {
        var sdk = new FakeChroma();
        using (var s = Source(sdk).Discover().Single().OpenSession()) s.Show(Red);

        // COLORREF 0x000000FF, little-endian; the mouse names RZLED_ALL (0xFFFF) first.
        Assert.Equal(
        [
            "init",
            "create Keyboard 4 FF000000",
            "create Mouse 6 FFFF0000FF000000",
            "create Headset 1 FF000000",
            "create Mousepad 4 FF000000",
            "create Keypad 5 FF000000",
            "create ChromaLink 2 FF000000",
            "uninit",
        ], sdk.Calls);
        Assert.True(sdk.Disposed);
    }

    [Fact]
    public void Each_step_replaces_the_last_and_nothing_is_left_behind()
    {
        var sdk = new FakeChroma();
        using (var s = Source(sdk).Discover().Single().OpenSession())
        {
            s.Show(Red);
            s.Show(Red with { Color = Rgb.Parse("#FFFFFF"), Brightness = 50 });
            Assert.Equal(6, sdk.Live.Count);   // only the second step's effects remain
            Assert.Contains("create Keyboard 4 80808000", sdk.Calls);
        }
        Assert.Empty(sdk.Live);
    }

    [Fact]
    public void Categories_with_no_device_are_skipped_quietly()
    {
        var log = new List<string>();
        var sdk = new FakeChroma(0, ChromaCategory.Headset, ChromaCategory.Keypad, ChromaCategory.ChromaLink, ChromaCategory.Mousepad);
        using (var s = new RazerChromaSource(() => true, () => sdk, () => Settings(), log.Add).Discover().Single().OpenSession()) s.Show(Red);
        Assert.Empty(log);
    }

    [Fact]
    public void Synapse_not_running_fails_the_session_cleanly()
    {
        var sdk = new FakeChroma(initResult: 1062);   // RZRESULT_SERVICE_NOT_ACTIVE
        var e = Assert.Throws<IOException>(() => Source(sdk).Discover().Single().OpenSession());
        Assert.Contains("1062", e.Message);
        Assert.True(sdk.Disposed);
        Assert.Equal(["init"], sdk.Calls);
    }

    [Fact]
    public void Without_synapse_installed_there_is_nothing_to_list()
    {
        Assert.Empty(Source(new FakeChroma(), installed: false).Discover());
    }

    [Fact]
    public void Can_be_switched_off_in_settings()
    {
        Assert.Empty(Source(new FakeChroma(), settings: """{ "devices": { "razer-chroma": { "enabled": false } } }""").Discover());
    }

    [Fact]
    public void Breathing_shows_steady()
    {
        var sdk = new FakeChroma();
        using (var s = Source(sdk).Discover().Single().OpenSession()) s.Show(new Step(Rgb.Parse("#FFB000"), 100, "breathing", 200, 0));
        Assert.Contains("create Keyboard 4 FFB00000", sdk.Calls);   // #FFB000 as COLORREF 0x0000B0FF
    }
}
