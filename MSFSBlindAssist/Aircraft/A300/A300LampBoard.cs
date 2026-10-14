namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>Which bus's light power a cockpit lamp needs: the last factor of its emissive code.</summary>
public enum A300LightPower { None, Ac, Dc }

/// <summary>One light the board decides: who it is, what it is called, the Ctrl+M row that mutes it,
/// the variables it is made of, and the cockpit's rule for whether it is lit (null while an input that
/// rule needs is unread).</summary>
public sealed class A300BoardLamp
{
    public A300BoardLamp(string id, string name, string muteKey, IReadOnlyList<string> inputs, Func<Func<string, double?>, bool?> lit)
    {
        Id = id;
        Name = name;
        MuteKey = muteKey;
        Inputs = inputs;
        Lit = lit;
    }

    public string Id { get; }
    public string Name { get; }
    public string MuteKey { get; }
    public IReadOnlyList<string> Inputs { get; }
    internal Func<Func<string, double?>, bool?> Lit { get; }
}

/// <summary>A light that went on or off in the cockpit.</summary>
public readonly record struct A300BoardChange(A300BoardLamp Lamp, bool On);

/// <summary>
/// The A300's lights as the cockpit shows them, transcribed from the aircraft's emissive code
/// (package 1.0.11, <c>A300_Interior.behavior.xml</c>). A lamp's brightness there is
/// <c>(state OR annunciator test) × brightness × its bus's light power</c>:
/// <list type="bullet">
/// <item>the bus's light power is <c>INI_DC_LIGHTS_FAILURE</c> (battery on, or the AC emergency bus
/// powered) or <c>INI_AC_LIGHTS_FAILURE</c> (the AC essential bus powered), 1 = powered;</item>
/// <item>the brightness (<c>INI_GENERAL_LIGHT_MULTIPLIER</c>) is 1 or 10, dim or bright, never 0, so it
/// is not a power term;</item>
/// <item>the annunciator test term is left out: the test lights every lamp at once, and the fleet's
/// lamp speech does not read a light test lamp by lamp.</item>
/// </list>
/// The 55 fault lights (<see cref="A300FaultLights"/>, each with its bus) and the autobrake buttons'
/// lights (an armed segment while the level is its level and its DECEL light is dark, and the DECEL
/// light; both on AC light power) are decided here, so a fault clearing on a dark panel says nothing,
/// power coming on speaks only the lights that light, and power going off darkens the lit ones.
/// A lamp's first value with every input read is a silent baseline; an input still unread leaves it
/// undecided (an armed segment too waits for its DECEL input). Pure.
/// </summary>
public sealed class A300LampBoard
{
    public const string AcPowerKey = "A300_LIGHTS_AC_POWER";
    public const string AcPowerVar = "INI_AC_LIGHTS_FAILURE";
    public const string DcPowerKey = "A300_LIGHTS_DC_POWER";
    public const string DcPowerVar = "INI_DC_LIGHTS_FAILURE";

    /// <summary>Every light the board decides: the fault lights, the autobrake lights, and the lights read
    /// through their own generated rule (<see cref="A300PanelLamps"/>).</summary>
    public static readonly IReadOnlyList<A300BoardLamp> Lamps = FaultLamps().Concat(AutobrakeLamps()).Concat(RuleLamps()).ToArray();

    /// <summary>The keys of the lights a panel status box shows: the fault lights and the rule lights.</summary>
    public static readonly IReadOnlySet<string> StatusLampKeys = A300FaultLights.All.Select(l => l.Key)
        .Concat(A300PanelLamps.Resolved.Select(l => l.Lamp.Key)).ToHashSet(StringComparer.Ordinal);

    public static readonly IReadOnlyDictionary<string, A300BoardLamp> ById = Lamps.ToDictionary(l => l.Id, StringComparer.Ordinal);

    private static readonly ILookup<string, A300BoardLamp> ByInput =
        Lamps.SelectMany(l => l.Inputs.Select(i => (Input: i, Lamp: l))).ToLookup(p => p.Input, p => p.Lamp, StringComparer.Ordinal);

    /// <summary>Every variable a light is made of.</summary>
    public static readonly IReadOnlySet<string> InputKeys = ByInput.Select(g => g.Key).ToHashSet(StringComparer.Ordinal);

    /// <summary>The variables that change more than one light (the two light power flags, the autobrake
    /// level and its DECEL lights): their deliveries check each light's own Ctrl+M row.</summary>
    public static readonly IReadOnlySet<string> SharedInputKeys = ByInput.Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);

    private readonly Dictionary<string, double> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _lit = new(StringComparer.Ordinal);

    public static string? PowerKeyFor(A300LightPower power) => power switch
    {
        A300LightPower.Ac => AcPowerKey,
        A300LightPower.Dc => DcPowerKey,
        _ => null,
    };

    /// <summary>Whether the cockpit shows <paramref name="lamp"/> lit, from <paramref name="read"/>; null while
    /// an input its rule needs is unread.</summary>
    public static bool? IsLit(A300BoardLamp lamp, Func<string, double?> read) => lamp.Lit(read);

    public bool Handles(string key) => InputKeys.Contains(key);

    /// <summary>Whether this delivery turns a bus's light power on or off (a first value is a baseline,
    /// never a flip). Ask before <see cref="Update"/>.</summary>
    public bool PowerFlips(string key, double value) =>
        key is AcPowerKey or DcPowerKey && _values.TryGetValue(key, out var was) && On(was) != On(value);

    /// <summary>Takes one delivery and returns the lights it turned on or off.</summary>
    public IReadOnlyList<A300BoardChange> Update(string key, double value)
    {
        if (!InputKeys.Contains(key))
            return Array.Empty<A300BoardChange>();
        _values[key] = value;
        List<A300BoardChange>? changes = null;
        foreach (var lamp in ByInput[key])
        {
            if (lamp.Lit(Read) is not bool lit)
                continue;
            if (_lit.TryGetValue(lamp.Id, out var was) && was != lit)
                (changes ??= new List<A300BoardChange>()).Add(new A300BoardChange(lamp, lit));
            _lit[lamp.Id] = lit;
        }
        return changes ?? (IReadOnlyList<A300BoardChange>)Array.Empty<A300BoardChange>();
    }

    /// <summary>Records a value only where none has been read yet, and baselines the lights it completes,
    /// silently; true when the value was taken.</summary>
    public bool Seed(string key, double value)
    {
        if (!InputKeys.Contains(key) || _values.ContainsKey(key))
            return false;
        _values[key] = value;
        foreach (var lamp in ByInput[key])
            if (!_lit.ContainsKey(lamp.Id) && lamp.Lit(Read) is bool lit)
                _lit[lamp.Id] = lit;
        return true;
    }

    public void Reset()
    {
        _values.Clear();
        _lit.Clear();
    }

    private double? Read(string key) => _values.TryGetValue(key, out var v) ? v : null;

    private static bool On(double value) => value >= 0.5;

    private static IEnumerable<A300BoardLamp> FaultLamps() =>
        A300FaultLights.All.Select(light =>
        {
            string? power = PowerKeyFor(light.Power);
            var inputs = power == null ? new[] { light.Key } : new[] { light.Key, power };
            return new A300BoardLamp(light.Key, light.Name, light.Key, inputs, read =>
                read(light.Key) is not double state ? null
                : power == null ? On(state)
                : read(power) is double powered ? On(state) && On(powered) : null);
        });

    /// <summary>A light read through its generated rule: lit while its rule holds AND its bus's light power is
    /// on (a lamp with no power term, such as external power AVAIL, needs only its rule).</summary>
    private static IEnumerable<A300BoardLamp> RuleLamps() =>
        A300PanelLamps.Resolved.Select(lamp =>
        {
            string? power = PowerKeyFor(lamp.Power);
            var inputs = lamp.Rule.Inputs.Select(lamp.KeyFor).Distinct().ToList();
            if (power != null)
                inputs.Add(power);
            return new A300BoardLamp(lamp.Lamp.Key, lamp.Lamp.Name, lamp.Lamp.Key, inputs, read =>
                lamp.Rule.IsLit(input => read(lamp.KeyFor(input))) is not bool state ? null
                : power == null ? state
                : read(power) is double powered ? state && On(powered) : null);
        });

    private static IEnumerable<A300BoardLamp> AutobrakeLamps()
    {
        foreach (var button in A300Autobrake.ByButton.Values.OrderBy(b => b.Level))
        {
            // Undecided until its DECEL input is read too ([A300-23]): the segment is lit only while the DECEL one is
            // dark. A button's label may guess an unread DECEL as "Armed" (Describe), but a baseline must not.
            yield return new A300BoardLamp(button.ArmedLampId, button.Name + " armed light", A300Autobrake.LevelKey,
                new[] { A300Autobrake.LevelKey, button.DecelKey, AcPowerKey },
                read => read(button.DecelKey) is null ? null
                    : A300Autobrake.Describe(button, read(A300Autobrake.LevelKey), read(button.DecelKey), read(AcPowerKey)) is string state
                        ? state == "Armed" : null);
            yield return new A300BoardLamp(button.DecelKey, button.Name + " decel light", button.DecelKey,
                new[] { button.DecelKey, AcPowerKey },
                read => read(button.DecelKey) is double decel && read(AcPowerKey) is double powered ? On(decel) && On(powered) : null);
        }
    }
}
