// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

namespace MatchAlert.Tray;

/// <summary>
/// Where things live. What people edit goes in Roaming AppData, so it follows them between machines;
/// the log and in-flight snapshots are this machine's own and go in Local.
/// </summary>
internal static class AppPaths
{
    public const string AppName = "lol-match-alert";

    public static string SettingsDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);

    public static string SettingsFile => Path.Combine(SettingsDirectory, MatchAlert.App.SettingsSources.SettingsFileName);
    public static string DevicesDirectory => Path.Combine(SettingsDirectory, MatchAlert.App.SettingsSources.DevicesDirectoryName);
    public static string PendingDirectory => Path.Combine(DataDirectory, "pending");
    public static string LogFile => Path.Combine(DataDirectory, "log.txt");
}
