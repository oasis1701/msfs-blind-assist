using System.Globalization;

namespace MSFSBlindAssist.Services;

/// <summary>The phase F names, in the words visual guidance speaks it.</summary>
internal enum VisualGuidanceReadoutPhase { Starting, Deciding, Joining, Vectoring, OnFinal, Flare, Touchdown }

/// <summary>A warning about the path that F repeats in place of the height (stage 5, plan Task 6).</summary>
internal enum VisualGuidanceVerticalWarning { None, HighAtFinal, TooHigh, TooLow }

/// <summary>
/// What F reads (VG-22). <paramref name="CrossTrackNm"/> is positive right of the centreline facing
/// the runway; <paramref name="AltitudeErrorFt"/> positive above the path; <paramref name="TargetFpm"/>
/// the descent rate the guidance asks for (negative down). Joining and vectoring: <paramref name="Leg"/>
/// the leg being flown; <paramref name="InterceptDeg"/> the intercept toward final; <paramref name="Turning"/>
/// once the turn onto final has come; <paramref name="CrossesFinal"/> whether the heading flown crosses
/// final (joining); <paramref name="JoinAlongNm"/> where the aircraft will be lined up on final, nm from
/// the threshold; <paramref name="SecondsToTurn"/> the time to the turn onto final, if known;
/// <paramref name="CentrelineBearingDeg"/> the bearing of the extended centreline's nearest point from the aircraft's
/// track, degrees, + right (NaN unknown);
/// <paramref name="JoinPathMslFt"/> the path's altitude (MSL) at the join point, null for no vertical half;
/// <paramref name="JoinHeightErrorFt"/> how far above it the aircraft is now (+ above);
/// <paramref name="Warning"/> a warning F says in place of the height.
/// </summary>
internal readonly record struct VisualGuidanceStatus(VisualGuidanceReadoutPhase Phase, double DistanceNm, double CrossTrackNm,
    double AltitudeErrorFt, double TargetFpm, double WheelHeightFt,
    VisualGuidanceLegKind Leg = VisualGuidanceLegKind.Final, double InterceptDeg = 0, bool Turning = false,
    bool CrossesFinal = true, double JoinAlongNm = 0, double? SecondsToTurn = null, double CentrelineBearingDeg = double.NaN,
    double? JoinPathMslFt = null, double JoinHeightErrorFt = 0, VisualGuidanceVerticalWarning Warning = VisualGuidanceVerticalWarning.None,
    VisualGuidanceFlareCall FlareCall = VisualGuidanceFlareCall.None);

/// <summary>The flare's call already said on final, before the flare starts: F repeats it (VG-22).</summary>
internal enum VisualGuidanceFlareCall { None, Prepare, Flare }

/// <summary>
/// Composes F's sentence: the phase first, then where the aircraft is sideways, then where it is
/// vertically, for example "On final, 6.2 miles. On the centreline. 40 feet high, target 750 down." or
/// "Joining a 6.4 mile final. 30 degree intercept, turn in about 85 seconds. Path there 2,470 feet,
/// you're 300 high." Before final the vertical half is the path's altitude at the join point (MSL, what
/// the altimeter and ATC use) and the height against it now; vectoring adds BA's target, joining none,
/// since the altitude is ATC's or the pilot's there (plan decision 8). Also the lines BA speaks on its
/// own about the path. Every number is culture-invariant, thousands with a comma ("2,260").
/// </summary>
internal static class VisualGuidanceStatusText
{
    private const double OnCentrelineNm = 0.05;
    /// <summary>The centreline is "ahead" within this of the nose and "behind" within this of the tail.</summary>
    public const double AheadBehindDeg = 30.0;
    private const double OnPathFt = VisualGuidanceVertical.OnPathFt;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Compose(in VisualGuidanceStatus s) => s.Phase switch
    {
        VisualGuidanceReadoutPhase.Deciding => "Visual guidance starting, waiting for wings level.",
        VisualGuidanceReadoutPhase.Joining => Joining(s),
        VisualGuidanceReadoutPhase.Vectoring => Vectoring(s),
        VisualGuidanceReadoutPhase.OnFinal =>
            $"On final, {Tenths(s.DistanceNm)} miles. {Side(s.CrossTrackNm)}. {Vertical(s)}.{FlareCallText(s.FlareCall)}",
        VisualGuidanceReadoutPhase.Flare =>
            $"Flare, {Math.Max(0, Math.Round(s.WheelHeightFt, MidpointRounding.AwayFromZero)).ToString("0", Inv)} feet. {Side(s.CrossTrackNm)}. {Capitalised(Target(s.TargetFpm))}.",
        VisualGuidanceReadoutPhase.Touchdown =>
            $"Touchdown. {Side(s.CrossTrackNm)}.",
        _ => "Visual guidance starting.",
    };

    // " Prepare to flare." or " Flare." after the final sentence, once BA has said it; nothing before.
    private static string FlareCallText(VisualGuidanceFlareCall call) => call switch
    {
        VisualGuidanceFlareCall.Prepare => " Prepare to flare.",
        VisualGuidanceFlareCall.Flare => " Flare.",
        _ => "",
    };

    /// <summary>"You'll be 1,700 feet high at final": joining, once level (plan decision 10).</summary>
    public static string HighAtFinalLine(double heightAboveFt) => $"You'll be {Feet(heightAboveFt)} feet high at final";

    /// <summary>"Too high, 600 feet above the path": on final (plan decision 3).</summary>
    public static string TooHighLine(double errorFt) => $"Too high, {Feet(errorFt)} feet above the path";

    /// <summary>"Too low, 400 feet below the path": on final (plan decision 5).</summary>
    public static string TooLowLine(double errorFt) => $"Too low, {Feet(errorFt)} feet below the path";

    /// <summary>
    /// A mile call: "3 miles out", and once established with the path, "3 miles out, 80 feet high" or
    /// "5 miles out, on path" (plan decision 11).
    /// </summary>
    public static string MileLine(double markNm, double? errorFt)
    {
        string mark = markNm == 1 ? "1 mile out" : $"{markNm.ToString("0", Inv)} miles out";
        if (errorFt is not double e) return mark;
        return Math.Abs(e) <= OnPathFt ? $"{mark}, on path" : $"{mark}, {Feet(e)} feet {(e > 0 ? "high" : "low")}";
    }

    private static string Joining(in VisualGuidanceStatus s)
    {
        if (s.Turning) return $"Joining final, turning onto final. {CentrelineFromYou(s)}.{PathThere(s, withTarget: false)}";
        if (!s.CrossesFinal) return $"Joining final. Heading does not cross final. {CentrelineFromYou(s)}.";
        return $"Joining {MileFinal(s.JoinAlongNm)}. {FivesAtLeastFive(s.InterceptDeg)} degree intercept{TurnIn(s)}."
            + PathThere(s, withTarget: false);
    }

    // "a 6.4 mile final", "an 8.8 mile final": "an" before a number spoken with a vowel sound (eight,
    // eighty-, eleven, eighteen).
    private static string MileFinal(double nm)
    {
        string number = Tenths(nm);
        string whole = number.Split('.')[0];
        bool vowel = whole.StartsWith('8') || whole is "11" or "18";
        return $"{(vowel ? "an" : "a")} {number} mile final";
    }

    // "A 6.4 mile final": the length of final the aircraft will be lined up on, in the pilot's and ATC's words
    // (the owner, 2026-11-06: "Joining final 6.4 miles out" was vague). The pattern names its leg after it: the
    // downwind has no time to the turn onto final yet; the base counts down to it.
    private static string Vectoring(in VisualGuidanceStatus s) => s.Leg switch
    {
        VisualGuidanceLegKind.Downwind =>
            $"Vectoring to {MileFinal(s.JoinAlongNm)}, downwind. {CentrelineFromYou(s)}.{PathThere(s, withTarget: true)}",
        VisualGuidanceLegKind.Base =>
            $"Vectoring to {MileFinal(s.JoinAlongNm)}, base. {CentrelineFromYou(s)}{TurnIn(s)}.{PathThere(s, withTarget: true)}",
        _ => s.Turning
            ? $"Vectoring to final, turning onto final. {CentrelineFromYou(s)}.{PathThere(s, withTarget: true)}"
            : $"Vectoring to {MileFinal(s.JoinAlongNm)}. {CentrelineFromYou(s)}{TurnIn(s)}.{PathThere(s, withTarget: true)}",
    };

    // " Path there 2,470 feet, you're 300 high[, target 800 down]." with a leading space; nothing without a join point.
    private static string PathThere(in VisualGuidanceStatus s, bool withTarget)
    {
        if (s.JoinPathMslFt is not double pathFt) return "";
        double e = s.JoinHeightErrorFt;
        string where = s.Warning == VisualGuidanceVerticalWarning.HighAtFinal ? $"you'll be {Feet(e)} feet high at final"
            : Math.Abs(e) <= OnPathFt ? "you're on it"
            : $"you're {Feet(e)} {(e > 0 ? "high" : "low")}";
        return $" Path there {Feet(pathFt)} feet, {where}{(withTarget ? ", " + Target(s.TargetFpm) : "")}.";
    }

    private static string TurnIn(in VisualGuidanceStatus s)
        => s.SecondsToTurn is double t ? $", turn in about {FivesAtLeastFive(t)} seconds" : "";

    private static string Tenths(double v) => Math.Round(v, 1, MidpointRounding.AwayFromZero).ToString("0.0", Inv);

    private static string FivesAtLeastFive(double v)
        => Math.Max(5, RoundTo(v, 5)).ToString("#,0", Inv);

    // Sideways on final, in the flare and at touchdown: the side facing the runway, "0.1 miles left of the centreline".
    private static string Side(double crossTrackNm)
        => SidewaysNm(crossTrackNm) is double nm
            ? $"{nm.ToString("0.0", Inv)} miles {(crossTrackNm > 0 ? "right" : "left")} of the centreline"
            : "On the centreline";

    // Sideways while joining or vectoring: where the centreline is from the cockpit, "Centreline 6.1 miles to your
    // left", "ahead" or "behind" (the owner, 2026-11-07: "6.1 miles left of final" was heard as 6.1 miles of final
    // left to fly).
    private static string CentrelineFromYou(in VisualGuidanceStatus s)
    {
        if (SidewaysNm(s.CrossTrackNm) is not double nm) return "On the centreline";
        string miles = nm.ToString("0.0", Inv);
        double b = s.CentrelineBearingDeg;
        if (double.IsNaN(b)) return $"{miles} miles from the centreline";
        double size = Math.Abs(VisualGuidanceJoin.Normalize(b));
        string where = size <= AheadBehindDeg ? "ahead"
            : size >= 180.0 - AheadBehindDeg ? "behind"
            : VisualGuidanceJoin.Normalize(b) > 0 ? "to your right" : "to your left";
        return $"Centreline {miles} miles {where}";
    }

    // The sideways distance to say, to a tenth and at least 0.1; null within OnCentrelineNm.
    private static double? SidewaysNm(double crossTrackNm)
    {
        double size = Math.Abs(crossTrackNm);
        return size <= OnCentrelineNm ? null : Math.Max(0.1, Math.Round(size, 1, MidpointRounding.AwayFromZero));
    }

    // On final: the height against the path, or the warning in its place, then the target.
    private static string Vertical(in VisualGuidanceStatus s)
    {
        double e = s.AltitudeErrorFt;
        string where = s.Warning switch
        {
            VisualGuidanceVerticalWarning.TooHigh => TooHighLine(e),
            VisualGuidanceVerticalWarning.TooLow => TooLowLine(e),
            _ => Math.Abs(e) <= OnPathFt ? "On path" : $"{Feet(e)} feet {(e > 0 ? "high" : "low")}",
        };
        return $"{where}, {Target(s.TargetFpm)}";
    }

    private static string Target(double targetFpm)
    {
        double fpm = RoundTo(Math.Abs(targetFpm), 50);
        return fpm == 0 ? "target level" : $"target {fpm.ToString("#,0", Inv)} down";
    }

    // The flare's target starts its own sentence (stage 6): "Target 350 down".
    private static string Capitalised(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // A height to 10 ft, thousands with a comma.
    private static string Feet(double ft) => RoundTo(Math.Abs(ft), 10).ToString("#,0", Inv);

    private static double RoundTo(double v, double step) => Math.Round(v / step, MidpointRounding.AwayFromZero) * step;
}
