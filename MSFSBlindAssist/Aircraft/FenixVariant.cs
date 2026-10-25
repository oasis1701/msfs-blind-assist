using System.Globalization;
using System.Text;

namespace MSFSBlindAssist.Aircraft;

// Which Fenix is loaded, read from eight config L:vars the Fenix publishes. Pure: no SimConnect, no
// state. FenixA320Definition keeps one current FenixVariant and raises VariantChanged when it changes.

/// <summary>The loaded airframe. The values follow the Fenix settings order (3 = A320 and 4 = A321
/// measured); the Fenix's own LOD.js 1/2/3 mapping is wrong, do not use it.</summary>
public enum FenixAircraftType
{
    Unknown = 0,
    A318 = 1,
    A319 = 2,
    A320 = 3,
    A321 = 4,
}

/// <summary>The fitted engines (CFM and IAE measured; the LEAP and the GTF follow the settings order).</summary>
public enum FenixEngineFamily
{
    Unknown = -1,
    Cfm56 = 0,
    IaeV2500 = 1,
    CfmLeap = 2,
    PwGtf = 3,
}

/// <summary>The Fenix config L:vars, spelled as the Fenix spells them (cargo heat's is CARGOMEAT).</summary>
public static class FenixConfigVars
{
    public const string AircraftType = "N_CONFIG_AIRCRAFT_TYPE";
    public const string EngineType = "N_CONFIG_ENGINETYPE";
    public const string ActCount = "N_CONFIG_ACT_COUNT";
    public const string Standby = "B_CONFIG_STDBY";
    public const string Dcdu = "B_CONFIG_DCDU";
    public const string IsNeo = "B_CONFIG_IS_NEO";
    public const string Adirs = "B_CONFIG_ADIRS";
    public const string CargoHeat = "B_CONFIG_CARGOMEAT";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        AircraftType, EngineType, ActCount, Standby, Dcdu, IsNeo, Adirs, CargoHeat,
    });
}

/// <summary>
/// What the config L:vars say about the loaded Fenix. A field the Fenix has not published, or published
/// as a value it never uses, is Unknown or null. HasCargoHeat is optional so a seven-argument
/// construction still compiles.
/// </summary>
public sealed record FenixVariant(
    FenixAircraftType Type,
    FenixEngineFamily Engine,
    int? ActCount,
    bool? HasIsis,
    bool? HasDcdu,
    bool? IsNeo,
    bool? HasGnadirs,
    bool? HasCargoHeat = null)
{
    public static FenixVariant Unknown { get; } = new(
        FenixAircraftType.Unknown, FenixEngineFamily.Unknown, null, null, null, null, null, null);

    /// <summary>The airframe and its engines are both known.</summary>
    public bool IsKnown => Type != FenixAircraftType.Unknown && Engine != FenixEngineFamily.Unknown;

    /// <summary>
    /// "Fenix A321, IAE engines, 2 extra tanks": the type, the engines and the extra tanks, each left
    /// out while unknown (or, for the tanks, none). The equipment (ISIS, DCDU, NEO, GNADIRS, cargo heat)
    /// is never described, so this text changes with the tank count and with nothing else of those.
    /// </summary>
    public string Describe()
    {
        var text = new StringBuilder("Fenix");
        switch (Type)
        {
            case FenixAircraftType.A318: text.Append(" A318"); break;
            case FenixAircraftType.A319: text.Append(" A319"); break;
            case FenixAircraftType.A320: text.Append(" A320"); break;
            case FenixAircraftType.A321: text.Append(" A321"); break;
        }
        switch (Engine)
        {
            case FenixEngineFamily.Cfm56: text.Append(", CFM engines"); break;
            case FenixEngineFamily.IaeV2500: text.Append(", IAE engines"); break;
            case FenixEngineFamily.CfmLeap: text.Append(", CFM LEAP engines"); break;
            case FenixEngineFamily.PwGtf: text.Append(", Pratt and Whitney GTF engines"); break;
        }
        if (ActCount is int tanks && tanks >= 1)
            text.Append(tanks == 1
                ? ", 1 extra tank"
                : string.Create(CultureInfo.InvariantCulture, $", {tanks} extra tanks"));
        return text.ToString();
    }

    /// <summary>
    /// This record with the one field <paramref name="configVar"/> drives set from <paramref name="value"/>.
    /// A name that is not a config var, and a value that is not a finite number, return this record. A
    /// finite value the var never takes sets its field to Unknown or null.
    /// </summary>
    public FenixVariant With(string configVar, double value)
    {
        if (!double.IsFinite(value)) return this;
        return configVar switch
        {
            FenixConfigVars.AircraftType => this with { Type = ReadType(value) },
            FenixConfigVars.EngineType => this with { Engine = ReadEngine(value) },
            FenixConfigVars.ActCount => this with { ActCount = ReadCount(value) },
            // 0 = ISIS, 1 = the round standby gauges, no ISIS. Measured 2026-10-22: it read 1 on the
            // round-gauge CFM A320 (C-FFWM, N_CONFIG_AIRCRAFT_TYPE 3) and 0 on the ISIS A321.
            FenixConfigVars.Standby => this with { HasIsis = ReadFlag(value, zeroMeans: true) },
            FenixConfigVars.Dcdu => this with { HasDcdu = ReadFlag(value, zeroMeans: false) },
            FenixConfigVars.IsNeo => this with { IsNeo = ReadFlag(value, zeroMeans: false) },
            // 0 = GNADIRS keypad, 1 = plain ADIRS.
            FenixConfigVars.Adirs => this with { HasGnadirs = ReadFlag(value, zeroMeans: true) },
            // The model shows the cargo heat controls for any non-zero value and a blank plate for 0.
            FenixConfigVars.CargoHeat => this with { HasCargoHeat = value != 0 },
            _ => this,
        };
    }

    private static FenixAircraftType ReadType(double value)
        => IsWholeIn(value, 0, 4) ? (FenixAircraftType)(int)value : FenixAircraftType.Unknown;

    private static FenixEngineFamily ReadEngine(double value)
        => IsWholeIn(value, 0, 3) ? (FenixEngineFamily)(int)value : FenixEngineFamily.Unknown;

    private static int? ReadCount(double value)
        => IsWholeIn(value, 0, int.MaxValue) ? (int)value : null;

    // A 0/1 var: <zeroMeans> for 0, its opposite for 1, anything else unknown.
    private static bool? ReadFlag(double value, bool zeroMeans)
        => value == 0 ? zeroMeans : value == 1 ? !zeroMeans : null;

    private static bool IsWholeIn(double value, int min, int max)
        => value >= min && value <= max && value == Math.Floor(value);
}
