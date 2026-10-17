using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Airbus;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.FBWA380;

/// <summary>
/// The value text the FlyByWire A380X First Officer speaks for a read-back line whose ECL answer is a
/// blank ("FLAPS SETTING ... CONF ____"), where the A380's inputs differ from the A320 family's. The
/// shared pieces (fuel quantity, takeoff speeds and flex, parking brake, the baro text itself, the
/// rounded-index helper) stay in <see cref="AirbusReadbackFormat"/> and are called from here, not
/// copied. Pure and culture-invariant: CI runs en-US, and a comma-decimal culture would otherwise leak
/// into speech. Null means "nothing to say": the line then speaks as it does without a value.
/// Encodings, from FlyByWire's A380X at 255546cb (docs/invariants/first-officer-airbus.md#foa-9):
/// - the armed autobrake mode, <c>A32NX_AUTOBRAKES_ARMED_MODE</c>: 0 DISARM, 1 BTV, 2 LOW, 3 L2, 4 L3,
///   5 HIGH, 6 RTO (a380_systems/src/hydraulic/autobrakes.rs, <c>A380AutobrakeMode</c>, L50-59);
/// - the flap lever, <c>A32NX_FLAPS_HANDLE_INDEX</c>: 0..4, lever 1 read as CONF 1+F, the way
///   FlyByWire's own warning system reads it on the ground (FwsCore.ts L4781);
/// - the FCU's baro word, <c>A32NX_FCU_{LEFT,RIGHT}_EIS_BARO_HPA</c>: an ARINC 429 word in hectopascals,
///   decoded by the A380 definition's own <c>BaroHpa</c> (800..1100 is hPa, 22..33 is inHg), which
///   <see cref="BaroHpaFromWord"/> calls, so the two can never decode it differently;
/// - approach minimums: the plain-feet L:vars (A380F-15) with FlyByWire's unset values
///   (<see cref="ApproachMinimums"/>).
/// </summary>
public static class FbwA380ReadbackFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Hectopascals per inch of mercury: the A380 definition's own conversion (it writes the
    /// literal in <c>BaroHpa</c> and in its altimeter phrase).</summary>
    private const double HpaPerInHg = 33.8639;

    /// <summary>The armed autobrake mode: "off", "brake to vacate", "low", "L2", "L3", "high" or "RTO".</summary>
    public static string? Autobrake(double armedMode) => AirbusReadbackFormat.Index(armedMode, 6) switch
    {
        0 => "off", 1 => "brake to vacate", 2 => "low", 3 => "L2", 4 => "L3", 5 => "high", 6 => "RTO", _ => null,
    };

    /// <summary>The flap lever in CONF terms: "CONF 0", "CONF 1 plus F", "CONF 2", "CONF 3", "CONF full".</summary>
    public static string? FlapsConf(double handleIndex) => AirbusReadbackFormat.Index(handleIndex, 4) switch
    {
        0 => "CONF 0", 1 => "CONF 1 plus F", 2 => "CONF 2", 3 => "CONF 3", 4 => "CONF full", _ => null,
    };

    /// <summary>Engine anti-ice on all four engines and wing anti-ice: "engine anti-ice off, wing
    /// anti-ice off" when the four engines agree, each engine by number when they do not; null when
    /// any switch is unknown.</summary>
    public static string? AntiIce(double eng1, double eng2, double eng3, double eng4, double wing)
    {
        string?[] engines =
        {
            AirbusReadbackFormat.OnOff(eng1), AirbusReadbackFormat.OnOff(eng2),
            AirbusReadbackFormat.OnOff(eng3), AirbusReadbackFormat.OnOff(eng4),
        };
        string? w = AirbusReadbackFormat.OnOff(wing);
        if (w == null || engines.Any(e => e == null)) return null;
        string engineText = engines.Distinct().Count() == 1
            ? "engine anti-ice " + engines[0]
            : string.Join(", ", engines.Select((e, i) => string.Format(Inv, "engine {0} anti-ice {1}", i + 1, e)));
        return engineText + ", wing anti-ice " + w;
    }

    /// <summary>The hectopascals an A380 FCU baro word carries; NaN when it is unread, carries no data
    /// (failure warning, no computed data, or the 0 a never-written L:var reads) or is out of range. It
    /// calls the A380 definition's own <c>BaroHpa</c> after the same
    /// <c>new Arinc429Word(value).ValueOr(0)</c> that definition's call sites apply, so this decode is
    /// the pilot's altimeter read-out's decode, not a copy of it.</summary>
    public static double BaroHpaFromWord(double raw)
    {
        if (double.IsNaN(raw)) return double.NaN;
        return FlyByWireA380Definition.BaroHpa(new Arinc429Word(raw).ValueOr(0), out double hpa) ? hpa : double.NaN;
    }

    /// <summary>"standard", "QNH 1013" or "QNH 29.92", from the FCU's STD flag, its baro word and the
    /// EFIS-CP unit selector (1 = inHg, A380F-2); null when any input it needs is unknown.</summary>
    public static string? Baro(double stdFlag, double baroWord, double inHgFlag)
    {
        if (double.IsNaN(stdFlag)) return null;
        if (stdFlag > 0.5) return AirbusReadbackFormat.Baro(1, double.NaN, false);
        double hpa = BaroHpaFromWord(baroWord);
        if (double.IsNaN(hpa) || double.IsNaN(inHgFlag)) return null;
        bool inHg = inHgFlag > 0.5;
        return AirbusReadbackFormat.Baro(0, inHg ? hpa / HpaPerInHg : hpa, inHg);
    }

    /// <summary>The approach minimum: "baro 940 feet", "radio 200 feet", both when both are set,
    /// "not set" when neither is; null when either is unread. Baro MDA is unset at 0 or below, radio DH
    /// below 0 (a DH of 0 is a CAT III entry): <see cref="ApproachMinimums"/>.</summary>
    public static string? Minimum(double baroMda, double radioDh)
    {
        if (double.IsNaN(baroMda) || double.IsNaN(radioDh)) return null;
        int mda = ApproachMinimums.ToFeet(isDecisionHeight: false, baroMda);
        int dh = ApproachMinimums.ToFeet(isDecisionHeight: true, radioDh);
        var parts = new List<string>();
        if (mda >= 0) parts.Add("baro " + mda.ToString(Inv) + " feet");
        if (dh >= 0) parts.Add("radio " + dh.ToString(Inv) + " feet");
        return parts.Count == 0 ? "not set" : string.Join(", ", parts);
    }
}
