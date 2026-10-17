namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>One light the A300 speaks. A master light speaks only as it comes on; a light with
/// <paramref name="SpeaksOff"/> (<see cref="A300FaultLights"/>) speaks both ways, as the cockpit shows it:
/// lit only while its bus's <paramref name="Power"/> is on (<see cref="A300LampBoard"/>).</summary>
public sealed record A300Lamp(string Key, string Var, string Name, string Panel, bool SpeaksOff = false,
    A300LightPower Power = A300LightPower.None, bool Flashes = false);

/// <summary>
/// What the A300 says on its own about its master lights and levers: the two master lights when they
/// come on, and the levers when something other than the pilot's own pick moves them (a hardware
/// lever, the cockpit itself). Each phrase is composed here; <see cref="A300AnnouncementTracker"/>
/// decides when. The fault and warning lights (<see cref="A300FaultLights"/>) speak both ways, but
/// <see cref="A300LampBoard"/> decides them and <see cref="A300LampSpeech"/> speaks them, not this class.
///
/// The master lights speak only as they COME ON. Pressing the light writes 0 to the very variable
/// read here (the cockpit's own click code), so speaking the light going out would announce the
/// pilot's own button press back to them.
/// </summary>
public static class A300Announcements
{
    public const string MasterWarningKey = "A300_LAMP_MASTER_WARNING";
    public const string MasterCautionKey = "A300_LAMP_MASTER_CAUTION";

    /// <summary>The map row keys of the two levers the generated map drives.</summary>
    public const string GearLeverKey = "A300_GEAR_LEVER";
    public const string ParkingBrakeKey = "A300_PARKINGBRAKE";

    private static readonly A300Lamp[] Masters =
    {
        new A300Lamp(MasterWarningKey, "INI_MASTER_WARNING_ACTIVE", "Master warning", "Warnings"),
        new A300Lamp(MasterCautionKey, "INI_MASTER_CAUTION_ACTIVE", "Master caution", "Warnings"),
    };

    /// <summary>Every light the A300 registers: the two master lights, which this class speaks, then the fault and
    /// warning lights, which the light board decides.</summary>
    public static readonly IReadOnlyList<A300Lamp> Lamps = Masters.Concat(A300FaultLights.All).ToArray();

    private static readonly Dictionary<string, A300Lamp> MasterByKey = Masters.ToDictionary(l => l.Key, StringComparer.Ordinal);

    /// <summary>The SAS control panel's pitch trim and yaw damper levers, which the aircraft drops by itself
    /// (a pitch trim lever picked on with no hydraulics engaged, then dropped 2.1 s later, 2026-10-10).
    /// The autothrottle levers are spoken by <see cref="A300EngagementTracker"/>.</summary>
    public static readonly IReadOnlyDictionary<string, string> SasLevers = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["A300_PITCH_TRIM_1"] = "Pitch trim 1",
        ["A300_PITCH_TRIM_2"] = "Pitch trim 2",
        ["A300_YAW_DAMPER_1"] = "Yaw damper 1",
        ["A300_YAW_DAMPER_2"] = "Yaw damper 2",
    };

    /// <summary>Every key this class speaks: the two master lights, the four levers and the SAS levers.</summary>
    public static readonly IReadOnlySet<string> AnnouncedKeys = Masters.Select(l => l.Key)
        .Concat(new[] { A300Levers.FlapsKey, A300Levers.SpoilersArmKey, GearLeverKey, ParkingBrakeKey })
        .Concat(SasLevers.Keys)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>The phrase a key's value speaks, or null when that value says nothing (a master light
    /// going out, a flap handle between detents).</summary>
    public static string? Phrase(string key, double value)
    {
        if (MasterByKey.TryGetValue(key, out var master))
            return value >= 0.5 ? master.Name : null;
        return LeverPhrase(key, value);
    }

    private static string? LeverPhrase(string key, double value) => key switch
    {
        A300Levers.FlapsKey => A300Levers.FlapPositions.TryGetValue(Math.Round(value), out var detent)
            && Math.Abs(value - Math.Round(value)) < 0.01
                ? $"Flaps {detent.ToLowerInvariant()}"
                : null,
        A300Levers.SpoilersArmKey => value >= 0.5 ? "Ground spoilers armed" : "Ground spoilers disarmed",
        GearLeverKey => value >= 0.5 ? "Gear lever down" : "Gear lever up",
        ParkingBrakeKey => value >= 0.5 ? "Parking brake set" : "Parking brake released",
        _ when SasLevers.TryGetValue(key, out var lever) => $"{lever} {(value >= 0.5 ? "on" : "off")}",
        _ => null,
    };
}

/// <summary>
/// Baseline-first: the first value of each key after a reset is recorded silently, and a later value
/// speaks when its phrase differs from the last one recorded. A value with no phrase (a light going
/// out) is recorded too, so the light coming on again speaks again. Pure.
/// </summary>
public sealed class A300AnnouncementTracker
{
    private readonly Dictionary<string, string?> _last = new(StringComparer.Ordinal);

    /// <summary>The phrase to speak for this delivery, or null.</summary>
    public string? Observe(string key, double value)
    {
        string? phrase = A300Announcements.Phrase(key, value);
        bool known = _last.TryGetValue(key, out var last);
        _last[key] = phrase;
        if (!known || phrase == null || phrase == last)
            return null;
        return phrase;
    }

    /// <summary>Records a baseline only where none exists yet; true when one was added.</summary>
    public bool Seed(string key, double value) => _last.TryAdd(key, A300Announcements.Phrase(key, value));

    public void Reset() => _last.Clear();
}
