// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text.Json;
using MatchAlert.App;
using MatchAlert.Devices.Hid;
using MatchAlert.Domain;

namespace MatchAlert.Devices;

/// <summary>An effect and color a session put on the keyboard, so crash recovery can tell ours from the user's.</summary>
public sealed record ShownColor(byte Effect, byte Hue, byte Sat);

/// <summary>
/// The protocol-specific half of a keyboard: what each driver implements. Everything a session does
/// with it - snapshot, playback, restore, crash recovery - is shared in <see cref="RestoringDevice{TLink,TSnapshot}"/>.
/// </summary>
public interface IKeyboardLink<TSnapshot> : IDisposable
{
    TSnapshot Snapshot();

    /// <summary>The effect, hue and sat on the keyboard right now.</summary>
    ShownColor Showing();

    /// <summary>Writes everything, effect included. False if the color could not be confirmed.</summary>
    bool Apply(byte effect, byte hue, byte sat, byte? speed, byte brightness);

    /// <summary>The playback path when the effect is not changing: as few, as quick writes as possible.</summary>
    void ShowColor(byte hue, byte sat, byte brightness, byte? speed);

    /// <summary>Puts a snapshot back. False if it could not be confirmed.</summary>
    bool Restore(TSnapshot snapshot);
}

/// <summary>What a pending file holds: the lighting to restore, and what was shown instead.</summary>
internal sealed record PendingState<TSnapshot>(TSnapshot Snapshot, List<ShownColor> Shown)
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}

/// <summary>
/// A keyboard that is put back the way it was after every alert. Opens the link per session, snapshots,
/// writes the effect only when it changes, keeps the snapshot on disk while it runs, and restores on
/// dispose. Write failures count as misses, so one unplugged keyboard never stops an alert.
/// </summary>
internal sealed class RestoringDevice<TLink, TSnapshot>(
    string driverId,
    HidDeviceInfo hid,
    DeviceProfile profile,
    PendingSnapshots pending,
    Action<string> log,
    Func<TLink> open,
    Func<TLink, TSnapshot>? snapshot = null,
    Action<TLink, TSnapshot>? afterRestore = null) : ILightingDevice
    where TLink : IKeyboardLink<TSnapshot>
{
    public string Id => profile.Id;
    public string Name => profile.Name;
    public int MinStepMs => profile.MinStepMs;

    /// <summary>Stable across replugs (the path is not), and distinct per model.</summary>
    public string Key => KeyFor(driverId, profile, hid);

    public static string KeyFor(string driverId, DeviceProfile profile, HidDeviceInfo hid) =>
        $"{driverId}-{profile.Id}-{hid.VendorId:X4}-{hid.ProductId:X4}";

    public ILightingSession OpenSession()
    {
        var link = open();
        try
        {
            var session = new Session(this, link, snapshot is null ? link.Snapshot() : snapshot(link));
            session.SavePending();
            return session;
        }
        catch
        {
            link.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Restores what an interrupted session left, but only if the keyboard still shows one of the colors
    /// it wrote. Anything else means the user has changed the lighting since (or replugged the board), and
    /// their lighting wins.
    /// </summary>
    public static void Recover(TLink link, PendingEntry entry, string name, Action<string> log)
    {
        var saved = entry.State.Deserialize<PendingState<TSnapshot>>(PendingState<TSnapshot>.Json)
            ?? throw new InvalidDataException("empty pending state");
        var now = link.Showing();
        if (saved.Shown.Any(s => s.Effect == now.Effect && Near(s.Hue, now.Hue, wraps: true) && Near(s.Sat, now.Sat, wraps: false)))
        {
            link.Restore(saved.Snapshot);
            log($"{name}: restored the lighting an interrupted alert left behind ({saved.Snapshot})");
        }
        else
        {
            log($"{name}: an interrupted alert's snapshot was discarded; the lighting has changed since ({now})");
        }
    }

    /// <summary>Within 2 steps: firmware that stores RGB rather than HSV hands back a hue or sat off by rounding.</summary>
    private static bool Near(byte a, byte b, bool wraps)
    {
        int d = Math.Abs(a - b);
        return (wraps ? Math.Min(d, 255 - d) : d) <= 2;
    }

    private sealed class Session(RestoringDevice<TLink, TSnapshot> device, TLink link, TSnapshot snapshot) : ILightingSession
    {
        private readonly List<ShownColor> _shown = [];
        private byte? _effect;
        private bool _failed;
        private bool _disposed;

        public void Show(Step step)
        {
            byte effect = (byte)device.Profile.Effects[step.Effect];
            var hsv = step.Color.ToHsv();
            byte brightness = step.DeviceBrightness;
            try
            {
                // Recorded before the write: if we crash mid-write, recovery must still know it as ours.
                var shown = new ShownColor(effect, hsv.H, hsv.S);
                if (!_shown.Contains(shown))
                {
                    _shown.Add(shown);
                    SavePending();
                }

                if (_effect != effect)
                {
                    if (!link.Apply(effect, hsv.H, hsv.S, step.Speed, brightness))
                        device.Log($"{device.Name}: color not confirmed after the effect change");
                    _effect = effect;
                }
                else
                {
                    link.ShowColor(hsv.H, hsv.S, brightness, step.Speed);
                }
                _failed = false;
            }
            catch (IOException e)
            {
                // Counted as a miss; said once, not every 300 ms.
                if (!_failed) device.Log($"{device.Name}: write failed: {e.Message}");
                _failed = true;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (!link.Restore(snapshot)) device.Log($"{device.Name}: restore could not confirm the color");
                device.AfterRestore?.Invoke(link, snapshot);
                device.Pending.Delete(device.Key);
            }
            catch (IOException e)
            {
                // The pending file stays: the next start tries again.
                device.Log($"{device.Name}: restore failed: {e.Message}");
            }
            finally
            {
                link.Dispose();
            }
        }

        public void SavePending() => device.Pending.Save(new PendingEntry(
            device.Key, device.DriverId, device.Profile.Id, device.Hid.VendorId, device.Hid.ProductId,
            JsonSerializer.SerializeToElement(new PendingState<TSnapshot>(snapshot, [.. _shown]), PendingState<TSnapshot>.Json)));
    }

    private string DriverId => driverId;
    private HidDeviceInfo Hid => hid;
    private DeviceProfile Profile => profile;
    private PendingSnapshots Pending => pending;
    private Action<string> Log => log;
    private Action<TLink, TSnapshot>? AfterRestore => afterRestore;
}

/// <summary>A device's lighting as named values, for <c>--test</c> to compare before and after.</summary>
public sealed record DeviceState(IReadOnlyList<DeviceState.Field> Fields)
{
    /// <param name="Tolerance">How far a value may come back off and still count as restored.</param>
    public sealed record Field(string Name, int Value, int Tolerance = 0);

    public bool Matches(DeviceState after) => Fields.All(f =>
        after.Fields.FirstOrDefault(a => a.Name == f.Name) is { } a && Math.Abs(a.Value - f.Value) <= f.Tolerance);

    public override string ToString() => string.Join(", ", Fields.Select(f => $"{f.Name} {f.Value}"));
}
