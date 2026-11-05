namespace MSFSBlindAssist.Services.PMDG;

/// <summary>How a glass field's value is stored: PMDG's double, bool (one byte) or int display value.</summary>
public enum GlassFieldKind { Double, Bool, Int }

/// <summary>
/// A value in one of PMDG's display objects: its offset, the stride between engines (or tanks) when it
/// is one per engine (0 for a scalar), and how it is stored. Read from the installed file's layout file, never
/// hardcoded.
/// </summary>
public readonly record struct GlassField(int Offset, int Stride, GlassFieldKind Kind);

/// <summary>A singleton behind a global: the global's wasm address and the object's size.</summary>
public readonly record struct GlassObject(uint Global, uint Size);

/// <summary>
/// The thrust mode display's rules, as PMDG applies them: nothing is drawn unless
/// the mode is below <see cref="ModeLimit"/>; MAN whenever N1 SET is not <see cref="N1SetAuto"/>; "---" for
/// <see cref="NoMode"/>; else the word PMDG chooses for the mode, by its derate display option
/// (<see cref="DerateOption"/>, a bool on the options object) and whether an assumed temperature is shown, one
/// choice for every mode below the limit (<see cref="Choices"/>: <see cref="Word"/>).
/// Equality compares <see cref="Choices"/> by content.
/// </summary>
public sealed record ThrustModeRules(int N1SetAuto, int NoMode, int ModeLimit, GlassField DerateOption, IReadOnlyList<int> Choices)
{
    /// <summary>The thrust word table index PMDG draws for <paramref name="mode"/> (0 to <see cref="ModeLimit"/> - 1).</summary>
    public int Word(int mode, bool derateOption, bool assumedTemperature) =>
        Choices[((derateOption ? 2 : 0) + (assumedTemperature ? 1 : 0)) * ModeLimit + mode];

    public bool Equals(ThrustModeRules? other) =>
        other is not null && N1SetAuto == other.N1SetAuto && NoMode == other.NoMode && ModeLimit == other.ModeLimit
        && DerateOption == other.DerateOption && Choices.SequenceEqual(other.Choices);

    public override int GetHashCode() => HashCode.Combine(N1SetAuto, NoMode, ModeLimit, DerateOption, Choices.Count);
}

/// <summary>
/// PMDG's aircraft object, which updates the display objects every frame only once the aircraft has initialised
/// (<see cref="Initialised"/>, the flag guarding its update of the engine display object). Before
/// that the objects hold their starting values, not readings.
/// </summary>
public sealed record AircraftFields(GlassObject Aircraft, GlassField Initialised);

/// <summary>What a display unit shows (PMDG's page values, mapped by the page each draws).</summary>
public enum DisplayPage { Off, Blank, Pfd, Nd, EngineSideBySide, EnginePrimary, EngineSecondary, EngineCompact, Map, Sys, Unknown }

/// <summary>One row group of an engine or SYS page, in PMDG's draw order.</summary>
public enum PageItem
{
    Tat, ThrustMode, N1, Egt, N2, FuelFlow, OilPressure, OilTemperature, OilQuantity, Vibration, Alerts, Fuel,
    HydraulicPressure, HydraulicQuantity, SysHydraulics, SysFlightControls, SysBrakeTemperature,
}

/// <summary>
/// The EFIS object's display units: <see cref="UnitCount"/> units <see cref="UnitStride"/> bytes apart, each
/// holding its page value at <see cref="PageOffset"/> and its index at <see cref="IndexOffset"/>; the upper
/// and lower center units by index; the page values PMDG maps to a page; the manual N1 per engine.
/// Equality compares <see cref="Pages"/> by content.
/// </summary>
public sealed record PageFields(
    GlassObject Efis,
    int UnitCount,
    int UnitStride,
    int PageOffset,
    int IndexOffset,
    int UpperUnit,
    int LowerUnit,
    IReadOnlyDictionary<int, DisplayPage> Pages,
    GlassField ManualN1)
{
    public bool Equals(PageFields? other) =>
        other is not null
        && Efis == other.Efis && UnitCount == other.UnitCount && UnitStride == other.UnitStride
        && PageOffset == other.PageOffset && IndexOffset == other.IndexOffset
        && UpperUnit == other.UpperUnit && LowerUnit == other.LowerUnit && ManualN1 == other.ManualN1
        && Pages.Count == other.Pages.Count
        && Pages.All(p => other.Pages.TryGetValue(p.Key, out var page) && page == p.Value);

    public override int GetHashCode() => HashCode.Combine(Efis, UnitStride, PageOffset, IndexOffset, Pages.Count, ManualN1);
}

/// <summary>
/// The SYS page, on the glass data object: per hydraulic system (A, then B one stride on) the quantity, pressure and
/// refill flag; the six flight control positions; per brake (four, one stride apart) the temperature and
/// its status. PMDG prints the pressure only when its rounded value is above <see cref="HydPressureShownAbove"/>
/// (its no-value marker holds -1000000).
/// </summary>
public sealed record SysFields(
    GlassField HydQuantity,
    GlassField HydPressure,
    GlassField Refill,
    GlassField AileronLeft,
    GlassField SpoilerLeft,
    GlassField Elevator,
    GlassField SpoilerRight,
    GlassField AileronRight,
    GlassField Rudder,
    GlassField BrakeTemperature,
    GlassField BrakeStatus,
    double HydPressureShownAbove);

/// <summary>The oil quantity units PMDG can print.</summary>
public enum OilQuantityUnit { Percent, Quarts, Litres }

/// <summary>
/// PMDG's options that change the printed units, on the options object: fuel in kilograms, and oil quantity in percent
/// when <see cref="OilInPercent"/> is set, else litres when <see cref="OilInLitres"/> is, else quarts, each
/// with the multiplier PMDG applies to the stored fraction; and the oil quantity LO indication, which PMDG
/// gives only when <see cref="OilLowIndication"/> is set.
/// </summary>
public sealed record OptionFields(
    GlassObject Options,
    GlassField FuelInKg,
    GlassField OilInPercent,
    GlassField OilInLitres,
    double PercentMultiplier,
    double LitresMultiplier,
    double QuartsMultiplier,
    GlassField OilLowIndication);

/// <summary>
/// Where one installed 737NG3.wasm keeps the engine displays, all from that file's layout file, never
/// hardcoded. The anchor locates the module's memory even
/// when the FMA's cannot. Equality compares the anchor and every map by content and ignores
/// <see cref="SourceFile"/>, so the same build installed twice gives one layout.
/// </summary>
public sealed record Pmdg737EngineDisplayLayout(
    string SourceFile,
    uint AnchorAddress,
    byte[] Anchor,
    GlassObject Engine,
    GlassObject Glass,
    EngineGaugeFields Gauges,
    AlertFields Alerts,
    PageFields Pages,
    SysFields Sys,
    OptionFields Options,
    AircraftFields Aircraft,
    IReadOnlyDictionary<DisplayPage, IReadOnlyList<PageItem>> PageItems)
{
    public bool Equals(Pmdg737EngineDisplayLayout? other) =>
        other is not null
        && AnchorAddress == other.AnchorAddress
        && Anchor.AsSpan().SequenceEqual(other.Anchor)
        && Engine == other.Engine && Glass == other.Glass
        && Gauges.Equals(other.Gauges) && Alerts == other.Alerts && Pages.Equals(other.Pages)
        && Sys == other.Sys && Options == other.Options && Aircraft == other.Aircraft
        && PageItems.Count == other.PageItems.Count
        && PageItems.All(p => other.PageItems.TryGetValue(p.Key, out var items) && items.SequenceEqual(p.Value));

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(AnchorAddress);
        h.AddBytes(Anchor);
        h.Add(Engine);
        h.Add(Gauges);
        h.Add(Pages);
        return h.ToHashCode();
    }
}

/// <summary>The engine displays' result in a layout file: a layout, or the detailed reason there is none.</summary>
public sealed record EngineDisplayDerivation(Pmdg737EngineDisplayLayout? Layout, string? Failure);

/// <summary>
/// The upper display's alert column and the gauges' messages. The crew alerts are PMDG's crew alert state
/// values per engine (0 off, 1 flashing, 2 steady); the fuel quantity and warning are per tank (center,
/// 1, 2) with the warning values that print LOW, CONFIG and IMBAL; the autoland advisory is one value
/// with those that print NO LAND 3 and NO AUTOLAND. The ATC and COMM memos are on the glass data object, every
/// other field on the engine display object.
/// </summary>
public sealed record AlertFields(
    GlassField StartValve,
    GlassField OilFilterBypass,
    GlassField LowOilPressure,
    GlassField FuelQuantity,
    GlassField FuelWarning,
    int FuelLowValue,
    int FuelConfigValue,
    int FuelImbalanceValue,
    GlassField ReverserShown,
    GlassField ReverserSecond,
    GlassField TaiOn,
    GlassField TaiAmber,
    GlassField EngineFail,
    GlassField CrossBleedStart,
    GlassField AutothrottleLimit,
    GlassField AutolandAdvisory,
    int NoLand3Value,
    int NoAutolandValue,
    GlassField AtcMemo,
    GlassField CommMemo);

/// <summary>
/// The engine gauges of the engine display object: the values the upper and lower displays print, the limits
/// PMDG compares them with, and the exceedance latches (per-engine bools PMDG's engine model sets after an
/// exceedance, which keep the N1, EGT and N2 readouts red once the value is back in range). PMDG prints the
/// vibration only above <see cref="VibrationShownAbove"/> (its no-value marker holds -1000000). Equality
/// compares <see cref="ThrustWords"/> by value.
/// </summary>
public sealed record EngineGaugeFields(
    GlassField Tat,
    GlassField N1,
    GlassField N1Reference,
    GlassField N1RedLine,
    GlassField Egt,
    GlassField EgtRedLine,
    GlassField EgtMaxContinuous,
    GlassField N2,
    GlassField N2Max,
    GlassField FuelFlow,
    GlassField FuelUsed,
    GlassField ShowFuelUsed,
    GlassField OilPressure,
    GlassField OilPressureRed,
    GlassField OilPressureAmber,
    GlassField OilTemperature,
    GlassField OilTemperatureRed,
    GlassField OilTemperatureAmber,
    GlassField OilQuantity,
    GlassField OilQuantityLow,
    GlassField Vibration,
    GlassField VibrationLimit,
    GlassField Running,
    GlassField ThrustMode,
    GlassField AssumedTempShown,
    GlassField AssumedTemperature,
    GlassField N1SetSelector,
    IReadOnlyList<string> ThrustWords,
    ThrustModeRules ThrustRules,
    GlassField N1RedLatch,
    GlassField EgtRedLatch,
    GlassField N2RedLatch,
    double VibrationShownAbove)
{
    public bool Equals(EngineGaugeFields? other) =>
        other is not null
        && Tat == other.Tat && N1 == other.N1 && N1Reference == other.N1Reference && N1RedLine == other.N1RedLine
        && Egt == other.Egt && EgtRedLine == other.EgtRedLine && EgtMaxContinuous == other.EgtMaxContinuous
        && N2 == other.N2 && N2Max == other.N2Max
        && FuelFlow == other.FuelFlow && FuelUsed == other.FuelUsed && ShowFuelUsed == other.ShowFuelUsed
        && OilPressure == other.OilPressure && OilPressureRed == other.OilPressureRed && OilPressureAmber == other.OilPressureAmber
        && OilTemperature == other.OilTemperature && OilTemperatureRed == other.OilTemperatureRed
        && OilTemperatureAmber == other.OilTemperatureAmber
        && OilQuantity == other.OilQuantity && OilQuantityLow == other.OilQuantityLow
        && Vibration == other.Vibration && VibrationLimit == other.VibrationLimit && Running == other.Running
        && ThrustMode == other.ThrustMode && AssumedTempShown == other.AssumedTempShown
        && AssumedTemperature == other.AssumedTemperature && N1SetSelector == other.N1SetSelector
        && ThrustWords.SequenceEqual(other.ThrustWords, StringComparer.Ordinal)
        && ThrustRules == other.ThrustRules
        && N1RedLatch == other.N1RedLatch && EgtRedLatch == other.EgtRedLatch && N2RedLatch == other.N2RedLatch
        && VibrationShownAbove == other.VibrationShownAbove;

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(N1);
        h.Add(Egt);
        h.Add(N2);
        h.Add(OilPressure);
        h.Add(ThrustMode);
        foreach (var w in ThrustWords) h.Add(w, StringComparer.Ordinal);
        h.Add(ThrustRules);
        return h.ToHashCode();
    }
}
