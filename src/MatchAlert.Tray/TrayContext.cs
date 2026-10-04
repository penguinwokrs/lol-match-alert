// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Diagnostics;
using MatchAlert.App;
using MatchAlert.Devices;
using MatchAlert.Devices.Hid;
using MatchAlert.Domain;
using MatchAlert.Tray.Resources;

namespace MatchAlert.Tray;

/// <summary>The tray icon and its menu. Everything else is wired up in <see cref="Program"/>.</summary>
internal sealed class TrayContext : ApplicationContext
{
    private const string Caption = "lol-match-alert";

    private readonly AlertService _alerts;
    private readonly IDeviceSource _devices;
    private readonly HidDeviceSource _hid;
    private readonly SettingsService _settings;
    private readonly FileLog _log;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _run;
    private readonly Control _ui = new();
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _status = new() { Enabled = false };
    private readonly ToolStripMenuItem _settingsError = new() { Visible = false };
    private readonly ToolStripMenuItem _keyboards = new(Strings.Menu_Keyboards);
    private readonly ToolStripMenuItem _autostart = new(Strings.Menu_Autostart) { CheckOnClick = true };
    private bool _setupRunning;
    private bool _stopped;

    public TrayContext(AlertService alerts, IDeviceSource devices, HidDeviceSource hid, SettingsService settings, FileLog log)
    {
        _alerts = alerts;
        _devices = devices;
        _hid = hid;
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
            new ToolStripMenuItem(Strings.Menu_Test, null, (_, _) => TestLighting()),
            new ToolStripMenuItem(Strings.Menu_Setup, null, (_, _) => _ = SetUpKeyboardAsync()),
            new ToolStripMenuItem(Strings.Menu_OpenSettings, null, (_, _) => OpenSettingsFolder()),
            _autostart,
            new ToolStripSeparator(),
            new ToolStripMenuItem(Strings.Menu_Exit, null, (_, _) => Exit()),
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
            AlertStatus.Alerting => Strings.Status_Alerting,
            AlertStatus.Connected => Strings.Status_Connected,
            _ => Strings.Status_WaitingForClient,
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
        _settingsError.Text = Strings.Menu_SettingsProblem;
        _settingsError.ToolTipText = error;
        _icon.ShowBalloonTip(10_000, Strings.Balloon_SettingsTitle, string.Format(Strings.Balloon_SettingsText, error), ToolTipIcon.Warning);
    }

    private void FillKeyboards()
    {
        _keyboards.DropDownItems.Clear();
        var found = Safely(_devices.Discover, []);
        foreach (var device in found)
        {
            if (_settings.Current.Profiles.FirstOrDefault(p => p.Id == device.Id) is not { } profile) continue;
            var current = _settings.Current;
            var state = current.IsEnabled(profile.Id) ? current.PatternNameFor(profile) : Strings.Keyboards_Off;
            var item = new ToolStripMenuItem($"{profile.Name}  ({state})")
            {
                ToolTipText = string.Format(Strings.Keyboards_ItemTooltip, profile.Id),
            };
            var id = profile.Id;
            item.Click += (_, _) => Clipboard.SetText(id);
            _keyboards.DropDownItems.Add(item);
        }
        if (found.Count == 0) _keyboards.DropDownItems.Add(new ToolStripMenuItem(Strings.Keyboards_None) { Enabled = false });
    }

    private void TestLighting()
    {
        if (Safely(_devices.Discover, []).Count == 0)
        {
            Inform(Strings.Test_NoKeyboard);
            return;
        }
        _ = _alerts.TestAsync(TimeSpan.FromSeconds(3), CancellationToken.None);
    }

    private async Task SetUpKeyboardAsync()
    {
        if (_setupRunning) return;
        if (_alerts.Status == AlertStatus.Alerting)
        {
            Inform(Strings.Setup_AlertPlaying);
            return;
        }

        var candidates = Safely(_hid.Unrecognised, []);
        if (candidates.Count == 0)
        {
            Inform(Strings.Setup_NothingNew);
            return;
        }

        var prompt = new TaskDialogPrompt(_ui, Strings.Setup_Caption);
        var target = candidates.Count == 1 ? candidates[0]
            : prompt.Choose(Strings.Setup_WhichDevice, candidates.Select(Describe).ToList()) is int i ? candidates[i] : null;
        if (target is null || _hid.SetupFor(target) is not { } flow) return;

        _setupRunning = true;
        try
        {
            _log.Write($"Setup: {flow.DeviceName}");
            var profile = await Task.Run(() => flow.Run(prompt));
            if (profile is null) return;
            var path = _settings.SaveProfile(profile);
            _log.Write($"Setup: saved {path}");
            Inform(string.Format(Strings.Setup_Done, profile.Name));
        }
        catch (Exception e)
        {
            _log.Write($"Setup failed: {e}");
            Inform(string.Format(Strings.Setup_Stopped, e.Message));
        }
        finally
        {
            _setupRunning = false;
        }
    }

    private void OfferSetupIfNothingIsKnown()
    {
        if (Safely(_hid.Recognised, []).Count > 0 || Safely(_hid.Unrecognised, []).Count == 0) return;
        _icon.BalloonTipClicked += OnBalloon;
        _icon.ShowBalloonTip(10_000, Strings.Balloon_NoKeyboardTitle, Strings.Balloon_NoKeyboardText, ToolTipIcon.Info);

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
