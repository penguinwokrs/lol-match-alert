// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using MatchAlert.App;

namespace MatchAlert.Devices.Setup;

/// <summary>How a setup flow talks to the person at the keyboard. The tray shows dialogs; tests script answers.</summary>
public interface IUserPrompt
{
    /// <summary>The index of the choice made, or null if the person cancelled.</summary>
    int? Choose(string message, IReadOnlyList<string> choices);

    /// <summary>A message to acknowledge. <paramref name="details"/>, when given, is offered for copying.</summary>
    void Inform(string message, string? details = null);
}

/// <summary>Works out a profile for a keyboard no profile describes yet.</summary>
public interface ISetupFlow
{
    /// <summary>The keyboard being set up, as the person would recognise it.</summary>
    string DeviceName { get; }

    /// <summary>The new profile, or null if it was cancelled or the keyboard cannot be supported yet.</summary>
    DeviceProfile? Run(IUserPrompt prompt);
}
