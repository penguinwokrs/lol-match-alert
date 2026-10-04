// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.App;
using MatchAlert.Devices;
using MatchAlert.Devices.Hid;
using MatchAlert.Devices.Setup;
using MatchAlert.Devices.Via;

namespace MatchAlert.Tests.Devices;

internal sealed class FakeHidBus : IHidBus
{
    private readonly List<(HidDeviceInfo Info, IRawHid? Board)> _devices = [];

    public static HidDeviceInfo Via(ushort vid, ushort pid, string product, string? path = null) =>
        new(path ?? $@"\\?\hid#vid_{vid:x4}&pid_{pid:x4}&col05", vid, pid, ViaKeyboard.UsagePage, ViaKeyboard.Usage, 33, 33, product);

    /// <summary>Pulsar boards answer on the raw HID page with 64-byte reports.</summary>
    public static HidDeviceInfo Pulsar(ushort pid, string product) =>
        new($@"\\?\hid#vid_3710&pid_{pid:x4}&mi_01", 0x3710, pid, 0xFF60, 0x61, 65, 65, product);

    /// <summary>The same keyboards' bootloader collection, which must never be opened.</summary>
    public static HidDeviceInfo PulsarBoot(ushort pid) =>
        new($@"\\?\hid#vid_3710&pid_{pid:x4}&mi_02", 0x3710, pid, 0xFF1C, 0x1C, 65, 65, "PULSAR BOOT");

    public static HidDeviceInfo KeyboardCollection(ushort vid, ushort pid, string product) =>
        new($@"\\?\hid#vid_{vid:x4}&pid_{pid:x4}&col01", vid, pid, 0x01, 0x06, 9, 2, product);

    public FakeHidBus Add(HidDeviceInfo info, IRawHid? board = null)
    {
        _devices.Add((info, board));
        return this;
    }

    public IReadOnlyList<HidDeviceInfo> Enumerate() => _devices.Select(d => d.Info).ToList();

    public IRawHid Open(HidDeviceInfo device) =>
        _devices.First(d => d.Info == device).Board ?? throw new IOException("not a raw HID device");
}

/// <summary>Answers prompts from a script and records what was said.</summary>
internal sealed class ScriptedPrompt(params int?[] answers) : IUserPrompt
{
    private readonly Queue<int?> _answers = new(answers);

    public List<string> Asked { get; } = [];
    public List<(string Message, string? Details)> Told { get; } = [];

    public int? Choose(string message, IReadOnlyList<string> choices)
    {
        Asked.Add(message);
        return _answers.Count > 0 ? _answers.Dequeue() : throw new InvalidOperationException($"unscripted question: {message}");
    }

    public void Inform(string message, string? details = null) => Told.Add((message, details));
}

internal sealed class DeviceTestbed : IDisposable
{
    public const ushort Keychron = 0x3434;
    public const ushort Q1Jis = 0x1012;
    public const ushort LinkKm = 0xD026;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lma-tests-" + Guid.NewGuid().ToString("N"));

    public DeviceTestbed(FakeHidBus bus, params SourceText[] userProfiles)
    {
        Bus = bus;
        Settings = SettingsLoader.Load(SettingsSources.BuiltIn() with { UserProfiles = userProfiles });
        Pending = new PendingSnapshots(Path.Combine(_dir, "pending"));
        Restart();
    }

    public FakeHidBus Bus { get; }
    public ResolvedSettings Settings { get; }
    public PendingSnapshots Pending { get; }
    public List<string> Log { get; } = [];
    public ViaDriver Driver { get; private set; } = null!;
    public MatchAlert.Devices.Pulsar.PulsarDriver Pulsar { get; private set; } = null!;
    public HidDeviceSource Source { get; private set; } = null!;

    /// <summary>A fresh driver and source over the same disk state, as after the app restarts.</summary>
    public void Restart()
    {
        Driver = new ViaDriver(Bus, Pending, Log.Add, ViaKeyboardTests.NoWait);
        Pulsar = new MatchAlert.Devices.Pulsar.PulsarDriver(Bus, Pending, Log.Add, new MatchAlert.Devices.Pulsar.PulsarTiming(1, 1));
        // Same order as Program.Devices: vendor drivers before VIA.
        Source = new HidDeviceSource(Bus, [Pulsar, Driver], () => Settings, Pending, Log.Add);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}
