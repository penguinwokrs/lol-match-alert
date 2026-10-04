// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text.Json;
using MatchAlert.App;
using MatchAlert.Devices.Hid;
using MatchAlert.Domain;

namespace MatchAlert.Devices.Via;

internal sealed class ViaLightingDevice(ViaDriver driver, HidDeviceInfo hid, DeviceProfile profile, ViaOptions options) : ILightingDevice
{
    private ViaDriver Driver { get; } = driver;
    private HidDeviceInfo Hid { get; } = hid;
    private DeviceProfile Profile { get; } = profile;

    public string Id => Profile.Id;
    public string Name => Profile.Name;
    public int MinStepMs => Profile.MinStepMs;

    /// <summary>Stable across replugs (the path is not), and distinct per model.</summary>
    private string Key => $"via-{Profile.Id}-{Hid.VendorId:X4}-{Hid.ProductId:X4}";

    public ILightingSession OpenSession()
    {
        var kb = Driver.Open(Hid, options);
        try
        {
            var snapshot = Driver.CorrectBrightness(Key, kb.Snapshot());
            var session = new Session(this, kb, snapshot);
            session.SavePending();
            return session;
        }
        catch
        {
            kb.Dispose();
            throw;
        }
    }

    private sealed class Session(ViaLightingDevice device, ViaKeyboard kb, ViaSnapshot snapshot) : ILightingSession
    {
        private readonly List<ViaDriver.Shown> _shown = [];
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
                // Record first: if we crash mid-write, recovery must still recognise this as ours.
                var shown = new ViaDriver.Shown(effect, hsv.H, hsv.S);
                if (!_shown.Contains(shown))
                {
                    _shown.Add(shown);
                    SavePending();
                }

                if (_effect != effect)
                {
                    if (!kb.Apply(effect, hsv.H, hsv.S, step.Speed, brightness))
                        device.Driver.Log($"{device.Name}: color not confirmed after the effect change");
                    _effect = effect;
                }
                else
                {
                    kb.ShowColor(hsv.H, hsv.S, brightness, step.Speed);
                }
                _failed = false;
            }
            catch (IOException e)
            {
                // Counted as a miss; say so once, not every 300 ms.
                if (!_failed) device.Driver.Log($"{device.Name}: write failed: {e.Message}");
                _failed = true;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (!kb.Restore(snapshot)) device.Driver.Log($"{device.Name}: restore could not confirm the color");
                device.Driver.RememberBrightness(device.Key, snapshot.Brightness, kb.Get(ViaValue.Brightness, 1)[0]);
                device.Driver.Pending.Delete(device.Key);
            }
            catch (IOException e)
            {
                // Pending stays on disk: the next start tries again.
                device.Driver.Log($"{device.Name}: restore failed: {e.Message}");
            }
            finally
            {
                kb.Dispose();
            }
        }

        public void SavePending() => device.Driver.Pending.Save(new PendingEntry(
            device.Key, device.Driver.Id, device.Profile.Id, device.Hid.VendorId, device.Hid.ProductId,
            JsonSerializer.SerializeToElement(new ViaDriver.PendingState(snapshot, [.. _shown]), ViaDriver.PendingState.Json)));
    }
}
