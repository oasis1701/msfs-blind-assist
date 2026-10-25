using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Services;

/// <summary>The FAP's emergency-exits setting: which doors and slides the cabin has.</summary>
public enum FenixExitsType
{
    SingleOverwing,
    DoubleOverwing,
    DoubleFullsize,
    SingleOverwingSingleFullsize,
    DoubleOverwingSingleFullsize
}

/// <summary>
/// Which doors, slides and lavatories the loaded airframe has, resolved from the Fenix's own settings
/// (<c>emergency_exits_type</c>, <c>aircraft_variant</c> and <c>fap_slide_disarmed_message</c>) with the
/// airframe type as the fallback for a setting that is missing or unknown. Pure: no data service, no state.
/// <para>
/// Evidence (Fenix 3.0.0.7426 FAP/EFB bundles): the per-type defaults are the
/// FAP's <c>be={A319:{... emergencyExitsType:"SingleOverwing", aircraftVariant:"HighDensity"}, A320:{...
/// "DoubleOverwing", "SingleClass"}, A321:{... "DoubleFullsize", "TwoClass"}}</c> (the FAP starts from
/// the A320 entry, so an unknown airframe is an A320). Doors and slides are the FAP's <c>mainDoors$</c>
/// (<c>function P(B){switch(B){case "SingleOverwing": case "DoubleOverwing": return
/// ["1L","1R","4L","4R"]; ...</c>) and <c>slides$</c> (<c>function T(B)</c>; the overwing exits add
/// <c>"OWL","OWR"</c>). Lavatories are the FAP's <c>getLavatoryDesignations</c> (<c>case "SingleClass":
/// return ["A","D","E"]; case "HighDensity": return ["A","D"]; case "TwoClass": return
/// ["A","M","D","E"]</c>). "Slide In Park" is the FAP's <c>getDoorMessage</c> test
/// <c>"SlideInPark"===l</c>. The ground-service doors are the EFB's per-type
/// <c>groundServices</c> door entries: the A320 table lists <c>d1l, d1r, d4l, d4r</c> plus the forward,
/// aft and bulk cargo; the A319 table lists the same four doors and only the forward and aft cargo; the
/// A321 table lists all eight doors and the bulk cargo. The EFB has no A318 table, so the A318 takes
/// the A320 entry.
/// </para>
/// </summary>
/// <param name="Type">The airframe the layout was resolved for.</param>
/// <param name="Exits">The exits type in effect: the setting, or the airframe's default.</param>
/// <param name="MainDoors">FAP door designations (<c>1L</c>), in the FAP's order.</param>
/// <param name="Slides">FAP slide designations (<c>1L</c>, <c>OWL</c>), in the FAP's order.</param>
/// <param name="Lavatories">Lavatory letters, in the FAP's order.</param>
/// <param name="GroundDoors">Door data keys (<c>d1l</c>) the EFB offers for ground service, in main-door order.</param>
/// <param name="HasBulkCargo">The EFB lists a bulk cargo door for this airframe (every one but the A319).</param>
/// <param name="SlideInPark">The Fenix's slide message says "SlideInPark" rather than "SlideDisarmed".</param>
public sealed record FenixCabinLayout(
    FenixAircraftType Type,
    FenixExitsType Exits,
    IReadOnlyList<string> MainDoors,
    IReadOnlyList<string> Slides,
    IReadOnlyList<string> Lavatories,
    IReadOnlyList<string> GroundDoors,
    bool HasBulkCargo,
    bool SlideInPark)
{
    /// <summary>The Fenix setting the exits type is read from (<see cref="Resolve"/>'s <c>emergencyExitsType</c>).</summary>
    public const string ExitsSettingKey = "emergency_exits_type";
    /// <summary>The Fenix setting the cabin variant is read from (<see cref="Resolve"/>'s <c>aircraftVariant</c>).</summary>
    public const string VariantSettingKey = "aircraft_variant";
    /// <summary>The Fenix setting the slide message is read from (<see cref="Resolve"/>'s <c>slideDisarmedMessage</c>).</summary>
    public const string SlideMessageSettingKey = "fap_slide_disarmed_message";

    private const string SlideInParkMessage = "SlideInPark";

    private static readonly IReadOnlyList<string> CornerDoors = Designations("1L", "1R", "4L", "4R");
    private static readonly IReadOnlyList<string> AllDoors = Designations("1L", "1R", "2L", "2R", "3L", "3R", "4L", "4R");
    private static readonly IReadOnlyList<string> ThreeDoorPairs = Designations("1L", "1R", "3L", "3R", "4L", "4R");
    private static readonly IReadOnlyList<string> CornerSlidesWithOverwing = Designations("1L", "1R", "OWL", "OWR", "4L", "4R");
    private static readonly IReadOnlyList<string> ThreePairSlidesWithOverwing = Designations("1L", "1R", "OWL", "OWR", "3L", "3R", "4L", "4R");

    private static readonly IReadOnlyList<string> SingleClassLavatories = Designations("A", "D", "E");
    private static readonly IReadOnlyList<string> HighDensityLavatories = Designations("A", "D");
    private static readonly IReadOnlyList<string> TwoClassLavatories = Designations("A", "M", "D", "E");

    /// <summary>
    /// The layout for the loaded airframe. A setting wins when it is spelled exactly (ordinal) as the
    /// Fenix spells it; a missing, empty or unknown one falls back to the airframe type.
    /// </summary>
    /// <param name="type">The airframe from the detected variant.</param>
    /// <param name="emergencyExitsType">The <c>emergency_exits_type</c> setting, if read.</param>
    /// <param name="aircraftVariant">The <c>aircraft_variant</c> setting, if read.</param>
    /// <param name="slideDisarmedMessage">The <c>fap_slide_disarmed_message</c> setting, if read.</param>
    public static FenixCabinLayout Resolve(
        FenixAircraftType type, string? emergencyExitsType, string? aircraftVariant, string? slideDisarmedMessage)
    {
        FenixExitsType exits = ParseExits(emergencyExitsType) ?? DefaultExits(type);
        IReadOnlyList<string> mainDoors = MainDoorsFor(exits);

        // The EFB offers the four corner doors on every airframe and all eight on the A321; a door the
        // cabin does not have (no 2L/2R, or 3L/3R) is not offered either.
        IReadOnlyList<string> offered = type == FenixAircraftType.A321 ? AllDoors : CornerDoors;
        var offeredKeys = new HashSet<string>(offered.Select(DataKey), StringComparer.Ordinal);
        string[] groundDoors = mainDoors.Select(DataKey).Where(offeredKeys.Contains).ToArray();

        return new FenixCabinLayout(
            type,
            exits,
            mainDoors,
            SlidesFor(exits),
            LavatoriesFor(aircraftVariant) ?? DefaultLavatories(type),
            Array.AsReadOnly(groundDoors),
            HasBulkCargo: type != FenixAircraftType.A319,
            SlideInPark: string.Equals(slideDisarmedMessage, SlideInParkMessage, StringComparison.Ordinal));
    }

    /// <summary>A setting's value from the Fenix settings API's rows (ordinal key), or null when it is not there.</summary>
    public static string? SettingValue(IReadOnlyList<FenixSetting> settings, string key)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.FirstOrDefault(setting => string.Equals(setting.Key, key, StringComparison.Ordinal))?.Value;
    }

    /// <summary>
    /// The data-service key of a FAP designation: <c>1L</c> is <c>d1l</c> and <c>OWR</c> is <c>owr</c>
    /// (the FAP's <c>doorDesignations</c> and <c>slideDesignations</c> maps, inverted).
    /// </summary>
    public static string DataKey(string designation)
    {
        string lower = designation.ToLowerInvariant();
        return lower.Length > 0 && char.IsAsciiDigit(lower[0]) ? "d" + lower : lower;
    }

    private static IReadOnlyList<string> Designations(params string[] items) => Array.AsReadOnly(items);

    // Exactly the enum name: Enum.TryParse would also take any case, padding and a number.
    private static FenixExitsType? ParseExits(string? setting)
    {
        foreach (FenixExitsType value in Enum.GetValues<FenixExitsType>())
        {
            if (string.Equals(setting, value.ToString(), StringComparison.Ordinal))
                return value;
        }
        return null;
    }

    private static FenixExitsType DefaultExits(FenixAircraftType type) => type switch
    {
        FenixAircraftType.A319 => FenixExitsType.SingleOverwing,
        FenixAircraftType.A321 => FenixExitsType.DoubleFullsize,
        _ => FenixExitsType.DoubleOverwing
    };

    private static IReadOnlyList<string> MainDoorsFor(FenixExitsType exits) => exits switch
    {
        FenixExitsType.SingleOverwing or FenixExitsType.DoubleOverwing => CornerDoors,
        FenixExitsType.DoubleFullsize => AllDoors,
        FenixExitsType.SingleOverwingSingleFullsize or FenixExitsType.DoubleOverwingSingleFullsize => ThreeDoorPairs,
        _ => throw new ArgumentOutOfRangeException(nameof(exits), exits, null)
    };

    private static IReadOnlyList<string> SlidesFor(FenixExitsType exits) => exits switch
    {
        FenixExitsType.SingleOverwing or FenixExitsType.DoubleOverwing => CornerSlidesWithOverwing,
        FenixExitsType.DoubleFullsize => AllDoors,
        FenixExitsType.SingleOverwingSingleFullsize or FenixExitsType.DoubleOverwingSingleFullsize => ThreePairSlidesWithOverwing,
        _ => throw new ArgumentOutOfRangeException(nameof(exits), exits, null)
    };

    private static IReadOnlyList<string>? LavatoriesFor(string? aircraftVariant) => aircraftVariant switch
    {
        "SingleClass" => SingleClassLavatories,
        "HighDensity" => HighDensityLavatories,
        "TwoClass" => TwoClassLavatories,
        _ => null
    };

    private static IReadOnlyList<string> DefaultLavatories(FenixAircraftType type) => type switch
    {
        FenixAircraftType.A319 => HighDensityLavatories,
        FenixAircraftType.A321 => TwoClassLavatories,
        _ => SingleClassLavatories
    };
}
