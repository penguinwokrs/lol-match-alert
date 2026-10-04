// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MatchAlert.Tray.Resources;
using WpfColor = System.Windows.Media.Color;

namespace MatchAlert.Tray.Editor;

/// <summary>Asks for a pattern name. WPF has no input box; this is the smallest one that matches the editor.</summary>
internal static class NameDialog
{
    public static string? Ask(Window owner, string prompt, string suggestion)
    {
        var text = new SolidColorBrush(WpfColor.FromRgb(0xE8, 0xEA, 0xED));
        var box = new TextBox
        {
            Text = suggestion,
            Margin = new Thickness(0, 8, 0, 14),
            Padding = new Thickness(6, 4, 6, 4),
            Foreground = text,
            Background = new SolidColorBrush(WpfColor.FromRgb(0x26, 0x29, 0x2D)),
            CaretBrush = text,
        };
        var ok = new Button { Content = Strings.Editor_Ok, IsDefault = true, MinWidth = 80, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = Strings.Editor_Cancel, IsCancel = true, MinWidth = 80, Height = 30 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } };
        var dialog = new Window
        {
            Owner = owner,
            Title = owner.Title,
            Width = 360,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(WpfColor.FromRgb(0x20, 0x22, 0x25)),
            Resources = owner.Resources,   // the editor's dark buttons and text boxes
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Children = { new TextBlock { Text = prompt, Foreground = text }, box, buttons },
            },
        };
        ok.Click += (_, _) => dialog.DialogResult = !string.IsNullOrWhiteSpace(box.Text);
        dialog.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        box.KeyDown += (_, e) => { if (e.Key == Key.Escape) dialog.DialogResult = false; };
        return dialog.ShowDialog() == true ? box.Text.Trim() : null;
    }
}
