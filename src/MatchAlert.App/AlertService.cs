// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.Domain;

namespace MatchAlert.App;

public enum AlertStatus
{
    WaitingForClient,
    Connected,
    Alerting,
}

/// <summary>
/// The use case: light every enabled device while the client is in a ready check, and put the
/// lighting back the moment it is not - or after <see cref="ResolvedSettings.MaxAlert"/>, or when
/// the client goes away, or when this service stops. Whichever comes first.
/// </summary>
public sealed class AlertService(
    IGameEvents events,
    IDeviceSource devices,
    Func<ResolvedSettings> settings,
    TimeProvider time,
    Action<string> log)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _statusLock = new();
    private Alert? _alert;
    private bool _connected;

    public event Action<AlertStatus>? StatusChanged;

    public AlertStatus Status { get; private set; } = AlertStatus.WaitingForClient;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        StatusChanged?.Invoke(Status);
        try
        {
            await foreach (var state in events.WatchAsync(cancellationToken))
            {
                _connected = state.Connected;
                if (state.IsReadyCheck) await StartAsync("match found");
                else await StopAsync();
                RefreshStatus();
            }
        }
        finally
        {
            await StopAsync();
        }
    }

    /// <summary>Plays the alert for <paramref name="duration"/> whatever the client is doing.</summary>
    public async Task TestAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        if (!await StartAsync("test")) return;
        try { await Task.Delay(duration, time, cancellationToken); }
        catch (OperationCanceledException) { }
        finally { await StopAsync(); }
    }

    private async Task<bool> StartAsync(string reason)
    {
        await _gate.WaitAsync();
        try
        {
            if (_alert is not null) return false;
            var current = settings();
            var cts = new CancellationTokenSource(current.MaxAlert, time);
            var plays = new List<Task>();

            IReadOnlyList<ILightingDevice> found;
            try { found = devices.Discover(); }
            catch (Exception e)
            {
                // This alert is lost; the next one must not be. Never let it end RunAsync.
                log($"Could not list keyboards: {e.Message}");
                found = [];
            }

            foreach (var device in found)
            {
                var profile = current.Profiles.FirstOrDefault(p => p.Id == device.Id);
                if (profile is null || !current.IsEnabled(device.Id)) continue;

                ILightingSession session;
                try { session = device.OpenSession(); }
                catch (Exception e)
                {
                    log($"{device.Name}: could not open: {e.Message}");
                    continue;
                }
                var pattern = current.PatternFor(profile);
                log($"{device.Name}: {reason}, playing {current.PatternNameFor(profile)}");
                plays.Add(Task.Run(() => PlayThenRestoreAsync(device, session, pattern, cts.Token)));
            }

            var alert = new Alert(cts, Task.WhenAll(plays));
            _alert = alert;
            // The safety stop ends playback on its own; tidy up when it does.
            _ = alert.Completion.ContinueWith(_ => Finished(alert), TaskScheduler.Default);
            RefreshStatus();
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_alert is not { } alert) return;
            alert.Cancellation.Cancel();
            await alert.Completion;
            Finished(alert);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task PlayThenRestoreAsync(ILightingDevice device, ILightingSession session, Pattern pattern, CancellationToken ct)
    {
        try
        {
            await PatternPlayer.PlayAsync(session, pattern, device.MinStepMs, time, ct);
        }
        catch (Exception e)
        {
            log($"{device.Name}: playback failed: {e.Message}");
        }
        finally
        {
            try { session.Dispose(); }
            catch (Exception e) { log($"{device.Name}: restore failed: {e.Message}"); }
        }
    }

    private void Finished(Alert alert)
    {
        // The cancellation source is not disposed: a concurrent StopAsync may still call Cancel on
        // it, and its timer is harmless once the alert is gone.
        if (Interlocked.CompareExchange(ref _alert, null, alert) != alert) return;
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        AlertStatus next = _alert is not null ? AlertStatus.Alerting
            : _connected ? AlertStatus.Connected
            : AlertStatus.WaitingForClient;
        lock (_statusLock)
        {
            if (next == Status) return;
            Status = next;
        }
        StatusChanged?.Invoke(next);
    }

    private sealed record Alert(CancellationTokenSource Cancellation, Task Completion);
}
