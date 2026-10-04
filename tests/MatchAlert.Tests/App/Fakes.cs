// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Collections.Concurrent;
using System.Threading.Channels;
using MatchAlert.Domain;

namespace MatchAlert.Tests.App;

internal sealed class FakeGameEvents : IGameEvents
{
    private readonly Channel<ClientState> _states = Channel.CreateUnbounded<ClientState>();

    public void Push(ClientState state) => _states.Writer.TryWrite(state);
    public void Push(string phase) => Push(new ClientState(true, phase));
    public void End() => _states.Writer.TryComplete();

    public async IAsyncEnumerable<ClientState> WatchAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var s in _states.Reader.ReadAllAsync(ct)) yield return s;
    }
}

internal sealed class FakeSession : ILightingSession
{
    public ConcurrentQueue<Step> Shown { get; } = new();
    public bool Disposed { get; private set; }
    public void Show(Step step) => Shown.Enqueue(step);
    public void Dispose() => Disposed = true;
}

internal sealed class FakeDevice(string id, int minStepMs = 0, bool fails = false) : ILightingDevice
{
    public string Id => id;
    public string Name => id;
    public int MinStepMs => minStepMs;
    public List<FakeSession> Sessions { get; } = [];
    public FakeSession? Last => Sessions.LastOrDefault();

    public ILightingSession OpenSession()
    {
        if (fails) throw new IOException("unplugged");
        var s = new FakeSession();
        lock (Sessions) Sessions.Add(s);
        return s;
    }
}

internal sealed class FakeDeviceSource(params ILightingDevice[] devices) : IDeviceSource
{
    public int Recovered { get; private set; }
    public IReadOnlyList<ILightingDevice> Discover() => devices;
    public void RecoverInterruptedSessions() => Recovered++;
}

/// <summary>
/// A fake clock that counts the timers created on it, so a test can wait for code under test to
/// start waiting before moving time on. Advancing first would be lost: the timer is not there yet.
/// </summary>
internal sealed class CountingTimeProvider : Microsoft.Extensions.Time.Testing.FakeTimeProvider
{
    private int _timers;
    public int Timers => Volatile.Read(ref _timers);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Interlocked.Increment(ref _timers);
        return base.CreateTimer(callback, state, dueTime, period);
    }
}

internal static class Eventually
{
    /// <summary>Waits for work that background tasks finish on their own schedule.</summary>
    public static async Task True(Func<bool> condition, string because = "")
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail($"timed out waiting: {because}");
            await Task.Delay(5);
        }
    }
}
