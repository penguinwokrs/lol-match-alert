// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.Domain;

namespace MatchAlert.App;

/// <summary>
/// A pattern playing on one device while someone edits it. Each <see cref="Update"/> restarts the player on
/// the same open session - no restore in between, so editing never flickers back to the user's lighting.
/// Stopping, or a real match taking over, restores. Started and stopped through <see cref="AlertService"/>,
/// under the same lock as alerts, so the two never drive a device at once.
/// </summary>
public sealed class LivePreview
{
    private readonly AlertService _owner;
    private readonly ILightingSession _session;
    private readonly ILightingDevice _device;
    private readonly DeviceProfile? _profile;
    private readonly TimeProvider _time;
    private readonly Action<string> _log;
    private readonly object _lock = new();
    private CancellationTokenSource _playing = new();
    private Task _playback = Task.CompletedTask;
    private bool _ended;

    internal LivePreview(AlertService owner, ILightingSession session, ILightingDevice device, DeviceProfile? profile,
        Pattern pattern, TimeProvider time, Action<string> log)
    {
        _owner = owner;
        _session = session;
        _device = device;
        _profile = profile;
        _time = time;
        _log = log;
        Play(pattern);
    }

    /// <summary>The preview was ended by a real match (not by <see cref="StopAsync"/>). Raised on a background thread.</summary>
    public event Action? Ended;

    public ILightingDevice Device => _device;

    /// <summary>Plays <paramref name="pattern"/> from its start, on the session already open. Ignored once stopped.</summary>
    public void Update(Pattern pattern)
    {
        lock (_lock)
        {
            if (!_ended) Play(pattern);
        }
    }

    /// <summary>Ends the preview and puts the lighting back.</summary>
    public Task StopAsync() => _owner.StopPreviewAsync(this);

    private void Play(Pattern pattern)
    {
        _playing.Cancel();
        var playing = _playing = new CancellationTokenSource();
        var previous = _playback;
        var steady = Steady(pattern);
        // After the previous player has let go of the session: one writer at a time.
        _playback = Task.Run(async () =>
        {
            try { await previous; } catch { }
            try { await PatternPlayer.PlayAsync(_session, steady, _device.MinStepMs, _time, playing.Token); }
            catch (Exception e) { _log($"{_device.Name}: preview failed: {e.Message}"); }
        });
    }

    /// <summary>An effect this device lacks would not load for it; in a preview it just shows steady.</summary>
    private Pattern Steady(Pattern pattern) => _profile is null ? pattern : pattern with
    {
        Steps = pattern.Steps.Select(s => _profile.Effects.ContainsKey(s.Effect) ? s : s with { Effect = Step.Solid }).ToList(),
    };

    internal async Task EndAsync(bool takenOver)
    {
        lock (_lock)
        {
            if (_ended) return;
            _ended = true;
            _playing.Cancel();
        }
        try { await _playback; } catch { }
        try { _session.Dispose(); }
        catch (Exception e) { _log($"{_device.Name}: restore after preview failed: {e.Message}"); }
        if (takenOver) Ended?.Invoke();
    }
}
