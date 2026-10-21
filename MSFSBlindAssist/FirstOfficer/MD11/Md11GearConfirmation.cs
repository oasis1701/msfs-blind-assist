using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.FirstOfficer.MD11;

/// <summary>
/// The TFDi MD-11's landing gear confirmed from the legs, never the lever alone (FO-13). The MD-11
/// shows its gear on the system display and has no gear lamp the First Officer can read, so the
/// After Takeoff and Before Landing read-only gear checks read the lever (TFDi's own DOWN threshold,
/// <see cref="Md11GearLever"/>) AND each stock leg's extension in percent, the simvars the A380's and
/// the PMDG 777's gear rows read: UP = lever up AND every leg below 5 %; DOWN = lever down AND every
/// leg above 95 %. NaN whenever the lever or any leg is unread, so an unread field never confirms.
/// Until 2026-10-18 the gear lines read the lever alone, and "Gear: Down" latched over gear still
/// in transit. The stock legs are unmeasured on this airframe: the in-sim plan reads them.
/// </summary>
public static class Md11GearConfirmation
{
    public const string UpField = "FO_GEAR_UP_CONFIRMED";
    public const string DownField = "FO_GEAR_DOWN_CONFIRMED";

    /// <summary>The lever, then the legs.</summary>
    public static IReadOnlyList<string> Inputs { get; } =
        new[] { Md11GearLever.Key }.Concat(TFDiMD11Definition.FoGearLegKeys).ToArray();

    private const double UpBelowPercent = 5.0;
    private const double DownAbovePercent = 95.0;

    public static double UpValue(Func<string, double> read)
    {
        double lever = read(Md11GearLever.Key);
        var legs = TFDiMD11Definition.FoGearLegKeys.Select(read).ToList();
        if (double.IsNaN(lever) || legs.Any(double.IsNaN)) return double.NaN;
        return !Md11GearLever.IsDown(lever) && legs.All(l => l < UpBelowPercent) ? 1.0 : 0.0;
    }

    public static double DownValue(Func<string, double> read)
    {
        double lever = read(Md11GearLever.Key);
        var legs = TFDiMD11Definition.FoGearLegKeys.Select(read).ToList();
        if (double.IsNaN(lever) || legs.Any(double.IsNaN)) return double.NaN;
        return Md11GearLever.IsDown(lever) && legs.All(l => l > DownAbovePercent) ? 1.0 : 0.0;
    }
}
