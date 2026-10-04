// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.Domain;

namespace MatchAlert.Tests.Domain;

public class ColorTests
{
    // Keyboards use QMK's 0-255 hue wheel: red 0, yellow ~43, green 85, blue 170.
    [Theory]
    [InlineData("#FF0000", 0, 255, 255)]
    [InlineData("#00FF00", 85, 255, 255)]
    [InlineData("#0000FF", 170, 255, 255)]
    [InlineData("#FFFF00", 43, 255, 255)]
    [InlineData("#FFFFFF", 0, 0, 255)]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#800000", 0, 255, 128)]
    public void Converts_to_the_keyboard_hue_wheel(string hex, int h, int s, int v)
    {
        Assert.Equal(new Hsv((byte)h, (byte)s, (byte)v), Rgb.Parse(hex).ToHsv());
    }

    [Theory]
    [InlineData("ff8000")]
    [InlineData("#ff8000")]
    [InlineData(" #FF8000 ")]
    public void Parses_with_or_without_hash_in_any_case(string hex)
    {
        Assert.Equal(new Rgb(0xFF, 0x80, 0x00), Rgb.Parse(hex));
    }

    [Theory]
    [InlineData("#GGG000")]
    [InlineData("#FFF")]
    [InlineData("")]
    [InlineData("#FF00001")]
    public void Rejects_anything_but_six_hex_digits(string hex)
    {
        Assert.Throws<FormatException>(() => Rgb.Parse(hex));
    }

    [Fact]
    public void Formats_back_to_hex()
    {
        Assert.Equal("#FF8000", new Rgb(0xFF, 0x80, 0x00).ToString());
    }

    [Theory]
    [InlineData("#FF0000")]
    [InlineData("#00FF00")]
    [InlineData("#0000FF")]
    [InlineData("#FFB000")]
    [InlineData("#FFFFFF")]
    [InlineData("#0080FF")]
    [InlineData("#800000")]
    public void Converts_back_to_rgb_within_rounding(string hex)
    {
        var rgb = Rgb.Parse(hex);
        var back = rgb.ToHsv().ToRgb();
        Assert.InRange(Math.Abs(back.R - rgb.R), 0, 3);
        Assert.InRange(Math.Abs(back.G - rgb.G), 0, 3);
        Assert.InRange(Math.Abs(back.B - rgb.B), 0, 3);
    }
}
