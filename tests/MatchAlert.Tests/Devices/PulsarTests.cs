// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text.RegularExpressions;
using MatchAlert.App;
using MatchAlert.Devices.Pulsar;
using MatchAlert.Domain;

namespace MatchAlert.Tests.Devices;

public partial class PulsarTests
{
    private const ushort Pcmk2HeTkl = 0x2402, XboardMsJis = 0x2407, UnknownPulsar = 0x2499;
    private static readonly Step Red = new(Rgb.Parse("#FF0000"), 100, "solid", null, 300);
    private static readonly Step White = new(Rgb.Parse("#FFFFFF"), 100, "solid", null, 300);

    private static (DeviceTestbed, FakePulsarBoard) Board(ushort pid = Pcmk2HeTkl, FakePulsarBoard? board = null)
    {
        board ??= new FakePulsarBoard();
        var bus = new FakeHidBus()
            .Add(FakeHidBus.Pulsar(pid, "Pulsar keyboard"), board)
            .Add(FakeHidBus.PulsarBoot(pid));   // opening this throws in the fake bus: it has no board
        return (new DeviceTestbed(bus), board);
    }

    [Theory]
    [InlineData(Pcmk2HeTkl, "pulsar-pcmk-2he-tkl")]
    [InlineData(XboardMsJis, "pulsar-xboard-ms")]
    public void Known_models_are_recognised_on_the_raw_hid_interface_only(ushort pid, string id)
    {
        var (bed, _) = Board(pid);
        using var _ = bed;
        var (hid, profile) = Assert.Single(bed.Source.Recognised());
        Assert.Equal((id, "pulsar"), (profile.Id, profile.Driver));
        Assert.Equal(0xFF60, hid.UsagePage);
        Assert.Empty(bed.Source.Unrecognised());
    }

    [Fact]
    public void The_built_in_profiles_keep_writes_rare_until_verified()
    {
        var settings = SettingsLoader.Load(SettingsSources.BuiltIn());
        foreach (var p in settings.Profiles.Where(p => p.Driver == "pulsar"))
        {
            Assert.True(p.MinStepMs >= 1000, p.Id);
            Assert.Single(settings.PatternFor(p).Steps);   // the firmware's own breathing: one write per alert
            Assert.Equal("breathing", settings.PatternFor(p).Steps[0].Effect);
        }
    }

    [Fact]
    public void Every_command_names_the_active_profile_and_a_blink_changes_the_effect_once()
    {
        var (bed, board) = Board(board: new FakePulsarBoard(profile: 2));
        using var _ = bed;
        using (var session = bed.Source.Discover()[0].OpenSession())
        {
            session.Show(Red);
            session.Show(White);
            session.Show(Red);
            Assert.Equal(1, board.EffectWrites);
            Assert.Equal((1, 0, 255), (board.Effect, board.Hue, board.Sat));
        }
        Assert.All(board.Packets.Where(p => p[0] is 0x22 or 0x23), p => Assert.Equal(2, p[3]));
    }

    [Fact]
    public void Ending_the_session_puts_every_value_back_even_lights_that_were_off()
    {
        var (bed, board) = Board(board: new FakePulsarBoard { Enabled = false, Effect = 27, Speed = 10 });
        using var _ = bed;
        var before = board.State;

        using (var session = bed.Source.Discover()[0].OpenSession())
        {
            session.Show(Red);
            Assert.True(board.Enabled);   // switched on to be seen
        }

        Assert.Equal(before, board.State);
    }

    [Fact]
    public void Only_reads_and_lighting_writes_are_ever_sent()
    {
        // The allowlist, proven: snapshot, play, restore, crash recovery, state readback and setup.
        var (bed, board) = Board();
        using var _ = bed;
        var (hid, profile) = bed.Source.Recognised()[0];
        bed.Pulsar.ReadState(hid, profile);
        using (var s = bed.Source.Discover()[0].OpenSession()) { s.Show(Red); s.Show(White); }
        bed.Source.Discover()[0].OpenSession().Show(Red);   // "crashes"
        bed.Restart();
        bed.Source.RecoverInterruptedSessions();
        new PulsarDriver(bed.Bus, bed.Pending, _ => { }, new PulsarTiming(1, 1)).TrySetup(hid)!.Run(new ScriptedPrompt());

        Assert.NotEmpty(board.Packets);
        Assert.All(board.Packets, p => Assert.Matches(Allowed(), Convert.ToHexString(p.AsSpan(0, 3))));
    }

    [GeneratedRegex("^(24....|2202(0[1-5])|2302(0[1-5]))$")]
    private static partial Regex Allowed();

    [Fact]
    public void Works_whether_or_not_replies_echo_the_header()
    {
        var (bed, board) = Board(board: new FakePulsarBoard(echoHeader: false) { Effect = 27 });
        using var _ = bed;
        using (var session = bed.Source.Discover()[0].OpenSession()) session.Show(Red);
        Assert.Equal(27, board.Effect);
    }

    [Fact]
    public void Another_programs_reports_are_skipped()
    {
        var (bed, board) = Board();
        using var _ = bed;
        board.QueueForeign(0x11, 0x22, 0x33);
        board.QueueForeign(0x23, 0x04, 0x01);
        var (hid, profile) = bed.Source.Recognised()[0];
        Assert.Equal(13, bed.Pulsar.ReadState(hid, profile).Fields.Single(f => f.Name == "effect").Value);
    }

    [Fact]
    public void A_board_that_does_not_answer_fails_to_open_cleanly()
    {
        var (bed, _) = Board(board: new FakePulsarBoard { Silent = true });
        using var __ = bed;
        Assert.Throws<IOException>(() => bed.Source.Discover()[0].OpenSession());
    }

    [Fact]
    public void State_read_back_after_an_alert_matches_the_state_before()
    {
        var (bed, _) = Board();
        using var __ = bed;
        var (hid, profile) = bed.Source.Recognised()[0];
        var before = bed.Pulsar.ReadState(hid, profile);
        using (var s = bed.Source.Discover()[0].OpenSession()) s.Show(White);
        Assert.True(before.Matches(bed.Pulsar.ReadState(hid, profile)));
    }

    [Fact]
    public void After_a_crash_the_next_start_restores_if_the_lighting_is_still_ours()
    {
        var (bed, board) = Board();
        using var _ = bed;
        var before = board.State;
        bed.Source.Discover()[0].OpenSession().Show(Red);   // never disposed

        bed.Restart();
        bed.Source.RecoverInterruptedSessions();

        Assert.Equal(before, board.State);
    }

    [Fact]
    public void A_pulsar_keyboard_on_via_firmware_falls_through_to_the_via_wizard()
    {
        var via = new FakeViaBoard();
        using var bed = new DeviceTestbed(new FakeHidBus().Add(FakeHidBus.Via(0x3710, UnknownPulsar, "Xboard QS"), via));
        var flow = bed.Source.SetupFor(Assert.Single(bed.Source.Unrecognised()))!;

        var profile = flow.Run(new ScriptedPrompt(0, 1, 0));   // steady, pulsing, save

        Assert.Equal("via", profile!.Driver);
        Assert.Contains(via.Packets, p => p[0] == 0x24);   // the Pulsar probe was tried first, and only read
    }

    [Fact]
    public void An_unknown_pulsar_model_is_set_up_by_the_pulsar_driver_without_questions()
    {
        var (bed, board) = Board(UnknownPulsar);
        using var _ = bed;
        var candidate = Assert.Single(bed.Source.Unrecognised());
        var flow = bed.Source.SetupFor(candidate)!;

        var profile = flow.Run(new ScriptedPrompt());   // no scripted answers: any question would throw

        Assert.NotNull(profile);
        Assert.Equal(("pulsar", 1, 5, 1000), (profile.Driver, profile.Effects["solid"], profile.Effects["breathing"], profile.MinStepMs));
        Assert.All(board.Packets, p => Assert.Contains(p[0], new byte[] { 0x24, 0x22 }));
        Assert.Contains(SettingsLoader.Load(SettingsSources.BuiltIn() with
        {
            UserProfiles = [new SourceText("p.json", SettingsLoader.Serialize(profile))],
        }).Profiles, p => p.Id == profile.Id);
    }
}
