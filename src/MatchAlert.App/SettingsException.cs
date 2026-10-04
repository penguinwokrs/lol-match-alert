// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

namespace MatchAlert.App;

/// <summary>
/// A settings or profile file that cannot be used. The message reads
/// <c>&lt;file&gt;: &lt;field path&gt;: &lt;what is wrong&gt;</c>, ready to show to the person who wrote it.
/// </summary>
public sealed class SettingsException(string message, Exception? inner = null) : Exception(message, inner);
