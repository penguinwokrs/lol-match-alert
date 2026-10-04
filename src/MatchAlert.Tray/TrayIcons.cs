// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Windows.Forms;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using MatchAlert.App;

namespace MatchAlert.Tray;

/// <summary>A small keyboard drawn at run time, its keys colored by status, so no icon files are needed per state.</summary>
internal static class TrayIcons
{
    public static Icon For(AlertStatus status) => Draw(status switch
    {
        AlertStatus.Alerting => Color.FromArgb(0xFF, 0x30, 0x30),
        AlertStatus.Connected => Color.FromArgb(0x4C, 0xC2, 0xFF),
        _ => Color.FromArgb(0x9A, 0xA0, 0xA6),
    });

    private static Icon Draw(Color keys)
    {
        int size = SystemInformation.SmallIconSize.Width;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = size / 16f;
            var body = new RectangleF(0.5f * s, 3.5f * s, 15f * s, 9.5f * s);
            using (var path = Rounded(body, 2f * s))
            using (var fill = new SolidBrush(Color.FromArgb(0x26, 0x2A, 0x30)))
            using (var edge = new Pen(keys, 1f * s))
            {
                g.FillPath(fill, path);
                g.DrawPath(edge, path);
            }
            using var key = new SolidBrush(keys);
            for (int row = 0; row < 2; row++)
                for (int col = 0; col < 4; col++)
                    g.FillRectangle(key, (2.2f + col * 3f) * s, (5.4f + row * 2.6f) * s, 2f * s, 1.6f * s);
            g.FillRectangle(key, 4.6f * s, 10.6f * s, 6.8f * s, 1.2f * s);
        }
        IntPtr handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
