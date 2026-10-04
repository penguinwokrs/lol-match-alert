// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text;

namespace MatchAlert.Devices;

internal static class ProfileIds
{
    /// <summary>"Keychron Q1 HE 8K" becomes "keychron-q1-he-8k": a profile id people can type in settings.json.</summary>
    public static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-') is { Length: > 0 } s ? s : "keyboard";
    }
}
