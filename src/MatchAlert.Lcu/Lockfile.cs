// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Globalization;
using System.Text;

namespace MatchAlert.Lcu;

/// <summary>
/// The League client's <c>lockfile</c>: <c>name:pid:port:password:protocol</c>. The port and password
/// change on every client launch, so it is read again on every connect.
/// </summary>
public sealed record Lockfile(int Port, string Password)
{
    public string BasicAuth => Convert.ToBase64String(Encoding.ASCII.GetBytes($"riot:{Password}"));

    public static Lockfile Parse(string content)
    {
        var parts = content.Trim().Split(':');
        if (parts.Length < 5 || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int port)
            || port is < 1 or > 65535 || parts[3].Length == 0)
            throw new FormatException("The League client lockfile is not in the expected name:pid:port:password:protocol form.");
        return new Lockfile(port, parts[3]);
    }
}
