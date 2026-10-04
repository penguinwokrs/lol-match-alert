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
    private const string Template = """
        {
          // Which pattern plays when a match is found. Built in:
          //   "match-found"  red and white, swapping every 300 ms
          //   "pulse"        gold, breathing
          //   "steady"       solid gold
          // or the name of one of your own below.
          "pattern": "match-found",

          // Your own patterns. color is #RRGGBB (#000000 is off), brightness is 0-100,
          // durationMs is how long each step shows. effect is "solid" (default) or "breathing".
          "patterns": {
            // "blue-blink": {
            //   "steps": [
            //     { "color": "#0080FF", "durationMs": 250 },
            //     { "color": "#000000", "durationMs": 250 }
            //   ]
            // }
          },

          // Per keyboard: "pattern" overrides the one above, "enabled": false leaves it alone.
          // The ids are in the tray menu under Keyboards.
          // "devices": { "keychron-q1-he-8k": { "pattern": "pulse" } },

          // Safety stop, in seconds, in case the client never leaves the ready check.
          "maxAlertSeconds": 30
        }

        """;

    private readonly FileLog _log;
    private readonly FileSystemWatcher _watcher;
    private readonly System.Threading.Timer _debounce;
    private readonly object _lock = new();

    public SettingsService(FileLog log)
    {
        _log = log;
        Directory.CreateDirectory(AppPaths.DevicesDirectory);
        if (!File.Exists(AppPaths.SettingsFile)) File.WriteAllText(AppPaths.SettingsFile, Template);

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
