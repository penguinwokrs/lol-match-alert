// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.App;
using MatchAlert.Domain;
using static MatchAlert.Tests.Devices.DeviceTestbed;

namespace MatchAlert.Tests.Devices;

public class ViaSessionTests
{
    private static readonly Step Red = new(Rgb.Parse("#FF0000"), 100, "solid", null, 300);
    private static readonly Step White = new(Rgb.Parse("#FFFFFF"), 100, "solid", null, 300);

    private static (DeviceTestbed Bed, FakeViaBoard Board) Q1(int? maxBrightness = null)
    {
        // The user's own lighting before any alert: effect 16, as measured on the author's board.
        var board = new FakeViaBoard(resetAfterWrites: 2, maxBrightness: maxBrightness)
        {
            StoredBrightness = 200, Effect = 16, Speed = 0, Hue = 0, Sat = 255,
        };
        return (new DeviceTestbed(new FakeHidBus().Add(FakeHidBus.Via(Keychron, Q1Jis, "Keychron Q1 HE 8K"), board)), board);
    }

    [Fact]
    public void A_blink_changes_the_effect_once_then_only_colors()
    {
        var (bed, board) = Q1();
        using var _ = bed;
        using var session = Assert.Single(bed.Source.Discover()).OpenSession();

        session.Show(Red);
        session.Show(White);
        session.Show(Red);

        Assert.Equal(1, board.EffectWrites);
        Assert.Equal((1, 0, 255, 255), ((int)board.Effect, (int)board.Hue, (int)board.Sat, (int)board.Brightness));
    }

    [Fact]
    public void Ending_the_session_puts_the_lighting_back()
    {
        var (bed, board) = Q1();
        using var _ = bed;
        var before = board.State;

        using (var session = bed.Source.Discover()[0].OpenSession())
        {
            session.Show(White);
            Assert.NotEqual(before, board.State);
        }

        Assert.Equal(before, board.State);
        Assert.True(board.Disposed);
    }

    [Fact]
    public void Brightness_does_not_walk_down_over_many_alerts()
    {
        // v3 brightness is lossy: a dim backlight would lose a step on every restore without the echo memory.
        var (bed, board) = Q1(maxBrightness: 200);
        using var _ = bed;
        board.StoredBrightness = 40;

        var stored = new List<int>();
        for (int i = 0; i < 6; i++)
        {
            using (var s = bed.Source.Discover()[0].OpenSession()) s.Show(Red);
            stored.Add(board.StoredBrightness);
        }

        Assert.Single(stored.Distinct());
    }

    [Fact]
    public void A_pending_snapshot_exists_only_while_the_session_runs()
    {
        var (bed, _) = Q1();
        using var __ = bed;
        var session = bed.Source.Discover()[0].OpenSession();
        session.Show(Red);
        Assert.Single(bed.Pending.All());

        session.Dispose();
        Assert.Empty(bed.Pending.All());
    }

    [Fact]
    public void An_unplugged_keyboard_does_not_throw_from_show()
    {
        var (bed, board) = Q1();
        using var _ = bed;
        using var session = bed.Source.Discover()[0].OpenSession();
        board.Unplugged = true;

        session.Show(Red);
        session.Show(White);

        Assert.Single(bed.Log, l => l.Contains("write failed"));
    }

    [Fact]
    public void After_a_crash_the_next_start_restores_what_the_alert_left()
    {
        var (bed, board) = Q1();
        using var _ = bed;
        var before = board.State;
        var crashed = bed.Source.Discover()[0].OpenSession();
        crashed.Show(Red);   // never disposed: the process died here

        bed.Restart();
        bed.Source.RecoverInterruptedSessions();

        Assert.Equal(before, board.State);
        Assert.Empty(bed.Pending.All());
    }

    [Fact]
    public void After_a_crash_lighting_the_user_has_changed_since_is_left_alone()
    {
        var (bed, board) = Q1();
        using var _ = bed;
        bed.Source.Discover()[0].OpenSession().Show(Red);
        board.Effect = 5;   // the user replugged the board and picked another effect
        board.Hue = 33;

        bed.Restart();
        bed.Source.RecoverInterruptedSessions();

        Assert.Equal((5, 33), ((int)board.Effect, (int)board.Hue));
        Assert.Empty(bed.Pending.All());
        Assert.Contains(bed.Log, l => l.Contains("discarded"));
    }
}

public class HidDeviceSourceTests
{
    private static FakeHidBus DeskWithDock() => new FakeHidBus()
        .Add(FakeHidBus.KeyboardCollection(Keychron, Q1Jis, "Keychron Q1 HE 8K"))
        .Add(FakeHidBus.Via(Keychron, Q1Jis, "Keychron Q1 HE 8K"), new FakeViaBoard())
        .Add(FakeHidBus.Via(Keychron, LinkKm, "Keychron Link-KM"), new FakeViaBoard());

    [Fact]
    public void Finds_the_q1_he_on_its_raw_hid_collection_and_not_the_dock()
    {
        using var bed = new DeviceTestbed(DeskWithDock());
        var device = Assert.Single(bed.Source.Discover());
        Assert.Equal("keychron-q1-he-8k", device.Id);
    }

    [Fact]
    public void The_dock_is_offered_for_setup()
    {
        using var bed = new DeviceTestbed(DeskWithDock());
        var unknown = Assert.Single(bed.Source.Unrecognised());
        Assert.Equal(LinkKm, unknown.ProductId);
        Assert.NotNull(bed.Source.SetupFor(unknown));
    }

    [Fact]
    public void A_users_own_profile_wins_over_the_built_in_one()
    {
        using var bed = new DeviceTestbed(DeskWithDock(), new SourceText("mine.json", """
            { "id": "my-q1", "driver": "via", "match": { "vendorId": "0x3434", "productIds": [ "0x1012" ] }, "effects": { "solid": 1 } }
            """));
        Assert.Equal("my-q1", Assert.Single(bed.Source.Discover()).Id);
    }

    [Fact]
    public void Collections_no_driver_speaks_are_not_offered()
    {
        using var bed = new DeviceTestbed(new FakeHidBus().Add(FakeHidBus.KeyboardCollection(0x046D, 0xC33F, "Some keyboard")));
        Assert.Empty(bed.Source.Unrecognised());
        Assert.Empty(bed.Source.Discover());
    }
}

public class ViaSetupFlowTests
{
    private const int Steady = 0, Pulsing = 1, Neither = 2, Save = 0;

    private static (DeviceTestbed, FakeViaBoard, MatchAlert.Devices.Setup.ISetupFlow) NewBoard(FakeViaBoard board)
    {
        var bed = new DeviceTestbed(new FakeHidBus().Add(FakeHidBus.Via(0x1234, 0x5678, "Fake Board 75"), board));
        return (bed, board, bed.Source.SetupFor(bed.Source.Unrecognised()[0])!);
    }

    [Fact]
    public void Detects_what_it_can_and_asks_for_the_rest()
    {
        var (bed, board, flow) = NewBoard(new FakeViaBoard(resetAfterWrites: 2) { Effect = 7, Hue = 20 });
        using var _ = bed;
        var before = board.State;

        var profile = flow.Run(new ScriptedPrompt(Steady, Pulsing, Save));

        Assert.NotNull(profile);
        Assert.Equal(("fake-board-75", "Fake Board 75", "via"), (profile.Id, profile.Name, profile.Driver));
        Assert.Equal((1, 2), (profile.Effects["solid"], profile.Effects["breathing"]));
        Assert.Equal((ushort)0x1234, profile.Match.VendorId);
        Assert.Equal([(ushort)0x5678], profile.Match.ProductIds);
        Assert.True(profile.Options["via"].GetProperty("resetOnEffect").GetBoolean());
        Assert.Equal(before, board.State);
    }

    [Fact]
    public void Keeps_asking_until_it_has_a_steady_and_a_pulsing_effect()
    {
        var (bed, _, flow) = NewBoard(new FakeViaBoard());
        using var __ = bed;
        var prompt = new ScriptedPrompt(Neither, Steady, Pulsing, Save);

        var profile = flow.Run(prompt);

        Assert.Equal((2, 0), (profile!.Effects["solid"], profile.Effects["breathing"]));
        Assert.Contains("effect 1", prompt.Asked[0]);
        Assert.Contains("effect 0", prompt.Asked[2]);
    }

    [Fact]
    public void The_saved_profile_loads()
    {
        var (bed, _, flow) = NewBoard(new FakeViaBoard());
        using var __ = bed;
        var profile = flow.Run(new ScriptedPrompt(Steady, Pulsing, Save))!;

        var settings = SettingsLoader.Load(SettingsSources.BuiltIn() with
        {
            UserProfiles = [new SourceText("fake-board-75.json", SettingsLoader.Serialize(profile))],
        });
        Assert.Contains(settings.Profiles, p => p.Id == "fake-board-75");
    }

    [Fact]
    public void A_board_whose_lights_are_elsewhere_is_not_supported_and_nothing_is_written_off_channel()
    {
        var (bed, board, flow) = NewBoard(new FakeViaBoard(channel: 2) { Speed = 9 });
        using var _ = bed;
        var prompt = new ScriptedPrompt();

        Assert.Null(flow.Run(prompt));

        var (message, details) = Assert.Single(prompt.Told);
        Assert.Contains("not supported yet", message);
        Assert.Contains("0x1234", details);
        Assert.All(board.Packets.Where(p => p[0] is 0x07 or 0x08), p => Assert.Equal(3, p[1]));
        Assert.Equal(9, board.Speed);
    }

    [Fact]
    public void Cancelling_puts_the_lighting_back()
    {
        var (bed, board, flow) = NewBoard(new FakeViaBoard { Effect = 7, Hue = 20 });
        using var _ = bed;
        var before = board.State;

        Assert.Null(flow.Run(new ScriptedPrompt(Neither, null)));
        Assert.Equal(before, board.State);
    }

    [Fact]
    public void Declining_to_save_returns_nothing()
    {
        var (bed, _, flow) = NewBoard(new FakeViaBoard());
        using var __ = bed;
        Assert.Null(flow.Run(new ScriptedPrompt(Steady, Pulsing, 1)));
    }
}
