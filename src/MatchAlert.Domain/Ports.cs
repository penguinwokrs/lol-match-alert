// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

namespace MatchAlert.Domain;

/// <summary>The game client, seen as a stream of states. Implementations reconnect on their own.</summary>
public interface IGameEvents
{
    IAsyncEnumerable<ClientState> WatchAsync(CancellationToken cancellationToken);
}

/// <summary>A keyboard (or anything else with lights) that can play steps.</summary>
public interface ILightingDevice
{
    /// <summary>The device profile id, which is also the key users write in settings.</summary>
    string Id { get; }

    string Name { get; }

    /// <summary>The shortest step this device shows cleanly; faster steps are stretched to it.</summary>
    int MinStepMs { get; }

    /// <summary>Takes hold of the device and remembers its current lighting.</summary>
    ILightingSession OpenSession();
}

/// <summary>Exclusive use of a device for one alert. Disposing puts the lighting back.</summary>
public interface ILightingSession : IDisposable
{
    void Show(Step step);
}

/// <summary>Finds the devices that can be lit right now.</summary>
public interface IDeviceSource
{
    IReadOnlyList<ILightingDevice> Discover();

    /// <summary>Puts back lighting left changed by a session that never finished, such as after a crash.</summary>
    void RecoverInterruptedSessions();
}
