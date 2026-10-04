// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text.Json;

namespace MatchAlert.Devices;

/// <summary>A session that started and has not finished: what to restore, in the driver's own format.</summary>
public sealed record PendingEntry(string Key, string Driver, string ProfileId, ushort VendorId, ushort ProductId, JsonElement State);

/// <summary>
/// The lighting each running session must put back, kept on disk so a crash mid alert does not
/// leave the keyboard flashing until it is unplugged. Written before the first change, deleted
/// after the restore.
/// </summary>
public sealed class PendingSnapshots(string directory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public void Save(PendingEntry entry)
    {
        Directory.CreateDirectory(directory);
        var path = PathFor(entry.Key);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(entry, Json));
        File.Move(tmp, path, overwrite: true);
    }

    public void Delete(string key)
    {
        try { File.Delete(PathFor(key)); }
        catch (IOException) { }
    }

    public IReadOnlyList<PendingEntry> All()
    {
        if (!Directory.Exists(directory)) return [];
        var entries = new List<PendingEntry>();
        foreach (var file in Directory.GetFiles(directory, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<PendingEntry>(File.ReadAllText(file), Json) is { } e) entries.Add(e);
            }
            catch (Exception e) when (e is JsonException or IOException)
            {
                File.Delete(file);   // unreadable: nothing can be done with it
            }
        }
        return entries;
    }

    private string PathFor(string key) =>
        Path.Combine(directory, string.Concat(key.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_')) + ".json");
}
