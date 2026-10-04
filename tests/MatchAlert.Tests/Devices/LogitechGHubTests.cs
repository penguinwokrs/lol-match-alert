// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.App;
using MatchAlert.Devices.Logitech;
using MatchAlert.Domain;

namespace MatchAlert.Tests.Devices;

public class LogitechGHubTests
{
    /// <summary>Records the SDK calls, in order, the way G HUB would receive them.</summary>
    private sealed class FakeLogiLed(bool gHubRunning = true) : ILogiLed
    {
        public List<string> Calls { get; } = [];
        public bool Disposed { get; private set; }

        public bool Init(string appName) { Calls.Add($"init {appName}"); return gHubRunning; }
        public bool SetTargetDevice(int deviceTypes) { Calls.Add($"target {deviceTypes}"); return true; }
        public bool SaveCurrentLighting() { Calls.Add("save"); return true; }
        public bool SetLighting(int r, int g, int b) { Calls.Add($"set {r} {g} {b}"); return true; }
        public bool PulseLighting(int r, int g, int b, int d, int i) { Calls.Add($"pulse {r} {g} {b} {d} {i}"); return true; }
        public bool StopEffects() { Calls.Add("stop"); return true; }
        public bool RestoreLighting() { Calls.Add("restore"); return true; }
        public void Shutdown() => Calls.Add("shutdown");
        public void Dispose() => Disposed = true;
    }

    private static ResolvedSettings Settings(string? json = null) => SettingsLoader.Load(SettingsSources.BuiltIn() with
    {
        UserSettings = json is null ? null : new SourceText("settings.json", json),
    });

    private static readonly Step Red = new(Rgb.Parse("#FF0000"), 100, "solid", null, 300);
    private static readonly Step DimWhite = new(Rgb.Parse("#FFFFFF"), 50, "solid", null, 300);
    private static readonly Step GoldPulse = new(Rgb.Parse("#FFB000"), 100, "breathing", 200, 0);

    [Fact]
    public void Saves_shows_and_hands_the_lighting_back_to_g_hub()
    {
        var sdk = new FakeLogiLed();
        var source = new LogitechGHubSource(() => true, () => sdk, () => Settings(), _ => { });

        using (var session = Assert.Single(source.Discover()).OpenSession())
        {
            session.Show(Red);
            session.Show(DimWhite);
        }

        Assert.Equal(["init lol-match-alert", "target 7", "save", "set 100 0 0", "set 50 50 50", "stop", "restore", "shutdown"], sdk.Calls);
        Assert.True(sdk.Disposed);
    }

    [Fact]
    public void Breathing_is_left_to_g_hub_and_stopped_before_a_solid_step()
    {
        var sdk = new FakeLogiLed();
        using (var session = new LogitechGHubSource(() => true, () => sdk, () => Settings(), _ => { }).Discover()[0].OpenSession())
        {
            session.Show(GoldPulse);
            session.Show(Red);
        }

        Assert.Contains("pulse 100 69 0 0 1000", sdk.Calls);
        Assert.True(sdk.Calls.IndexOf("stop") < sdk.Calls.IndexOf("set 100 0 0"));
    }

    [Fact]
    public void G_hub_not_running_fails_the_session_without_touching_the_lighting()
    {
        var sdk = new FakeLogiLed(gHubRunning: false);
        var device = new LogitechGHubSource(() => true, () => sdk, () => Settings(), _ => { }).Discover()[0];

        Assert.Throws<IOException>(() => device.OpenSession());
        Assert.Equal(["init lol-match-alert"], sdk.Calls);
        Assert.True(sdk.Disposed);
    }

    [Fact]
    public void G_hub_not_installed_fails_the_session_cleanly()
    {
        var device = new LogitechGHubSource(() => true, () => null, () => Settings(), _ => { }).Discover()[0];
        Assert.Throws<IOException>(() => device.OpenSession());
    }

    [Fact]
    public void Without_g_hub_installed_there_is_no_device_to_list()
    {
        var source = new LogitechGHubSource(() => false, () => new FakeLogiLed(), () => Settings(), _ => { });
        Assert.Empty(source.Discover());
    }

    [Fact]
    public void Can_be_switched_off_in_settings()
    {
        var source = new LogitechGHubSource(() => true, () => new FakeLogiLed(), () => Settings("""{ "devices": { "logitech-g-hub": { "enabled": false } } }"""), _ => { });
        Assert.Empty(source.Discover());
    }

    [Fact]
    public void Plays_its_own_pattern_choice()
    {
        var settings = Settings("""{ "devices": { "logitech-g-hub": { "pattern": "pulse" } } }""");
        var profile = settings.Profiles.Single(p => p.Id == LogitechGHubSource.ProfileId);
        Assert.Equal("breathing", Assert.Single(settings.PatternFor(profile).Steps).Effect);
    }

    [Fact]
    public void Its_profile_is_never_claimed_by_a_hid_driver()
    {
        using var bed = new DeviceTestbed(new FakeHidBus().Add(FakeHidBus.Via(0x046D, 0xC33F, "G815"), new FakeViaBoard()));
        Assert.DoesNotContain(bed.Source.Recognised(), r => r.Profile.Id == LogitechGHubSource.ProfileId);
    }
}
