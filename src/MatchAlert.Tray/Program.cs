// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Runtime.InteropServices;
using MatchAlert.App;
using MatchAlert.Devices;
using MatchAlert.Devices.Hid;
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
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => log.Write($"Unexpected error: {e.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => log.Write($"Fatal error: {e.ExceptionObject}");

        using var settings = new SettingsService(log);
        var (devices, _) = Devices(settings, log);
        try { devices.RecoverInterruptedSessions(); }
        catch (Exception e) { log.Write($"Recovery failed: {e.Message}"); }

        var alerts = new AlertService(
            new LcuGameEvents(LcuGameEvents.FindRunningClientLockfile, log.Write),
            devices, () => settings.Current, TimeProvider.System, log.Write);
        alerts.StatusChanged += s => log.Write($"Status: {s}");

        using var tray = new TrayContext(alerts, devices, settings, log);
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
        // Another protocol is one more driver in this list.
        return (new HidDeviceSource(bus, [via], settings, pending, log.Write), via);
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
}
