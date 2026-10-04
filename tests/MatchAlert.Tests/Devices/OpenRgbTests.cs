// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.App;
using MatchAlert.Devices.OpenRgb;
using MatchAlert.Domain;

namespace MatchAlert.Tests.Devices;

public class OpenRgbTests
{
    private static readonly Step Red = new(Rgb.Parse("#FF0000"), 100, "solid", null, 300);
    private static readonly Step HalfWhite = new(Rgb.Parse("#FFFFFF"), 50, "solid", null, 300);

    private static ResolvedSettings Settings(int port, bool enabled = true, string types = """["keyboard"]""") =>
        SettingsLoader.Load(SettingsSources.BuiltIn() with
        {
            UserSettings = new SourceText("settings.json", $$"""{ "devices": { "openrgb": { "enabled": {{(enabled ? "true" : "false")}} } } }"""),
            UserProfiles = [new SourceText("openrgb.json", $$"""
                { "id": "openrgb", "name": "OpenRGB devices", "driver": "openrgb", "match": { "vendorId": "0x0000" },
                  "effects": { "solid": 0, "breathing": 1 }, "enabledByDefault": false,
                  "openrgb": { "host": "127.0.0.1", "port": {{port}}, "deviceTypes": {{types}} } }
                """)],
        });

    [Fact]
    public void Is_off_until_switched_on()
    {
        var builtIn = SettingsLoader.Load(SettingsSources.BuiltIn());
        Assert.False(builtIn.IsEnabled(OpenRgbSource.ProfileId));
        using var server = new FakeOpenRgbServer(4, FakeOpenRgbServer.Keyboard());
        Assert.Empty(new OpenRgbSource(() => Settings(server.Port, enabled: false), _ => { }).Discover());
    }

    [Fact]
    public void Lists_keyboards_by_default_and_any_type_when_asked()
    {
        using var server = new FakeOpenRgbServer(4, FakeOpenRgbServer.Mouse(), FakeOpenRgbServer.Keyboard());

        var keyboards = new OpenRgbSource(() => Settings(server.Port), _ => { }).Discover();
        Assert.Equal(["Fake Keyboard (OpenRGB)"], keyboards.Select(d => d.Name));

        var all = new OpenRgbSource(() => Settings(server.Port, types: """["all"]"""), _ => { }).Discover();
        Assert.Equal(2, all.Count);
        Assert.Contains("lol-match-alert", server.ClientNames);
    }

    [Theory]
    [InlineData(4u)]
    [InlineData(3u)]
    [InlineData(1u)]
    [InlineData(5u)]
    public void Lights_every_led_and_puts_mode_and_colors_back(uint serverVersion)
    {
        var keyboard = FakeOpenRgbServer.Keyboard();
        using var server = new FakeOpenRgbServer(serverVersion, keyboard);
        var before = (keyboard.ActiveMode, Colors: keyboard.Colors.ToArray(), keyboard.Modes[1].Speed, ModeColors: keyboard.Modes[1].Colors.ToArray());

        var device = new OpenRgbSource(() => Settings(server.Port), _ => { }).Discover().Single();
        using (var session = device.OpenSession())
        {
            session.Show(Red);
            WaitFor(() => keyboard.Colors.All(c => c == 0x0000FF));
            Assert.Equal(0, keyboard.ActiveMode);   // Direct
            session.Show(HalfWhite);
            WaitFor(() => keyboard.Colors.All(c => c == 0x808080));
        }

        WaitFor(() => keyboard.ActiveMode == before.ActiveMode && keyboard.Colors.SequenceEqual(before.Colors));
        Assert.Equal(before.Speed, keyboard.Modes[1].Speed);
        Assert.Equal(before.ModeColors, keyboard.Modes[1].Colors);
    }

    [Fact]
    public void Skips_packets_the_server_sends_on_its_own()
    {
        using var server = new FakeOpenRgbServer(4, FakeOpenRgbServer.Keyboard()) { AnnounceListChanges = true };
        Assert.Single(new OpenRgbSource(() => Settings(server.Port), _ => { }).Discover());
    }

    [Fact]
    public void OpenRGB_not_running_lists_nothing_and_says_so_once()
    {
        int port;
        using (var server = new FakeOpenRgbServer(4)) port = server.Port;   // closed again: nothing listens
        var log = new List<string>();
        var source = new OpenRgbSource(() => Settings(port), log.Add);

        Assert.Empty(source.Discover());
        Assert.Empty(source.Discover());
        Assert.Single(log, l => l.Contains("not reachable"));
    }

    [Fact]
    public void Never_lights_a_different_device_after_a_rescan()
    {
        var keyboard = FakeOpenRgbServer.Keyboard();
        using var server = new FakeOpenRgbServer(4, keyboard);
        var device = new OpenRgbSource(() => Settings(server.Port), _ => { }).Discover().Single();
        keyboard.Name = "Something Else";

        Assert.Throws<IOException>(() => device.OpenSession());
        Assert.Equal(0, keyboard.CustomModeCalls);
    }

    [Fact]
    public void Parses_device_data_like_the_documented_layout()
    {
        // Pinned by the independent serialiser above, at each version the client may negotiate.
        using var server = new FakeOpenRgbServer(4, FakeOpenRgbServer.Keyboard());
        using var client = OpenRgbClient.Connect("127.0.0.1", server.Port, "test", TimeSpan.FromSeconds(2));
        var c = client.Controller(0);

        Assert.Equal((4u, 5, "Fake Keyboard", "Fake Vendor", 1), (client.Protocol, c.Type, c.Name, c.Vendor, c.ActiveMode));
        Assert.Equal(["Direct", "Breathing"], c.Modes.Select(m => m.Name));
        Assert.Equal(new uint[] { 0x010101, 0x020202, 0x030303, 0x040404 }, c.Colors);
    }

    private static void WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail("server state never got there");
            Thread.Sleep(5);
        }
    }
}
