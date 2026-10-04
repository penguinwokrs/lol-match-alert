// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Globalization;

namespace MatchAlert.Domain;

/// <summary>A color as people write it: <c>#RRGGBB</c>.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb Parse(string hex)
    {
        var s = hex.Trim().TrimStart('#');
        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"\"{hex}\" is not a color; write it as #RRGGBB, for example #FF0000.");
        return new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    /// <summary>
    /// Converts to the 0-255 hue wheel keyboards use (QMK and VIA): red 0, yellow 43, green 85,
    /// blue 170. Value carries the color's own lightness, so #800000 is red at half brightness.
    /// </summary>
    public Hsv ToHsv()
    {
        int max = Math.Max(R, Math.Max(G, B)), min = Math.Min(R, Math.Min(G, B)), delta = max - min;
        byte s = max == 0 ? (byte)0 : (byte)Math.Round(delta * 255.0 / max, MidpointRounding.AwayFromZero);
        if (delta == 0) return new Hsv(0, s, (byte)max);

        double degrees =
            max == R ? 60.0 * ((G - B) / (double)delta) :
            max == G ? 60.0 * ((B - R) / (double)delta + 2) :
                       60.0 * ((R - G) / (double)delta + 4);
        if (degrees < 0) degrees += 360;
        // 255 and 0 are both red on the wheel; keep it in one place.
        var h = (int)Math.Round(degrees * 255 / 360, MidpointRounding.AwayFromZero) % 255;
        return new Hsv((byte)h, s, (byte)max);
    }

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>Hue, saturation and value on the 0-255 scale keyboard firmware uses.</summary>
public readonly record struct Hsv(byte H, byte S, byte V)
{
    /// <summary>Back to RGB, for firmware that stores colors that way. The inverse of <see cref="Rgb.ToHsv"/> to within rounding.</summary>
    public Rgb ToRgb()
    {
        if (S == 0) return new Rgb(V, V, V);
        double h = H * 360.0 / 255 / 60, s = S / 255.0, v = V;
        int sector = (int)Math.Floor(h) % 6;
        double f = h - Math.Floor(h);
        byte p = Round(v * (1 - s)), q = Round(v * (1 - s * f)), t = Round(v * (1 - s * (1 - f))), w = V;
        return sector switch
        {
            0 => new Rgb(w, t, p),
            1 => new Rgb(q, w, p),
            2 => new Rgb(p, w, t),
            3 => new Rgb(p, q, w),
            4 => new Rgb(t, p, w),
            _ => new Rgb(w, p, q),
        };

        static byte Round(double x) => (byte)Math.Round(x, MidpointRounding.AwayFromZero);
    }
}
