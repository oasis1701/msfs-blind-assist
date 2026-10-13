namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The PMDG 737's annunciator flash filter. NG3 annunciators are SDK booleans that flip on every
/// blink and the SDK carries no flash flag for them, so a flashing light (IRS ALIGN with no position
/// entered) used to speak "on, off, on, off" without end. Per light:
/// <list type="bullet">
/// <item>"on" is spoken at once at the first lit edge;</item>
/// <item>an off edge is held, and spoken only once the light has stayed off for the hold time;</item>
/// <item>a light that re-lights during the hold <see cref="RelightsToFlashing"/> times is marked
/// flashing and spoken once as flashing; while flashing nothing more is spoken;</item>
/// <item>a flashing light that stays in one state for the hold time settles: steady (on) or off, and
/// the mark is cleared.</item>
/// </list>
/// Pure: time comes in as milliseconds, so the definition drives it from a timer and the tests drive
/// it by hand. The definition turns each <see cref="Callout"/> into speech.
/// </summary>
public sealed class Pmdg737FlashingLights
{
    /// <summary>The hold time for a light with no override. The real PMDG blink rate is not known
    /// (debug.log does not record annunciator changes), so this is a starting value to confirm in the
    /// sim; a light that blinks slower gets an override.</summary>
    public const int DefaultHoldMs = 2000;

    /// <summary>How many re-lights inside a hold mark a light as flashing.</summary>
    public const int RelightsToFlashing = 2;

    public enum Kind { On, Off, Flashing, Steady }

    public readonly record struct Callout(string Key, Kind Kind);

    private sealed class Light
    {
        public bool Lit;
        public long SinceMs;      // time of the last edge
        public bool Settled;      // the current state has lasted the hold time
        public int Relights;      // re-lights inside a hold since the light last settled
        public bool Flashing;
        public bool HeardLit;     // "on" (or "flashing") was spoken and no "off" since
    }

    private readonly Dictionary<string, Light> _lights = new();
    private readonly IReadOnlyDictionary<string, int> _holdOverridesMs;

    public Pmdg737FlashingLights(IReadOnlyDictionary<string, int>? holdOverridesMs = null)
    {
        _holdOverridesMs = holdOverridesMs ?? new Dictionary<string, int>();
    }

    /// <summary>The hold time for <paramref name="key"/>: its override, else <see cref="DefaultHoldMs"/>.</summary>
    public int HoldMsFor(string key) =>
        _holdOverridesMs.TryGetValue(key, out int ms) ? ms : DefaultHoldMs;

    /// <summary>Whether any light is waiting on its hold, so the caller keeps its timer running.</summary>
    public bool HasPending => _lights.Values.Any(l => !l.Settled);

    /// <summary>The lights marked flashing now, in the order they were first seen.</summary>
    public IReadOnlyList<string> FlashingKeys =>
        _lights.Where(kv => kv.Value.Flashing).Select(kv => kv.Key).ToList();

    /// <summary>
    /// A delivered value for <paramref name="key"/>. Returns what to speak now, or null. A light seen
    /// for the first time is taken to have been in the other state before: the delivery is a change,
    /// and the connect snapshot never reaches here.
    /// </summary>
    public Callout? OnValue(string key, bool lit, long nowMs)
    {
        if (!_lights.TryGetValue(key, out var light))
        {
            light = new Light { Lit = !lit, Settled = true, HeardLit = false };
            _lights[key] = light;
        }
        if (light.Lit == lit) return null;

        bool interruptedHold = !light.Settled;
        light.Lit = lit;
        light.SinceMs = nowMs;
        light.Settled = false;

        if (light.Flashing) return null;          // settles in Tick
        if (!lit) return null;                    // off is held

        if (interruptedHold)
        {
            light.Relights++;
            if (light.Relights >= RelightsToFlashing)
            {
                light.Flashing = true;
                light.HeardLit = true;
                return new Callout(key, Kind.Flashing);
            }
            // A re-light inside the hold reads as still on, unless "on" was never spoken.
            if (light.HeardLit) return null;
        }

        light.HeardLit = true;
        return new Callout(key, Kind.On);
    }

    /// <summary>Settles every light whose state has lasted its hold time; returns what to speak.</summary>
    public IReadOnlyList<Callout> Tick(long nowMs)
    {
        List<Callout>? said = null;
        foreach (var (key, light) in _lights)
        {
            if (light.Settled || nowMs - light.SinceMs < HoldMsFor(key)) continue;

            light.Settled = true;
            light.Relights = 0;
            if (light.Flashing)
            {
                light.Flashing = false;
                light.HeardLit = light.Lit;
                (said ??= new List<Callout>()).Add(new Callout(key, light.Lit ? Kind.Steady : Kind.Off));
            }
            else if (!light.Lit)
            {
                light.HeardLit = false;
                (said ??= new List<Callout>()).Add(new Callout(key, Kind.Off));
            }
            // A light that stayed on for the hold is simply confirmed: nothing to say.
        }
        return (IReadOnlyList<Callout>?)said ?? Array.Empty<Callout>();
    }

    /// <summary>Forgets every light (reconnect, flight load, aircraft switch).</summary>
    public void Reset() => _lights.Clear();

    /// <summary>
    /// Whether <paramref name="def"/> is a two-state annunciator lamp the filter owns: an announced,
    /// continuously monitored PMDG field whose only spoken values are its 0 and 1 descriptions (the
    /// definition's Annun, AnnunInverted and Door lamps). Derived from the definition, never a list.
    /// </summary>
    public static bool IsLamp(SimConnect.SimVarDefinition def) =>
        def.Type == SimConnect.SimVarType.PMDGVar
        && def.UpdateFrequency == SimConnect.UpdateFrequency.Continuous
        && def.IsAnnounced
        && def.OnlyAnnounceValueDescriptionMatches
        && def.ValueDescriptions.Count == 2
        && def.ValueDescriptions.ContainsKey(0)
        && def.ValueDescriptions.ContainsKey(1);

    /// <summary>The spoken sentence for <paramref name="callout"/>. On and off use the lamp's own
    /// descriptions (an inverted lamp lit reads "off", a door lit reads "open").</summary>
    public static string Phrase(Callout callout, SimConnect.SimVarDefinition def) => callout.Kind switch
    {
        Kind.On => $"{def.DisplayName}: {def.ValueDescriptions[1]}",
        Kind.Off => $"{def.DisplayName}: {def.ValueDescriptions[0]}",
        Kind.Flashing => $"{def.DisplayName}: flashing",
        _ => $"{def.DisplayName}: steady",
    };

    /// <summary>The rows of the Flashing lights list: one light per row, or the single row
    /// "No lights flashing".</summary>
    public static IReadOnlyList<string> ListRows(IEnumerable<string> flashingNames)
    {
        var names = flashingNames.ToList();
        return names.Count == 0 ? new[] { "No lights flashing" } : names;
    }
}
