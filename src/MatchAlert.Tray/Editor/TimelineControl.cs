// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MatchAlert.App;
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace MatchAlert.Tray.Editor;

/// <summary>
/// The pattern's steps as color blocks, left to right, each as wide as it lasts. Drag a block's right edge to
/// change its duration, drag a block to move it, click + to add a step. Drawn by hand: a handful of
/// rectangles is simpler than a templated items control with thumbs.
/// </summary>
internal sealed class TimelineControl : FrameworkElement
{
    private const double AddWidth = 44, Handle = 7, Gap = 2, DragThreshold = 6;

    private PatternDraft? _draft;
    private int _selected;
    private int _playing = -1;

    private enum Drag { None, Resize, Move }
    private Drag _drag;
    private int _dragIndex;
    private WpfPoint _dragStart;
    private int _dragStartMs;
    private double _dragScale;
    private int _dropIndex = -1;
    private bool _pressed;

    public event Action<int>? SelectionChanged;
    public event Action? DraftChanged;

    public string HoldText { get; set; } = "";

    public PatternDraft? Draft
    {
        get => _draft;
        set { _draft = value; _selected = 0; InvalidateVisual(); }
    }

    public int Selected
    {
        get => _selected;
        set { _selected = value; InvalidateVisual(); }
    }

    /// <summary>The step the on-screen preview is showing, marked under its block.</summary>
    public int Playing
    {
        get => _playing;
        set { if (_playing != value) { _playing = value; InvalidateVisual(); } }
    }

    public TimelineControl()
    {
        Height = 92;
        Focusable = true;
    }

    private double Scale()
    {
        if (_draft is null || _draft.Steps.Count <= 1) return 0;
        double total = _draft.Steps.Sum(s => s.DurationMs);
        return Math.Max(0.02, (ActualWidth - AddWidth - Gap * _draft.Steps.Count) / Math.Max(total, 1));
    }

    /// <summary>Each block's left and right edge, at the current scale (or the one fixed when a drag began).</summary>
    private List<(double Left, double Right)> Blocks(double? scale = null)
    {
        var blocks = new List<(double, double)>();
        if (_draft is null) return blocks;
        if (_draft.Steps.Count == 1)
        {
            blocks.Add((0, ActualWidth - AddWidth - Gap));
            return blocks;
        }
        double s = scale ?? Scale(), x = 0;
        foreach (var step in _draft.Steps)
        {
            double w = Math.Max(14, step.DurationMs * s);
            blocks.Add((x, x + w));
            x += w + Gap;
        }
        return blocks;
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(WpfColor.FromRgb(0x26, 0x29, 0x2D)), null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_draft is null) return;

        var blocks = Blocks(_drag == Drag.Resize ? _dragScale : null);
        double top = 6, height = ActualHeight - 30;
        for (int i = 0; i < blocks.Count; i++)
        {
            var (left, right) = blocks[i];
            var step = _draft.Steps[i];
            var rect = new Rect(left, top, Math.Max(1, right - left), height);
            var shown = Shown(step);
            dc.DrawRoundedRectangle(new SolidColorBrush(shown), new Pen(new SolidColorBrush(WpfColor.FromRgb(0x3A, 0x3D, 0x42)), 1), rect, 4, 4);
            if (step.Effect == "breathing")
            {
                // A pale wave across the block: this step breathes.
                var wave = new StreamGeometry();
                using (var g = wave.Open())
                {
                    g.BeginFigure(new WpfPoint(left + 4, top + height / 2), false, false);
                    for (double x = left + 4; x <= right - 4; x += 3)
                        g.LineTo(new WpfPoint(x, top + height / 2 + Math.Sin((x - left) / 6) * height / 5), true, false);
                }
                dc.DrawGeometry(null, new Pen(new SolidColorBrush(WpfColor.FromArgb(150, 255, 255, 255)), 1.5), wave);
            }
            if (i == _selected) dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(WpfColor.FromRgb(0x4C, 0x9A, 0xFF)), 3), rect, 4, 4);

            string label = _draft.Steps.Count == 1 ? HoldText : $"{step.DurationMs} ms";
            var text = Text(label, 12, WpfColor.FromRgb(0xE8, 0xEA, 0xED));
            if (text.Width <= right - left || _draft.Steps.Count == 1)
                dc.DrawText(text, new WpfPoint(left + Math.Max(2, (right - left - text.Width) / 2), top + height + 4));
            if (i == _playing)
                dc.DrawEllipse(new SolidColorBrush(WpfColor.FromRgb(0x4C, 0x9A, 0xFF)), null, new WpfPoint((left + right) / 2, top - 1), 3, 3);
            if (_draft.Steps.Count > 1)   // resize grip
                dc.DrawRectangle(new SolidColorBrush(WpfColor.FromArgb(120, 0, 0, 0)), null, new Rect(right - Handle + 2, top + height / 3, 3, height / 3));
        }

        if (_drag == Drag.Move && _dropIndex >= 0)
        {
            double x = _dropIndex < blocks.Count ? blocks[_dropIndex].Left - Gap / 2 - 1 : blocks[^1].Right + 1;
            dc.DrawRectangle(new SolidColorBrush(WpfColor.FromRgb(0x4C, 0x9A, 0xFF)), null, new Rect(x - 1.5, 0, 3, ActualHeight - 22));
        }

        var add = AddRect();
        dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(WpfColor.FromRgb(0x5F, 0x63, 0x68)), 1) { DashStyle = DashStyles.Dash }, add, 4, 4);
        var plus = Text("+", 22, WpfColor.FromRgb(0x9A, 0xA0, 0xA6));
        dc.DrawText(plus, new WpfPoint(add.Left + (add.Width - plus.Width) / 2, add.Top + (add.Height - plus.Height) / 2));
    }

    /// <summary>The color as the keyboard will show it: brightness applied, so a dim step looks dim.</summary>
    internal static WpfColor Shown(StepDraft step, double breath = 1)
    {
        double k = step.Brightness / 100.0 * breath;
        return WpfColor.FromRgb((byte)(step.Color.R * k), (byte)(step.Color.G * k), (byte)(step.Color.B * k));
    }

    private Rect AddRect() => new(ActualWidth - AddWidth + Gap, 6, AddWidth - Gap, ActualHeight - 30);

    private FormattedText Text(string s, double size, WpfColor color) =>
        new(s, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight, new Typeface("Segoe UI"), size,
            new SolidColorBrush(color), VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private (int Index, bool OnHandle) HitTest(WpfPoint p)
    {
        var blocks = Blocks();
        for (int i = 0; i < blocks.Count; i++)
        {
            var (left, right) = blocks[i];
            if (p.X >= left && p.X <= right + Gap)
                return (i, _draft!.Steps.Count > 1 && p.X >= right - Handle);
        }
        return (-1, false);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_drag == Drag.None && !_pressed)
        {
            Cursor = HitTest(p).OnHandle ? Cursors.SizeWE : AddRect().Contains(p) ? Cursors.Hand : Cursors.Arrow;
            return;
        }
        if (_drag == Drag.Resize)
        {
            _draft!.SetDuration(_dragIndex, (int)(_dragStartMs + (p.X - _dragStart.X) / _dragScale));
            DraftChanged?.Invoke();
            InvalidateVisual();
            return;
        }
        if (_pressed && _drag == Drag.None && Math.Abs(p.X - _dragStart.X) > DragThreshold && _draft!.Steps.Count > 1)
            _drag = Drag.Move;
        if (_drag == Drag.Move)
        {
            var blocks = Blocks();
            _dropIndex = blocks.Count;
            for (int i = 0; i < blocks.Count; i++)
                if (p.X < (blocks[i].Left + blocks[i].Right) / 2) { _dropIndex = i; break; }
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var p = e.GetPosition(this);
        if (_draft is null) return;
        if (AddRect().Contains(p))
        {
            _selected = _draft.Add(after: _selected);
            SelectionChanged?.Invoke(_selected);
            DraftChanged?.Invoke();
            InvalidateVisual();
            return;
        }
        var (index, onHandle) = HitTest(p);
        if (index < 0) return;
        _selected = index;
        SelectionChanged?.Invoke(index);
        _dragIndex = index;
        _dragStart = p;
        _pressed = true;
        if (onHandle)
        {
            _drag = Drag.Resize;
            _dragStartMs = _draft.Steps[index].DurationMs;
            _dragScale = Scale();
        }
        CaptureMouse();
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_drag == Drag.Move && _dropIndex >= 0 && _draft is not null)
        {
            int to = _dropIndex > _dragIndex ? _dropIndex - 1 : _dropIndex;
            if (to != _dragIndex)
            {
                _draft.Move(_dragIndex, to);
                _selected = to;
                SelectionChanged?.Invoke(to);
                DraftChanged?.Invoke();
            }
        }
        _drag = Drag.None;
        _dropIndex = -1;
        _pressed = false;
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        InvalidateVisual();
    }
}
