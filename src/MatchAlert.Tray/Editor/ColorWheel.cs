// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MatchAlert.Domain;
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace MatchAlert.Tray.Editor;

/// <summary>
/// Hue around, saturation outwards, always at full value: how bright a step is lives on its own slider,
/// because that is how the keyboard treats it. Hue follows the keyboards' 0-255 wheel, red at the top.
/// </summary>
internal sealed class ColorWheel : FrameworkElement
{
    private WriteableBitmap? _wheel;
    private Hsv _value = new(0, 255, 255);

    public event Action<Rgb>? ColorPicked;

    public ColorWheel()
    {
        Width = Height = 168;
    }

    /// <summary>The color shown, at full value. Setting it does not raise <see cref="ColorPicked"/>.</summary>
    public Rgb Color
    {
        set
        {
            var hsv = value.ToHsv();
            // Black and greys have no hue: keep the marker where it was rather than snap it to red.
            _value = hsv.S == 0 && hsv.V == 0 ? _value with { S = 0 } : new Hsv(hsv.H, hsv.S, 255);
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        _wheel ??= Render((int)size);
        dc.DrawImage(_wheel, new Rect(0, 0, size, size));

        double r = size / 2, angle = _value.H / 255.0 * 2 * Math.PI - Math.PI / 2, dist = _value.S / 255.0 * r;
        var marker = new WpfPoint(r + Math.Cos(angle) * dist, r + Math.Sin(angle) * dist);
        dc.DrawEllipse(null, new Pen(Brushes.Black, 3), marker, 7, 7);
        dc.DrawEllipse(null, new Pen(Brushes.White, 1.5), marker, 7, 7);
    }

    private static WriteableBitmap Render(int size)
    {
        var bmp = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[size * size * 4];
        double r = size / 2.0;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double dx = x + 0.5 - r, dy = y + 0.5 - r, dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist > r) continue;
                var (h, s) = HueSat(dx, dy, r);
                var c = new Hsv(h, s, 255).ToRgb();
                int i = (y * size + x) * 4;
                pixels[i] = c.B; pixels[i + 1] = c.G; pixels[i + 2] = c.R;
                pixels[i + 3] = (byte)Math.Clamp((r - dist) * 255, 0, 255);   // soft edge
            }
        bmp.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
        bmp.Freeze();
        return bmp;
    }

    private static (byte H, byte S) HueSat(double dx, double dy, double r)
    {
        double angle = Math.Atan2(dy, dx) + Math.PI / 2;
        if (angle < 0) angle += 2 * Math.PI;
        return ((byte)(Math.Round(angle / (2 * Math.PI) * 255) % 255), (byte)Math.Clamp(Math.Sqrt(dx * dx + dy * dy) / r * 255, 0, 255));
    }

    private void Pick(WpfPoint p)
    {
        double size = Math.Min(ActualWidth, ActualHeight), r = size / 2;
        var (h, s) = HueSat(p.X - r, p.Y - r, r);
        _value = new Hsv(h, s, 255);
        InvalidateVisual();
        ColorPicked?.Invoke(_value.ToRgb());
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        CaptureMouse();
        Pick(e.GetPosition(this));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured) Pick(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) => ReleaseMouseCapture();
}
