// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text.Json;

namespace MatchAlert.Lcu;

/// <summary>
/// The client's websocket speaks WAMP 1.0: every message is a JSON array whose first item is an
/// opcode. 5 subscribes; 8 is an event, <c>[8, "&lt;event&gt;", {"data": ..., "eventType": ..., "uri": ...}]</c>.
/// </summary>
public static class LcuFrames
{
    public const string PhaseEvent = "OnJsonApiEvent_lol-gameflow_v1_gameflow-phase";
    public const string PhasePath = "/lol-gameflow/v1/gameflow-phase";
    public const string Subscribe = $"[5,\"{PhaseEvent}\"]";

    private const int EventOpcode = 8;

    public static bool TryParsePhase(string frame, out string phase)
    {
        phase = "";
        try
        {
            using var doc = JsonDocument.Parse(frame);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 3) return false;
            if (root[0].ValueKind != JsonValueKind.Number || root[0].GetInt32() != EventOpcode) return false;
            if (root[1].ValueKind != JsonValueKind.String || root[1].GetString() != PhaseEvent) return false;
            if (root[2].ValueKind != JsonValueKind.Object || !root[2].TryGetProperty("data", out var data)) return false;
            if (data.ValueKind != JsonValueKind.String) return false;
            phase = data.GetString()!;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
