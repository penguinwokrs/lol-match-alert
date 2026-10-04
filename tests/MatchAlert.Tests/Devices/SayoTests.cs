// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.App;
using MatchAlert.Devices.Sayo;
using MatchAlert.Domain;

namespace MatchAlert.Tests.Devices;

public class SayoFrameTests
{
    private static byte[] Hex(string s) => Convert.FromHexString(s.Replace(" ", ""));

    // Packets Pulsar's own configurator library emitted (report id 0x22, payload after it), captured by
    // running its wasm against an emulated device. The frame code must produce the very same bytes.
    [Fact]
    public void Builds_the_read_request_the_configurator_sends()
    {
        var expected = Hex("12 4c 12 04 00 26 00");
        Assert.Equal(expected, SayoFrame.Request(0x22, 0x26, 0, []));
    }

    [Fact]
    public void Builds_the_write_request_the_configurator_sends()
    {
        var data = Enumerable.Range(0x40, 48).Select(i => (byte)i).ToArray();
        data[4] = 0x05;
        data[7] = 0x7F;
        var expected = Hex("12 65 92 34 00 26 00").Concat(data).ToArray();
        Assert.Equal(expected, SayoFrame.Request(0x22, 0x26, 0, data));
    }

    [Fact]
    public void The_checksum_of_the_configurators_save_command_matches_too()
    {
        // Never sent by this app; still a third independent check of the checksum.
        Assert.Equal(Hex("12 cb 84 06 00 0d 00 96 72"), SayoFrame.Request(0x22, 0x0D, 0, [0x96, 0x72]));
    }

    [Fact]
    public void Reads_a_reply_the_configurator_accepts_and_rejects_a_broken_one()
    {
        var data = Enumerable.Range(0x40, 48).Select(i => (byte)i).ToArray();
        var reply = Hex("12 a4 5a 34 00 26 00").Concat(data).Concat(new byte[8]).ToArray();

        Assert.Equal(data, SayoFrame.Data(0x22, reply, 0x26, 0));

        reply[10] ^= 1;
        Assert.Null(SayoFrame.Data(0x22, reply, 0x26, 0));
    }

    [Fact]
    public void A_reply_with_a_failure_status_or_to_another_command_is_not_ours()
    {
        var ok = SayoFrame.Request(0x22, 0x26, 0, new byte[48]);
        Assert.Null(SayoFrame.Data(0x22, ok, 0x27, 0));
        Assert.Null(SayoFrame.Data(0x22, ok, 0x26, 1));

        var failed = (byte[])ok.Clone();
        failed[4] |= 0x0C;   // status 3 in the top six bits
        var crc = SayoFrame.Crc(0x22, failed);
        (failed[1], failed[2]) = ((byte)crc, (byte)(crc >> 8));
        Assert.Null(SayoFrame.Data(0x22, failed, 0x26, 0));
    }
}

public class SayoDriverTests
{
    private const ushort Pulsar = 0x3710, Pcmk3He60 = 0x2404, Unnamed = 0x2506;
    private static readonly Step Red = new(Rgb.Parse("#FF0000"), 100, "solid", null, 300);
    private static readonly Step White = new(Rgb.Parse("#FFFFFF"), 100, "solid", null, 300);

    private static (DeviceTestbed Bed, FakeSayoBoard Board) SetUp(FakeSayoBoard? board = null)
    {
        board ??= new FakeSayoBoard();
        var bus = new FakeHidBus()
            .Add(FakeHidBus.KeyboardCollection(Pulsar, Unnamed, "Pulsar Something"))
            .Add(FakeHidBus.Sayo(Pulsar, Unnamed, "Pulsar Something"), board);
        var bed = new DeviceTestbed(bus);
        var profile = bed.Source.SetupFor(Assert.Single(bed.Source.Unrecognised()))!.Run(new ScriptedPrompt())!;
        return (new DeviceTestbed(bus, new SourceText("p.json", SettingsLoader.Serialize(profile))), board);
    }

    [Fact]
    public void Setup_reads_only_asks_nothing_and_makes_a_profile_that_loads()
    {
        var (bed, board) = SetUp();
        using var _ = bed;
        var (_, profile) = Assert.Single(bed.Source.Recognised());

        Assert.Equal(("pulsar-something", "Pulsar Something", "sayo"), (profile.Id, profile.Name, profile.Driver));
        Assert.Equal((0x50, 0x30, 1000), (profile.Effects["solid"], profile.Effects["breathing"], profile.MinStepMs));
        Assert.Equal(0, board.Writes);
    }

    [Fact]
    public void Only_led_effect_frames_on_report_0x22_are_sent_and_never_save()
    {
        var (bed, board) = SetUp();
        using var _ = bed;
        using (var s = bed.Source.Discover()[0].OpenSession()) { s.Show(Red); s.Show(White); }

        Assert.All(board.Packets, p => Assert.Equal((0x22, 0x26), (p.ReportId, p.Payload[5])));
    }

    [Fact]
    public void Shows_the_color_and_puts_the_whole_block_back()
    {
        var (bed, board) = SetUp();
        using var _ = bed;
        var before = board.Effect.ToArray();

        using (var s = bed.Source.Discover()[0].OpenSession())
        {
            s.Show(Red);
            Assert.Equal((255, 0, 0, 255, 5, 0, 100), (board.Effect[0], board.Effect[1], board.Effect[2], board.Effect[3], board.Effect[4], board.Effect[5], board.Effect[7]));
            Assert.Equal(before[8..], board.Effect[8..]);   // indicator and profile colors untouched
        }

        Assert.Equal(before, board.Effect);
    }

    [Fact]
    public void Falls_back_to_report_0x21_like_the_configurator()
    {
        var (bed, board) = SetUp(new FakeSayoBoard(reportId: 0x21));
        using var _ = bed;
        using (var s = bed.Source.Discover()[0].OpenSession())
        {
            s.Show(Red);
            Assert.Equal(255, board.Effect[0]);
        }
        Assert.Contains(board.Packets, p => p.ReportId == 0x21);
        Assert.All(board.Packets, p => Assert.Equal(0x26, p.Payload[5]));
    }

    [Fact]
    public void A_corrupt_or_foreign_report_is_skipped()
    {
        var (bed, board) = SetUp();
        using var _ = bed;
        var junk = SayoFrame.Request(0x22, 0x26, 0, new byte[48]);
        junk[20] ^= 0xFF;   // crc no longer matches
        board.QueueForeign(junk);
        var (hid, profile) = bed.Source.Recognised()[0];
        Assert.True(bed.Sayo.ReadState(hid, profile).Matches(bed.Sayo.ReadState(hid, profile)));
    }

    [Fact]
    public void After_a_crash_the_next_start_restores_through_rgb_rounding()
    {
        var (bed, board) = SetUp();
        using var _ = bed;
        var before = board.Effect.ToArray();
        bed.Source.Discover()[0].OpenSession().Show(new Step(Rgb.Parse("#FFB000"), 100, "breathing", 200, 0));

        bed.Restart();
        bed.Source.RecoverInterruptedSessions();

        Assert.Equal(before, board.Effect);
    }

    [Theory]
    [InlineData((ushort)0x2404, "pulsar-pcmk-3-he-60")]
    [InlineData((ushort)0x2502, "pulsar-pcmk-3-he-tkl")]
    [InlineData((ushort)0x2504, "pulsar-pcmk-3-he-tkl")]
    public void The_pcmk_3_he_series_is_recognised_without_setup(ushort pid, string id)
    {
        var board = new FakeSayoBoard();
        using var bed = new DeviceTestbed(new FakeHidBus().Add(FakeHidBus.Sayo(Pulsar, pid, "PCMK 3 HE"), board));
        var (_, profile) = Assert.Single(bed.Source.Recognised());
        Assert.Equal((id, "sayo"), (profile.Id, profile.Driver));
        Assert.Empty(bed.Source.Unrecognised());

        var before = board.Effect.ToArray();
        using (var s = bed.Source.Discover()[0].OpenSession()) s.Show(bed.Settings.PatternFor(profile).Steps[0]);
        Assert.Equal(before, board.Effect);
        Assert.All(board.Packets, p => Assert.Equal(0x26, p.Payload[5]));
    }

    [Fact]
    public void Vendor_collections_of_other_devices_are_not_offered_for_setup()
    {
        using var bed = new DeviceTestbed(new FakeHidBus().Add(FakeHidBus.Sayo(0x054C, 0x0E9A, "INZONE Buds")));
        Assert.Empty(bed.Source.Unrecognised());
    }
}
