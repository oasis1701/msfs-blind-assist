using System.Globalization;
using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.FirstOfficer.A300;

/// <summary>
/// What the First Officer reads where the card leaves a blank ("Altimeters: SET, 1013 hectopascals, both"):
/// spoken after the label when the pilot ticks the line, never in the tree text (ChecklistItem.LiveValue).
/// Numbers come from the panels' own readout formats, which use the invariant culture. Null when unread.
/// </summary>
public static class A300FoLiveValues
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly IReadOnlyDictionary<string, A300Readout> Readouts =
        A300Readouts.All.Concat(A300EcamPages.Readouts).GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.First());

    /// <summary>Every key a live value reads (the registration test).</summary>
    public static readonly IReadOnlyList<string> Keys = new[]
    {
        A300FoKeys.FuelTotal, A300FoKeys.LandingElevation, A300FoKeys.BaroCaptain, A300FoKeys.BaroFirstOfficer,
        A300Baro.Captain.ModeKey, A300Baro.FirstOfficer.ModeKey, A300FoKeys.ParkingBrake, A300FoKeys.PitchTrim,
        A300FoKeys.Flaps, A300FoVariables.StatusMessagesKey, A300FoKeys.AntiIceEng1, A300FoKeys.AntiIceEng2,
        A300FoKeys.WingSupply, A300TakeoffCallouts.V1Key, A300TakeoffCallouts.VrKey, A300TakeoffCallouts.V2Key,
        A300FoKeys.FlexTemperature, A300FoKeys.Transponder, A300FoKeys.Squawk, A300FoKeys.Ignition,
        A300FoKeys.Minimums, A300FoKeys.AutobrakeLevel, A300FoKeys.DeltaP, A300FoKeys.ApuBleed,
    };

    private static string? Readout(Func<string, double?> read, string key) =>
        read(key) is double v ? Readouts[key].Format(v) : null;

    private static string OnOff(double v) => v >= 0.5 ? "on" : "off";

    public static string? Fuel(Func<string, double?> read) => Readout(read, A300FoKeys.FuelTotal);
    public static string? LandingElevation(Func<string, double?> read) => Readout(read, A300FoKeys.LandingElevation);
    public static string? PitchTrim(Func<string, double?> read) => Readout(read, A300FoKeys.PitchTrim);
    public static string? Minimums(Func<string, double?> read) => Readout(read, A300FoKeys.Minimums);
    public static string? DeltaP(Func<string, double?> read) => Readout(read, A300FoKeys.DeltaP);

    public static string? Altimeters(Func<string, double?> read)
    {
        if (read(A300Baro.Captain.ModeKey) is double m1 && read(A300Baro.FirstOfficer.ModeKey) is double m2
            && A300Baro.IsStd(m1) && A300Baro.IsStd(m2))
            return "standard, both";
        if (read(A300FoKeys.BaroCaptain) is not double cpt || read(A300FoKeys.BaroFirstOfficer) is not double fo)
            return null;
        return Math.Round(cpt) == Math.Round(fo)
            ? $"{A300Readouts.Altimeter(cpt)}, both"
            : $"captain {A300Readouts.Altimeter(cpt)}, first officer {A300Readouts.Altimeter(fo)}";
    }

    public static string? ParkingBrake(Func<string, double?> read) =>
        read(A300FoKeys.ParkingBrake) is double v ? (v >= 0.5 ? "set" : "released") : null;

    public static string? Flaps(Func<string, double?> read) =>
        read(A300FoKeys.Flaps) is double v && A300Levers.FlapPositions.TryGetValue(Math.Round(v), out var word) ? word : null;

    public static string? EcamStatus(Func<string, double?> read)
    {
        if (read(A300FoVariables.StatusMessagesKey) is not double n)
            return null;
        int count = (int)Math.Round(n);
        return count switch
        {
            0 => "no status messages",
            1 => "1 status message",
            _ => $"{count.ToString(Inv)} status messages",
        };
    }

    public static string? AntiIce(Func<string, double?> read) =>
        read(A300FoKeys.AntiIceEng1) is double e1 && read(A300FoKeys.AntiIceEng2) is double e2 && read(A300FoKeys.WingSupply) is double wing
            ? $"engine 1 {OnOff(e1)}, engine 2 {OnOff(e2)}, wing {OnOff(wing)}"
            : null;

    public static string? Speeds(Func<string, double?> read)
    {
        var parts = new List<string>();
        double v1 = read(A300TakeoffCallouts.V1Key) ?? 0, vr = read(A300TakeoffCallouts.VrKey) ?? 0;
        double v2 = A300TakeoffCallouts.PlausibleV2(vr, read(A300TakeoffCallouts.V2Key) ?? 0);
        if (v1 > 0) parts.Add($"V1 {Math.Round(v1).ToString(Inv)}");
        if (vr > 0) parts.Add($"VR {Math.Round(vr).ToString(Inv)}");
        if (v2 > 0) parts.Add($"V2 {Math.Round(v2).ToString(Inv)}");
        if (read(A300FoKeys.FlexTemperature) is double flex && flex > 0) parts.Add($"flex {Math.Round(flex).ToString(Inv)} degrees");
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    public static string? Transponder(Func<string, double?> read)
    {
        string? mode = read(A300FoKeys.Transponder) switch
        {
            double m when Math.Round(m) == 0 => "standby",
            double m when Math.Round(m) == 1 => "XPDR",
            double m when Math.Round(m) == 2 => "TA/RA",
            double m when Math.Round(m) == 3 => "TA only",
            _ => null,
        };
        return mode != null && Readout(read, A300FoKeys.Squawk) is string code ? $"{code}, {mode}" : null;
    }

    public static string? Ignition(Func<string, double?> read) => read(A300FoKeys.Ignition) is double v
        ? Math.Round(v) switch { 0 => "start B", 1 => "start A", 2 => "crank", 3 => "off", 4 => "continuous relight", _ => null }
        : null;

    public static string? Autobrake(Func<string, double?> read) => read(A300FoKeys.AutobrakeLevel) is double v
        ? Math.Round(v) switch { 0 => "off", 1 => "low", 2 => "medium", 3 => "max", _ => null }
        : null;

    public static string? ApuBleed(Func<string, double?> read) =>
        read(A300FoKeys.ApuBleed) is double v ? OnOff(v) : null;
}
