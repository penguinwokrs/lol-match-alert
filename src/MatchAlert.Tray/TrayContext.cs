// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Diagnostics;
using MatchAlert.App;
using MatchAlert.Devices;
using MatchAlert.Devices.Hid;

namespace MatchAlert.Tray;

/// <summary>The tray icon and its menu. Everything else is wired up in <see cref="Program"/>.</summary>
internal sealed class TrayContext : ApplicationContext
{
    private const string Caption = "lol-match-alert";

    private readonly AlertService _alerts;
    private readonly HidDeviceSource _devices;
    private readonly SettingsService _settings;
    private readonly FileLog _log;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _run;
    private readonly Control _ui = new();
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _status = new() { Enabled = false };
    private readonly ToolStripMenuItem _settingsError = new() { Visible = false };
    private readonly ToolStripMenuItem _keyboards = new("Keyboards");
    private readonly ToolStripMenuItem _autostart = new("Start with Windows") { CheckOnClick = true };
    private bool _setupRunning;
    private bool _stopped;

    public TrayContext(AlertService alerts, HidDeviceSource devices, SettingsService settings, FileLog log)
    {
        _alerts = alerts;
        _devices = devices;
        _settings = settings;
        _log = log;
        _ = _ui.Handle;   // forces a window handle on this (the UI) thread, for BeginInvoke from other threads

        _keyboards.DropDownItems.Add(new ToolStripMenuItem("…"));   // filled when opened
        _keyboards.DropDownOpening += (_, _) => FillKeyboards();
        _autostart.Checked = Autostart.IsEnabled;
        _autostart.CheckedChanged += (_, _) => Autostart.Set(_autostart.Checked);
        _settingsError.Click += (_, _) => OpenSettingsFolder();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(
        [
            _status,
            _settingsError,
            new ToolStripSeparator(),
            _keyboards,
            new ToolStripMenuItem("Test lighting", null, (_, _) => TestLighting()),
            new ToolStripMenuItem("Set up a keyboard…", null, (_, _) => _ = SetUpKeyboardAsync()),
            new ToolStripMenuItem("Open settings folder", null, (_, _) => OpenSettingsFolder()),
            _autostart,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Exit", null, (_, _) => Exit()),
        ]);

        _icon = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) TestLighting(); };
        ShowStatus(_alerts.Status);

        _alerts.StatusChanged += s => _ui.BeginInvoke(() => ShowStatus(s));
        _settings.Changed += () => _ui.BeginInvoke(ShowSettingsError);
        Microsoft.Win32.SystemEvents.SessionEnding += (_, _) => Stop();
        ShowSettingsError();

        _run = Task.Run(() => _alerts.RunAsync(_stop.Token));
        _run.ContinueWith(t => _log.Write($"Alerting stopped unexpectedly: {t.Exception}"), TaskContinuationOptions.OnlyOnFaulted);
        OfferSetupIfNothingIsKnown();
    }

    private void ShowStatus(AlertStatus status)
    {
        var text = status switch
        {
            AlertStatus.Alerting => "Match found",
            AlertStatus.Connected => "Waiting for a match",
            _ => "Waiting for the League client",
        };
        _status.Text = text;
        _icon.Text = $"{Caption}: {text}";
        var old = _icon.Icon;
        _icon.Icon = TrayIcons.For(status);
        old?.Dispose();
    }

    private void ShowSettingsError()
    {
        _settingsError.Visible = _settings.Error is not null;
        if (_settings.Error is not { } error) return;
        _settingsError.Text = "Settings problem: click to open the folder";
        _settingsError.ToolTipText = error;
        _icon.ShowBalloonTip(10_000, "Settings not applied", error + "\nThe previous settings are still in use.", ToolTipIcon.Warning);
    }

    private void FillKeyboards()
    {
        _keyboards.DropDownItems.Clear();
        var found = Safely(_devices.Recognised, []);
        foreach (var (_, profile) in found)
        {
            var current = _settings.Current;
            var state = current.IsEnabled(profile.Id) ? current.PatternNameFor(profile) : "off";
            var item = new ToolStripMenuItem($"{profile.Name}  ({state})")
            {
                ToolTipText = $"id for settings.json: {profile.Id}\nclick to copy it",
            };
            var id = profile.Id;
            item.Click += (_, _) => Clipboard.SetText(id);
            _keyboards.DropDownItems.Add(item);
        }
        if (found.Count == 0) _keyboards.DropDownItems.Add(new ToolStripMenuItem("No set-up keyboard is connected") { Enabled = false });
    }

    private void TestLighting()
    {
        if (Safely(_devices.Recognised, []).Count == 0)
        {
            Inform("No set-up keyboard is connected. Connect it with a USB cable, or use \"Set up a keyboard…\".");
            return;
        }
        _ = _alerts.TestAsync(TimeSpan.FromSeconds(3), CancellationToken.None);
    }

    private async Task SetUpKeyboardAsync()
    {
        if (_setupRunning) return;
        if (_alerts.Status == AlertStatus.Alerting)
        {
            Inform("A match alert is playing. Try again once it is over.");
            return;
        }

        var candidates = Safely(_devices.Unrecognised, []);
        if (candidates.Count == 0)
        {
            Inform("There is no new keyboard to set up.\n\n" +
                "Keyboards already set up are under Keyboards. A keyboard must be connected by USB cable " +
                "(not wireless or Bluetooth) and support VIA.");
            return;
        }

        var prompt = new TaskDialogPrompt(_ui, "Set up a keyboard");
        var target = candidates.Count == 1 ? candidates[0]
            : prompt.Choose("Which device is the keyboard to set up?", candidates.Select(Describe).ToList()) is int i ? candidates[i] : null;
        if (target is null || _devices.SetupFor(target) is not { } flow) return;

        _setupRunning = true;
        try
        {
            _log.Write($"Setup: {flow.DeviceName}");
            var profile = await Task.Run(() => flow.Run(prompt));
            if (profile is null) return;
            var path = _settings.SaveProfile(profile);
            _log.Write($"Setup: saved {path}");
            Inform($"{profile.Name} is set up and will light up on the next match.\n\nUse \"Test lighting\" to see it now.");
        }
        catch (Exception e)
        {
            _log.Write($"Setup failed: {e}");
            Inform($"Setup stopped: {e.Message}");
        }
        finally
        {
            _setupRunning = false;
        }
    }

    private void OfferSetupIfNothingIsKnown()
    {
        if (Safely(_devices.Recognised, []).Count > 0 || Safely(_devices.Unrecognised, []).Count == 0) return;
        _icon.BalloonTipClicked += OnBalloon;
        _icon.ShowBalloonTip(10_000, "No keyboard set up yet", "Click here to set up your keyboard.", ToolTipIcon.Info);

        void OnBalloon(object? s, EventArgs e)
        {
            _icon.BalloonTipClicked -= OnBalloon;
            _ = SetUpKeyboardAsync();
        }
    }

    private static string Describe(HidDeviceInfo d) => $"{d.Product} (0x{d.VendorId:X4}:0x{d.ProductId:X4})";

    private static void OpenSettingsFolder() =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.SettingsDirectory}\"") { UseShellExecute = true });

    private static void Inform(string message) => TaskDialog.ShowDialog(new TaskDialogPage
    {
        Caption = Caption,
        Text = message,
        Icon = TaskDialogIcon.Information,
        SizeToContent = true,
    }, TaskDialogStartupLocation.CenterScreen);

    private T Safely<T>(Func<T> read, T fallback)
    {
        try { return read(); }
        catch (Exception e)
        {
            _log.Write($"Listing devices failed: {e.Message}");
            return fallback;
        }
    }

    /// <summary>Ends any alert and waits for its restore, so the keyboard is never left flashing.</summary>
    private void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        _stop.Cancel();
        try { _run.Wait(TimeSpan.FromSeconds(5)); }
        catch (AggregateException) { }
    }

    private void Exit()
    {
        Stop();
        _icon.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Stop();
            _icon.Dispose();
            _ui.Dispose();
            _settings.Dispose();
        }
        base.Dispose(disposing);
    }
}
