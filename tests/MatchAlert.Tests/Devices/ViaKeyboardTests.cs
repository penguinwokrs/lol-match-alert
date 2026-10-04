// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.Devices.Via;

namespace MatchAlert.Tests.Devices;

public class ViaKeyboardTests
{
    internal static readonly ViaTiming NoWait = new(0, 0, 0, 1000, 1, 1, 5);

    private static ViaKeyboard Open(FakeViaBoard board, bool resetOnEffect = false) => new(board, board.Channel, resetOnEffect, NoWait);

    [Fact]
    public void Reads_the_protocol_version()
    {
        Assert.Equal(13, Open(new FakeViaBoard(protocol: 13)).Protocol);
        Assert.False(Open(new FakeViaBoard(protocol: 9)).IsV3);
    }

    [Fact]
    public void V3_commands_carry_the_channel()
    {
        var board = new FakeViaBoard(protocol: 13, channel: 3);
        Open(board).Set(ViaValue.Effect, 1);
        Assert.Equal(new byte[] { 0x07, 0x03, 0x02, 0x01 }, board.Packets.Last()[..4]);
    }

    [Fact]
    public void V2_commands_use_the_lighting_ids()
    {
        var board = new FakeViaBoard(protocol: 9);
        Open(board).Set(ViaValue.Effect, 1);
        Assert.Equal(new byte[] { 0x07, 0x81, 0x01 }, board.Packets.Last()[..3]);
        Assert.Equal(1, board.Effect);
    }

    [Fact]
    public void Snapshot_reads_all_four_values()
    {
        var board = new FakeViaBoard { StoredBrightness = 180, Effect = 16, Speed = 0, Hue = 10, Sat = 200 };
        Assert.Equal(new ViaSnapshot(180, 16, 0, 10, 200), Open(board).Snapshot());
    }

    [Fact]
    public void Another_programs_echo_is_skipped()
    {
        var board = new FakeViaBoard { Effect = 16 };
        var kb = Open(board);
        // Someone else's GET speed and SET color land in our input queue first. (An echo of the very same
        // command cannot be told apart from our own; that is inherent to VIA, not something to fix here.)
        board.QueueForeignEcho(0x08, 0x03, 0x03, 0x63);
        board.QueueForeignEcho(0x07, 0x03, 0x04, 0x10, 0x20);

        Assert.Equal(16, kb.Get(ViaValue.Effect, 1)[0]);
        Assert.Equal(16, kb.Snapshot().Effect);
    }

    [Fact]
    public void Apply_on_a_plain_board_writes_everything()
    {
        var board = new FakeViaBoard();
        Assert.True(Open(board).Apply(effect: 1, hue: 85, sat: 255, speed: 50, brightness: 255));
        Assert.Equal((1, 85, 255, 255, 50), ((int)board.Effect, (int)board.Hue, (int)board.Sat, (int)board.Brightness, (int)board.Speed));
    }

    [Fact]
    public void Apply_beats_the_post_effect_reset()
    {
        var board = new FakeViaBoard(resetAfterWrites: 2);
        Assert.True(Open(board, resetOnEffect: true).Apply(effect: 1, hue: 85, sat: 255, speed: null, brightness: 150));

        Assert.Equal((1, 85, 255, 150), ((int)board.Effect, (int)board.Hue, (int)board.Sat, (int)board.Brightness));
    }

    [Fact]
    public void Without_the_workaround_the_reset_wins()
    {
        // The reason resetOnEffect exists: written straight through, the reset's red sticks.
        var board = new FakeViaBoard(resetAfterWrites: 1);
        Open(board, resetOnEffect: false).Apply(effect: 1, hue: 85, sat: 255, speed: null, brightness: 150);
        Assert.Equal(0, board.Hue);
    }

    [Fact]
    public void ShowColor_never_touches_the_effect()
    {
        var board = new FakeViaBoard { Effect = 1 };
        Open(board).ShowColor(hue: 170, sat: 255, brightness: 100);

        Assert.Equal(0, board.EffectWrites);
        Assert.Equal((170, 100), ((int)board.Hue, (int)board.Brightness));
    }

    [Fact]
    public void A_working_channel_verifies_and_keeps_its_speed()
    {
        var board = new FakeViaBoard { Speed = 77 };
        Assert.True(Open(board).VerifyChannel());
        Assert.Equal(77, board.Speed);
    }

    [Fact]
    public void A_channel_that_does_not_drive_the_board_fails_verification()
    {
        var board = new FakeViaBoard(channel: 2);
        var kb = new ViaKeyboard(board, channel: 3, resetOnEffect: false, NoWait);
        Assert.False(kb.VerifyChannel());
    }

    [Fact]
    public void Detects_the_post_effect_reset()
    {
        Assert.True(Open(new FakeViaBoard(resetAfterWrites: 1)).ProbeResetOnEffect(otherEffect: 2));
        Assert.False(Open(new FakeViaBoard()).ProbeResetOnEffect(otherEffect: 2));
    }

    [Fact]
    public void Never_saves_to_eeprom()
    {
        var board = new FakeViaBoard(resetAfterWrites: 2);
        var kb = Open(board, resetOnEffect: true);
        kb.Restore(kb.Snapshot());
        kb.Apply(1, 0, 255, 10, 255);
        kb.ShowColor(1, 2, 3);
        kb.VerifyChannel();
        kb.ProbeResetOnEffect(2);

        Assert.DoesNotContain(board.Packets, p => p[0] == 0x09);
    }
}
