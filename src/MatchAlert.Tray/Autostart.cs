// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using Microsoft.Win32;

namespace MatchAlert.Tray;

/// <summary>Start with Windows, per user, through the Run key. No elevation, no scheduled task.</summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(AppPaths.AppName) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(AppPaths.AppName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(AppPaths.AppName, throwOnMissingValue: false);
    }
}
