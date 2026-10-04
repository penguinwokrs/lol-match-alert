// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Buffers.Binary;
using MatchAlert.App;
using MatchAlert.Domain;

namespace MatchAlert.Devices.Razer;

/// <summary>
/// Every Razer Chroma device, as one device, through Razer's Chroma SDK - the way Razer intends apps to light
/// its hardware. Synapse owns the devices: this app shows a static color on every device category, and when
/// it releases the SDK, Synapse goes back to the user's own lighting. Nothing is written to a device.
/// <para>
/// Razer's header marks breathing, blinking, wave, reactive and spectrum cycling as deprecated, so only the
/// static effect is used: breathing steps show steady.
/// </para>
/// </summary>
/// <param name="installed">Whether Synapse's library is there at all, checked on every listing.</param>
public sealed class RazerChromaSource(Func<bool> installed, Func<IChromaSdk?> load, Func<ResolvedSettings> settings, Action<string> log) : IDeviceSource
{
    public const string ProfileId = "razer-chroma";

    /// <summary>CHROMA_STATIC in each category's own EFFECT_TYPE enum (RzChromaSDKTypes.h); they differ.</summary>
    internal static readonly IReadOnlyDictionary<ChromaCategory, int> StaticEffect = new Dictionary<ChromaCategory, int>
    {
        [ChromaCategory.Keyboard] = 4,
        [ChromaCategory.Mouse] = 6,
        [ChromaCategory.Headset] = 1,
        [ChromaCategory.Mousepad] = 4,
        [ChromaCategory.Keypad] = 5,
        [ChromaCategory.ChromaLink] = 2,
    };

    /// <summary>The mouse's STATIC_EFFECT_TYPE names an LED first: RZLED_ALL.</summary>
    private const int AllMouseLeds = 0xFFFF;

    private Func<IChromaSdk?> Load => load;
    private Action<string> Log => log;

    public IReadOnlyList<ILightingDevice> Discover()
    {
        if (!installed()) return [];
        var current = settings();
        if (current.Profiles.FirstOrDefault(p => p.Id == ProfileId) is not { } profile || !current.IsEnabled(ProfileId)) return [];
        return [new Device(this, profile)];
    }

    /// <summary>Nothing to do: when this app stops, its SDK session ends and Synapse takes the lighting back.</summary>
    public void RecoverInterruptedSessions() { }

    /// <summary>COLORREF: 0x00BBGGRR.</summary>
    internal static uint ColorRef(byte r, byte g, byte b) => (uint)(b << 16 | g << 8 | r);

    /// <summary>The STATIC_EFFECT_TYPE struct for a category: the color, after an LED id for mice.</summary>
    internal static byte[] StaticParam(ChromaCategory category, uint colorRef)
    {
        var p = new byte[category == ChromaCategory.Mouse ? 8 : 4];
        if (category == ChromaCategory.Mouse) BinaryPrimitives.WriteInt32LittleEndian(p, AllMouseLeds);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(p.Length - 4), colorRef);
        return p;
    }

    private sealed class Device(RazerChromaSource source, DeviceProfile profile) : ILightingDevice
    {
        public string Id => profile.Id;
        public string Name => profile.Name;
        public int MinStepMs => profile.MinStepMs;

        public ILightingSession OpenSession()
        {
            var sdk = source.Load() ?? throw new IOException("Razer Synapse's Chroma SDK is not available (is Synapse installed?)");
            int result = sdk.Init();
            if (result != 0)
            {
                sdk.Dispose();
                throw new IOException($"Razer Chroma did not start (error {result}: is Synapse running, with Chroma apps allowed?)");
            }
            return new Session(sdk, source.Log, Name);
        }
    }

    private sealed class Session(IChromaSdk sdk, Action<string> log, string name) : ILightingSession
    {
        private readonly List<Guid> _effects = [];
        private bool _disposed;

        public void Show(Step step)
        {
            byte Scale(byte c) => (byte)Math.Round(c * step.Brightness / 100.0, MidpointRounding.AwayFromZero);
            uint color = ColorRef(Scale(step.Color.R), Scale(step.Color.G), Scale(step.Color.B));

            var previous = _effects.ToList();
            _effects.Clear();
            int shown = 0;
            foreach (var (category, effect) in StaticEffect)
            {
                // A category with no device connected just fails; that is not an error here.
                if (sdk.CreateEffect(category, effect, StaticParam(category, color), out var id) != 0) continue;
                _effects.Add(id);
                if (sdk.SetEffect(id) == 0) shown++;
            }
            foreach (var id in previous) sdk.DeleteEffect(id);
            if (shown == 0) log($"{name}: Chroma accepted no effect (no Razer device connected?)");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                foreach (var id in _effects) sdk.DeleteEffect(id);
                sdk.UnInit();
            }
            finally
            {
                sdk.Dispose();
            }
        }
    }
}
