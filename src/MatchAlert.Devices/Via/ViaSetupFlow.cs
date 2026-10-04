// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text;
using MatchAlert.App;
using MatchAlert.Devices.Hid;
using MatchAlert.Devices.Setup;

namespace MatchAlert.Devices.Via;

/// <summary>
/// Builds a profile for a VIA keyboard no profile knows. Detects what can be detected (protocol,
/// whether channel 3 drives the lights, the post-effect reset) and asks only what cannot: which
/// effect numbers look steady and pulsing, because the enabled-effect list is not readable over VIA.
/// </summary>
internal sealed class ViaSetupFlow(IHidBus bus, HidDeviceInfo hid, ViaTiming timing) : ISetupFlow
{
    /// <summary>QMK's default order has solid at 1 and breathing at 2, so this usually ends after two questions.</summary>
    private static readonly byte[] Candidates = [1, 2, 0, .. Enumerable.Range(3, 38).Select(i => (byte)i)];

    private const byte ProbeHue = 85, ProbeSat = 255, ProbeSpeed = 170, ProbeBrightness = 200;

    public string DeviceName => string.IsNullOrWhiteSpace(hid.Product) ? $"USB device {hid.VendorId:X4}:{hid.ProductId:X4}" : hid.Product;

    public DeviceProfile? Run(IUserPrompt prompt)
    {
        ViaKeyboard kb;
        try
        {
            kb = new ViaKeyboard(bus.Open(hid), ViaOptions.RgbMatrixChannel, resetOnEffect: false, timing);
        }
        catch (IOException)
        {
            Unsupported(prompt, "It did not answer as a VIA keyboard.");
            return null;
        }

        using (kb)
        {
            // Only channel 3 is ever written. Searching other channels is not safe: on a Q1 HE a write to
            // channel 0 changed the effect and wedged the connection.
            if (kb.IsV3 && !kb.VerifyChannel())
            {
                Unsupported(prompt, "Its lights are not on the channel this app knows how to drive.");
                return null;
            }

            var before = kb.Snapshot();
            Dictionary<string, int>? effects;
            bool resetOnEffect;
            try
            {
                resetOnEffect = kb.ProbeResetOnEffect(before.Effect == 1 ? (byte)2 : (byte)1);
                kb.ResetOnEffect = resetOnEffect;
                effects = Interview(kb, prompt);
            }
            finally
            {
                kb.Restore(before);
            }
            if (effects is null) return null;

            var profile = new DeviceProfile
            {
                Id = Slug(DeviceName),
                Name = DeviceName,
                Driver = "via",
                Match = new DeviceMatch(hid.VendorId, [hid.ProductId], null),
                Effects = effects,
                Options = new Dictionary<string, System.Text.Json.JsonElement>
                {
                    ["via"] = new ViaOptions(ViaOptions.RgbMatrixChannel, resetOnEffect).ToJson(),
                },
            };
            var summary = $"{DeviceName} is ready.\n\n" +
                $"Steady effect: {effects["solid"]}\n" +
                (effects.TryGetValue("breathing", out int b) ? $"Pulsing effect: {b}\n" : "Pulsing effect: none found\n") +
                $"Needs the reset workaround: {(resetOnEffect ? "yes" : "no")}";
            return prompt.Choose(summary, ["Save", "Cancel"]) == 0 ? profile : null;
        }
    }

    private Dictionary<string, int>? Interview(ViaKeyboard kb, IUserPrompt prompt)
    {
        var found = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var n in Candidates)
        {
            if (found.Count == 2) break;
            kb.Apply(n, ProbeHue, ProbeSat, ProbeSpeed, ProbeBrightness);
            var answer = prompt.Choose(
                $"Look at {DeviceName}. How do its lights look now? (effect {n})",
                ["Steady green", "Pulsing", "Something else / off"]);
            switch (answer)
            {
                case null: return null;
                case 0: found.TryAdd("solid", n); break;
                case 1: found.TryAdd("breathing", n); break;
            }
        }
        if (found.ContainsKey("solid")) return found;
        prompt.Inform($"No steady effect was found on {DeviceName}, so it cannot show patterns.");
        return null;
    }

    private void Unsupported(IUserPrompt prompt, string why) => prompt.Inform(
        $"{DeviceName} is not supported yet. {why}\n\n" +
        "You can ask for it at github.com/penguinwokrs/lol-match-alert/issues with the details below.",
        details: $"{hid}\npath: {hid.Path}");

    internal static string Slug(string name)
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
