// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.App;

namespace MatchAlert.Tray;

/// <summary>
/// The current settings, reloaded whenever a file under the settings folder is saved. A broken file
/// never stops the app: the last good settings stay in use and <see cref="Error"/> says what is wrong.
/// </summary>
internal sealed class SettingsService : IDisposable
{
    private readonly FileLog _log;
    private readonly FileSystemWatcher _watcher;
    private readonly System.Threading.Timer _debounce;
    private readonly object _lock = new();

    public SettingsService(FileLog log)
    {
        _log = log;
        Directory.CreateDirectory(AppPaths.DevicesDirectory);
        if (!File.Exists(AppPaths.SettingsFile)) File.WriteAllText(AppPaths.SettingsFile, Resources.Strings.SettingsTemplate);   // in the UI language, so its comments can be read

        Current = SettingsLoader.Load(SettingsSources.BuiltIn());
        Reload();

        _debounce = new System.Threading.Timer(_ => Reload());
        _watcher = new FileSystemWatcher(AppPaths.SettingsDirectory, "*.json")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        FileSystemEventHandler changed = (_, _) => _debounce.Change(300, Timeout.Infinite);
        _watcher.Changed += changed;
        _watcher.Created += changed;
        _watcher.Deleted += changed;
        _watcher.Renamed += (_, _) => _debounce.Change(300, Timeout.Infinite);
        _watcher.EnableRaisingEvents = true;
    }

    public ResolvedSettings Current { get; private set; }

    /// <summary>What is wrong with the files on disk, or null when they are fine.</summary>
    public string? Error { get; private set; }

    public event Action? Changed;

    public void Reload()
    {
        lock (_lock)
        {
            try
            {
                Current = SettingsLoader.Load(SettingsSources.FromDisk(AppPaths.SettingsDirectory));
                if (Error is not null) _log.Write("Settings: fixed, reloaded");
                Error = null;
            }
            catch (SettingsException e)
            {
                Error = e.Message;
                _log.Write($"Settings: {e.Message} (keeping the previous settings)");
            }
            catch (IOException)
            {
                _debounce?.Change(500, Timeout.Infinite);   // an editor is still writing; try again shortly
                return;
            }
        }
        Changed?.Invoke();
    }

    /// <summary>Saves a profile the setup wizard made. The watcher picks it up.</summary>
    public string SaveProfile(DeviceProfile profile)
    {
        var path = Path.Combine(AppPaths.DevicesDirectory, profile.Id + ".json");
        File.WriteAllText(path, SettingsLoader.Serialize(profile));
        Reload();
        return path;
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _debounce.Dispose();
    }
}
