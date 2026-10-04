// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Runtime.InteropServices;
using MatchAlert.App;
using MatchAlert.Devices;
using MatchAlert.Devices.Hid;
using MatchAlert.Devices.Logitech;
using MatchAlert.Devices.OpenRgb;
using MatchAlert.Devices.Razer;
using MatchAlert.Devices.Pulsar;
using MatchAlert.Devices.Sayo;
using MatchAlert.Devices.Via;
using MatchAlert.Lcu;

namespace MatchAlert.Tray;

/// <summary>The composition root: the only place that knows every concrete type.</summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--test", StringComparer.OrdinalIgnoreCase))
        {
            if (!Console.IsOutputRedirected) AttachConsole(-1);   // a GUI exe has no console of its own
            return TestCommand.Run(args, new FileLog(AppPaths.LogFile, Console.Out));
        }

        using var single = new Mutex(true, @"Local\lol-match-alert", out bool first);
        if (!first) return 0;

        var log = new FileLog(AppPaths.LogFile);
        log.Write($"Starting {Application.ProductVersion}");
        ApplyLanguage(log);
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => log.Write($"Unexpected error: {e.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => log.Write($"Fatal error: {e.ExceptionObject}");

        using var settings = new SettingsService(log);
        var (hid, _) = Devices(settings, log);
        // Keyboards driven directly, and devices reached through their maker's own software.
        var devices = new DeviceSources(
            [hid, GHub(() => settings.Current, log), Chroma(() => settings.Current, log), new OpenRgbSource(() => settings.Current, log.Write)],
            log.Write);
        try { devices.RecoverInterruptedSessions(); }
        catch (Exception e) { log.Write($"Recovery failed: {e.Message}"); }

        var alerts = new AlertService(
            new LcuGameEvents(LcuGameEvents.FindRunningClientLockfile, log.Write),
            devices, () => settings.Current, TimeProvider.System, log.Write);
        alerts.StatusChanged += s => log.Write($"Status: {s}");

        using var tray = new TrayContext(alerts, devices, hid, settings, log);
        Application.Run(tray);
        log.Write("Exited");
        return 0;
    }

    internal static (HidDeviceSource Source, ViaDriver Via) Devices(SettingsService settings, FileLog log) =>
        Devices(() => settings.Current, log);

    internal static (HidDeviceSource Source, ViaDriver Via) Devices(Func<ResolvedSettings> settings, FileLog log)
    {
        var bus = new WindowsHidBus();
        var pending = new PendingSnapshots(AppPaths.PendingDirectory);
        var via = new ViaDriver(bus, pending, log.Write);
        // Another protocol is one more driver in this list. Order matters for setup only: the first
        // driver that can talk to an unknown keyboard sets it up, so vendor-specific drivers go before VIA.
        var pulsar = new PulsarDriver(bus, pending, log.Write);
        var sayo = new SayoDriver(bus, pending, log.Write);
        return (new HidDeviceSource(bus, [pulsar, sayo, via], settings, pending, log.Write), via);
    }

    /// <summary>
    /// Before anything shows text: the menu, the dialogs and the first-run settings template all read
    /// the UI culture once. "auto" leaves it at the Windows display language; a language with no
    /// translation falls back to English.
    /// </summary>
    private static void ApplyLanguage(FileLog log)
    {
        string language;
        try { language = SettingsLoader.Load(SettingsSources.FromDisk(AppPaths.SettingsDirectory)).Language; }
        catch (Exception e) when (e is SettingsException or IOException) { language = "auto"; }
        if (language == "auto") return;
        var culture = System.Globalization.CultureInfo.GetCultureInfo(language);
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
        System.Globalization.CultureInfo.CurrentUICulture = culture;
        log.Write($"Language: {language}");
    }

    /// <summary>Logitech G through G HUB's LED SDK, loaded fresh per alert so starting G HUB later just works.</summary>
    internal static LogitechGHubSource GHub(Func<ResolvedSettings> settings, FileLog log)
    {
        string? lastReason = null;
        return new LogitechGHubSource(() => GHubLogiLed.FindLibrary() is not null, () =>
        {
            var sdk = GHubLogiLed.TryLoad(out var reason);
            if (reason != lastReason) log.Write($"Logitech G HUB: {reason}");   // once per change, not per alert
            lastReason = reason;
            return sdk;
        }, settings, log.Write);
    }

    /// <summary>Razer through Synapse's Chroma SDK, loaded fresh per alert like G HUB.</summary>
    internal static RazerChromaSource Chroma(Func<ResolvedSettings> settings, FileLog log)
    {
        string? lastReason = null;
        return new RazerChromaSource(() => ChromaNative.FindLibrary() is not null, () =>
        {
            var sdk = ChromaNative.TryLoad(out var reason);
            if (reason != lastReason) log.Write($"Razer Chroma: {reason}");
            lastReason = reason;
            return sdk;
        }, settings, log.Write);
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
}
