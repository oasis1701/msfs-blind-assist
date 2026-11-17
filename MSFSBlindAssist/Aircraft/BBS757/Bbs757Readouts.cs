using System.Globalization;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>What the three gear legs say together.</summary>
public enum Bbs757GearState { Up, Down, Transit }

/// <summary>One read-only engine row on the Pedestal's Engines panel: its key, stock variable, units and spoken label.</summary>
public sealed record Bbs757EngineRow(string Key, string SimVar, string Units, string Label, string Kind);

/// <summary>
/// The BlueBird 757's flight-control and engine readouts, pure: the words for the flap detents, the
/// gear, the speedbrake and the parking brake (announced on change and read by the hotkeys), the
/// fuel and weight sentences (F, Shift+F, W, Shift+W) and the engine rows. Every number is written
/// with the invariant culture: a comma decimal or a locale's minus sign is not what the cockpit
/// shows. <see cref="BBS757Definition"/> registers the variables and reads them.
///
/// Which variable is the truth on this aircraft (measured 2026-10-25; docs/b757.md, "Flight-control readouts and call-outs"):
///   • Flaps: the stock <c>FLAPS HANDLE INDEX</c> follows BlueBird's lever (FLAPS_INCR moved it 0
///     to 1 with <c>L:B757_FC_FLAP_LEVER</c>); the surfaces are <c>TRAILING EDGE FLAPS LEFT ANGLE</c>.
///   • The speedbrake is <c>L:B757_FC_SPOILER_LEVER</c> (0 down, 10 armed, 100 up) and the parking
///     brake <c>L:B757_FC_PARK_BRAKE</c>: the stock <c>BRAKE PARKING POSITION</c> read 0 with
///     BlueBird's brake set, so it is never read.
///   • Gear: the stock legs, <c>GEAR CENTER/LEFT/RIGHT POSITION</c> (0 up, 1 down), and BlueBird's
///     lever <c>L:B757_GEAR_HANDLE_POS</c> (0 UP, 0.5 OFF, 1 DOWN): the stock <c>GEAR HANDLE
///     POSITION</c> has no OFF and read up with the lever at OFF and the gear down (a flight of 2026-10-28).
/// </summary>
public static class Bbs757Readouts
{
    // ---- variables ----------------------------------------------------------------------------

    public const string FlapsHandleKey = "BBS757_FLAPS_HANDLE";
    public const string FlapsAngleKey = "BBS757_FLAPS_ANGLE";
    public const string GearHandleKey = "BBS757_GEAR_HANDLE";
    /// <summary>The nose leg; its row is the one Ctrl+M row for the gear call-outs, whichever leg moved.</summary>
    public const string GearNoseKey = "BBS757_GEAR_NOSE";
    public const string GearLeftKey = "BBS757_GEAR_LEFT";
    public const string GearRightKey = "BBS757_GEAR_RIGHT";
    public const string SpeedbrakeKey = "BBS757_SPEEDBRAKE";
    public const string ParkingBrakeKey = "BBS757_PARKING_BRAKE";
    public const string TotalWeightKey = "BBS757_TOTAL_WEIGHT";

    public const string SpeedbrakeLeverVar = "B757_FC_SPOILER_LEVER";
    public const string ParkingBrakeVar = "B757_FC_PARK_BRAKE";

    // ---- flaps ----------------------------------------------------------------------------------

    /// <summary>The lever detents by <c>FLAPS HANDLE INDEX</c>, as the 757's flap lever is marked.</summary>
    public static IReadOnlyList<string> FlapDetents { get; } = new[] { "UP", "1", "5", "15", "20", "25", "30" };

    /// <summary>
    /// The trailing-edge angle of each detent in BlueBird's <c>flight_model.cfg</c> ([FLAPS.0]
    /// flaps-position.0 to .6): flaps 15 is 11 degrees and flaps 30 is 34, so the detent's name is not
    /// its angle.
    /// </summary>
    public static IReadOnlyList<double> FlapDetentAngles { get; } = new double[] { 0, 1, 5, 11, 20, 25, 34 };

    /// <summary>
    /// How far the surfaces may sit from the detent's angle and still be "at" it, in degrees: below half
    /// the smallest gap between two detents (UP 0 and 1 are a degree apart), so a surface still at 0
    /// with the lever on 1 is never taken for flaps 1 (the measured no-hydraulics case).
    /// </summary>
    public const double FlapAngleTolerance = 0.4;

    /// <summary>Two angle reads further apart than this, in degrees, mean the surfaces are moving.</summary>
    public const double FlapMovingDelta = 0.1;

    /// <summary>The time between the two angle reads that tell moving surfaces from stopped ones.</summary>
    public const int FlapMovingProbeMs = 500;

    /// <summary>The detent's name for a handle index (0 to 6), or null for anything else.</summary>
    public static string? FlapDetentName(double index)
    {
        int i = DetentIndex(index);
        return i >= 0 ? FlapDetents[i] : null;
    }

    private static int DetentIndex(double index)
    {
        if (!double.IsFinite(index)) return -1;
        double r = Math.Round(index);
        if (Math.Abs(index - r) > 0.01 || r < 0 || r >= FlapDetents.Count) return -1;
        return (int)r;
    }

    /// <summary>"Flaps 5", "Flaps UP"; "Flaps unavailable" when the handle was not read or is no detent.</summary>
    public static string FlapsPhrase(double? index)
        => index is double i && FlapDetentName(i) is { } name ? $"Flaps {name}" : "Flaps unavailable";

    /// <summary>
    /// Output L: the detent, and where the surfaces are when they are not at its angle: "Flaps 5",
    /// "Flaps 5, moving, 3 degrees" (two reads apart differ), "Flaps 5, surfaces at 0 degrees" (they
    /// stay off the detent: no hydraulics, or a fault). An unread angle says the detent alone.
    /// </summary>
    public static string FlapsReadout(double? index, double? angle, bool? moving)
    {
        int d = index is double i ? DetentIndex(i) : -1;
        if (d < 0) return "Flaps unavailable";
        string detent = $"Flaps {FlapDetents[d]}";
        if (angle is not double a || !double.IsFinite(a)) return detent;
        if (Math.Abs(a - FlapDetentAngles[d]) <= FlapAngleTolerance) return detent;
        long whole = Whole(a);
        string degrees = $"{whole.ToString(CultureInfo.InvariantCulture)} {(Math.Abs(whole) == 1 ? "degree" : "degrees")}";
        return moving == true ? $"{detent}, moving, {degrees}" : $"{detent}, surfaces at {degrees}";
    }

    /// <summary>Whether two angle reads <see cref="FlapMovingProbeMs"/> apart show the surfaces moving.</summary>
    public static bool FlapsMoving(double first, double second) => Math.Abs(second - first) > FlapMovingDelta;

    /// <summary>
    /// Output L: reads the handle and the surface angle; when the surfaces are off the detent's angle,
    /// waits <see cref="FlapMovingProbeMs"/> and reads the angle again to tell moving surfaces from
    /// stopped ones, and speaks the second read (<see cref="FlapsReadout"/>).
    /// </summary>
    public static async Task<string> ReadFlapsAsync(McpIo io, CancellationToken ct)
    {
        double? index = await io.Read(FlapsHandleKey, ct);
        double? angle = await io.Read(FlapsAngleKey, ct);
        int d = index is double i ? DetentIndex(i) : -1;
        if (d < 0 || angle is not double first || !double.IsFinite(first)
            || Math.Abs(first - FlapDetentAngles[d]) <= FlapAngleTolerance)
            return FlapsReadout(index, angle, moving: null);

        await io.Delay(FlapMovingProbeMs, ct);
        double? second = await io.Read(FlapsAngleKey, ct);
        if (second is not double s || !double.IsFinite(s)) return FlapsReadout(index, first, moving: null);
        return FlapsReadout(index, s, FlapsMoving(first, s));
    }

    // ---- gear -----------------------------------------------------------------------------------

    /// <summary>
    /// The three legs together (0 up, 1 down): all down, all up, or in transit; null when a leg was
    /// not read.
    /// </summary>
    public static Bbs757GearState? GearLegs(double? nose, double? left, double? right)
    {
        if (nose is not double n || left is not double l || right is not double r) return null;
        if (!double.IsFinite(n) || !double.IsFinite(l) || !double.IsFinite(r)) return null;
        if (n >= 0.99 && l >= 0.99 && r >= 0.99) return Bbs757GearState.Down;
        if (n <= 0.01 && l <= 0.01 && r <= 0.01) return Bbs757GearState.Up;
        return Bbs757GearState.Transit;
    }

    /// <summary>The gear call-out: "Gear down", "Gear up", "Gear in transit" or "Gear unavailable".</summary>
    public static string GearPhrase(Bbs757GearState? state) => state switch
    {
        Bbs757GearState.Down => "Gear down",
        Bbs757GearState.Up => "Gear up",
        Bbs757GearState.Transit => "Gear in transit",
        _ => "Gear unavailable",
    };

    /// <summary>The gear's status row: "Down", "Up", "In transit".</summary>
    public static string GearDisplay(Bbs757GearState? state) => state switch
    {
        Bbs757GearState.Down => "Down",
        Bbs757GearState.Up => "Up",
        Bbs757GearState.Transit => "In transit",
        _ => "unavailable",
    };

    /// <summary>
    /// The lever's words from <c>L:B757_GEAR_HANDLE_POS</c> (0 UP, 0.5 OFF, 1 DOWN), at the nearest
    /// detent: "up", "off", "down".
    /// </summary>
    public static string GearLeverWords(double handle) => handle < 0.25 ? "up" : handle <= 0.75 ? "off" : "down";

    /// <summary>
    /// Shift+G: the legs, then the lever, always, as the PMDG 737 does: "Gear up, lever off", "Gear
    /// down, lever off" (a flight of 2026-10-28: the sim's gear key stepped BlueBird's lever to OFF before
    /// the gear moved, and the stock handle, with no OFF, read up). An unread lever says the legs alone.
    /// </summary>
    public static string GearReadout(double? handle, double? nose, double? left, double? right)
    {
        var legs = GearLegs(nose, left, right);
        string phrase = GearPhrase(legs);
        if (legs == null || handle is not double h || !double.IsFinite(h)) return phrase;
        return $"{phrase}, lever {GearLeverWords(h)}";
    }

    // ---- speedbrake and parking brake -----------------------------------------------------------

    /// <summary>
    /// The speedbrake lever's words, from <c>L:B757_FC_SPOILER_LEVER</c> (measured: 0 down, 10 armed;
    /// the UP click writes 100): "Down" below 5, "Armed" to 15, "Up" from 95, otherwise "Up N percent"
    /// to the nearest ten. Null when it was not read.
    /// </summary>
    public static string? SpeedbrakeWords(double? lever)
    {
        if (lever is not double v || !double.IsFinite(v)) return null;
        if (v < 5) return "Down";
        if (v <= 15) return "Armed";
        if (v >= 95) return "Up";
        long tens = (long)Math.Round(v / 10, MidpointRounding.AwayFromZero) * 10;
        return $"Up {tens.ToString(CultureInfo.InvariantCulture)} percent";
    }

    /// <summary>The call-out and readout: "Speedbrake armed", "Speedbrake up 50 percent", "Speedbrake unavailable".</summary>
    public static string SpeedbrakePhrase(double? lever)
        => SpeedbrakeWords(lever) is { } w ? $"Speedbrake {Lower(w)}" : "Speedbrake unavailable";

    /// <summary>The speedbrake's status row: "Armed", "Up 60 percent".</summary>
    public static string SpeedbrakeDisplay(double? lever) => SpeedbrakeWords(lever) ?? "unavailable";

    /// <summary>"Parking brake set", "Parking brake released", or "Parking brake unavailable" when not read.</summary>
    public static string ParkingBrakePhrase(double? value)
    {
        if (value is not double v || !double.IsFinite(v)) return "Parking brake unavailable";
        return v > 0.5 ? "Parking brake set" : "Parking brake released";
    }

    private static string Lower(string words) => words.Length == 0 ? words : char.ToLowerInvariant(words[0]) + words[1..];

    // ---- stabilizer trim ------------------------------------------------------------------------

    /// <summary>The trim indicator strip's floor, in units: it shows no less however far nose down the stabilizer goes.</summary>
    public const double StabTrimMinUnits = 0.436;

    /// <summary>
    /// The stabilizer trim in BlueBird's indicator units from the stock <c>ELEVATOR TRIM POSITION</c> in
    /// degrees: <c>max(0.436, -0.002148·d² + 0.803152·d + 1.11308)</c>, the strip's own code in the
    /// cockpit XML (measured live: 2.3 degrees at load is 2.95 units). The curve rises until
    /// about 187 degrees, far past the stabilizer's travel, so more nose-up trim never reads fewer units;
    /// below about -0.84 degrees the strip rests on <see cref="StabTrimMinUnits"/>.
    /// </summary>
    public static double StabTrimUnits(double degrees)
        => Math.Max(StabTrimMinUnits, -0.002148 * degrees * degrees + 0.803152 * degrees + 1.11308);

    // ---- EXEC light -----------------------------------------------------------------------------

    /// <summary>The EXEC light's call-out row, keyed apart from the L:var like the other rows.</summary>
    public const string ExecLightKey = "BBS757_EXEC_LIGHT";

    /// <summary>
    /// The captain's CDU EXEC light, <c>L:FMC_EXEC_ACTIVE_1</c> (1 lit). Both CDUs lit together after
    /// ACTIVATE and went out after EXEC (session 7), so the first officer's <c>_2</c> is not read (ruling R36).
    /// </summary>
    public const string ExecLightVar = "FMC_EXEC_ACTIVE_1";

    /// <summary>"EXEC light on", "EXEC light off" (the iFly CDU window's words), "EXEC light unavailable" when not read.</summary>
    public static string ExecLightPhrase(double? value)
    {
        if (value is not double v || !double.IsFinite(v)) return "EXEC light unavailable";
        return v > 0.5 ? "EXEC light on" : "EXEC light off";
    }

    // ---- fuel and weight ------------------------------------------------------------------------

    /// <summary>
    /// F and Shift+F: the fuel on board, each tank from its gallons × the sim's fuel weight per gallon,
    /// in pounds or kilograms, rounded to the nearest 10, the total the sum of the spoken tanks (the
    /// same shape as the SimBrief load's sentence): "Fuel 25480 pounds: left 12740, right 12740,
    /// center 0". "Fuel unavailable" when a tank or the weight per gallon was not read.
    /// </summary>
    public static string FuelPhrase(double? leftGallons, double? rightGallons, double? centerGallons,
        double? poundsPerGallon, bool kilograms)
    {
        if (leftGallons is not double l || rightGallons is not double r || centerGallons is not double c
            || poundsPerGallon is not double d
            || !double.IsFinite(l) || !double.IsFinite(r) || !double.IsFinite(c) || !double.IsFinite(d))
            return "Fuel unavailable";
        long N(double gallons) => Round10(kilograms ? gallons * d / Bbs757Loadsheet.PoundsPerKilogram : gallons * d);
        long left = N(l), right = N(r), center = N(c);
        return $"Fuel {S(left + right + center)} {Unit(kilograms)}: left {S(left)}, right {S(right)}, center {S(center)}";
    }

    /// <summary>W and Shift+W: "Gross weight 208239 pounds" or "… kilograms", whole; "Gross weight unavailable".</summary>
    public static string WeightPhrase(double? totalPounds, bool kilograms)
    {
        if (totalPounds is not double w || !double.IsFinite(w)) return "Gross weight unavailable";
        long value = Whole(kilograms ? w / Bbs757Loadsheet.PoundsPerKilogram : w);
        return $"Gross weight {S(value)} {Unit(kilograms)}";
    }

    private static string Unit(bool kilograms) => kilograms ? "kilograms" : "pounds";

    private static string S(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>To the nearest 10, half away from zero, as a whole number (so a hair below zero is 0, never "-0").</summary>
    private static long Round10(double value) => (long)Math.Round(value / 10, MidpointRounding.AwayFromZero) * 10;

    private static long Whole(double value) => (long)Math.Round(value, MidpointRounding.AwayFromZero);

    // ---- engines --------------------------------------------------------------------------------

    /// <summary>
    /// The Engines panel's status rows, left engine then right: N1, N2, EGT, fuel flow and EPR from the
    /// stock engine variables. Compared with BlueBird's upper EICAS on 2026-11-09 (the RR aircraft): they are
    /// the simulator's values, not the EICAS's (docs/b757.md, "Engine values"). Nothing here is announced.
    /// </summary>
    public static IReadOnlyList<Bbs757EngineRow> EngineRows { get; } = BuildEngineRows();

    private static Bbs757EngineRow[] BuildEngineRows()
    {
        var rows = new List<Bbs757EngineRow>();
        foreach (var (n, side) in new[] { (1, "Left"), (2, "Right") })
        {
            rows.Add(new($"BBS757_ENG{n}_N1", $"TURB ENG N1:{n}", "percent", $"{side} N1", "N1"));
            rows.Add(new($"BBS757_ENG{n}_N2", $"TURB ENG N2:{n}", "percent", $"{side} N2", "N2"));
            rows.Add(new($"BBS757_ENG{n}_EGT", $"GENERAL ENG EXHAUST GAS TEMPERATURE:{n}", "celsius", $"{side} EGT", "EGT"));
            rows.Add(new($"BBS757_ENG{n}_FF", $"ENG FUEL FLOW PPH:{n}", "pounds per hour", $"{side} Fuel Flow", "FF"));
            rows.Add(new($"BBS757_ENG{n}_EPR", $"TURB ENG PRESSURE RATIO:{n}", "ratio", $"{side} EPR", "EPR"));
        }
        return rows.ToArray();
    }

    /// <summary>
    /// An engine row's text: N1 and N2 "23.4 percent", EGT "425 degrees C", fuel flow "1251 pounds per
    /// hour", EPR "1.02" (the words the PMDG 777's system display uses for the same values).
    /// </summary>
    public static string EngineText(Bbs757EngineRow row, double value) => row.Kind switch
    {
        "N1" or "N2" => string.Create(CultureInfo.InvariantCulture, $"{value:0.0} percent"),
        "EGT" => $"{S(Whole(value))} degrees C",
        "FF" => $"{S(Whole(value))} pounds per hour",
        _ => string.Create(CultureInfo.InvariantCulture, $"{value:0.00}"),
    };
}
