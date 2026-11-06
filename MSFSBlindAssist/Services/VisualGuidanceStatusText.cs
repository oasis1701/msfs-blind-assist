using System.Globalization;

namespace MSFSBlindAssist.Services;

/// <summary>The phase F names, in the words visual guidance speaks it.</summary>
internal enum VisualGuidanceReadoutPhase { Starting, Deciding, Joining, Vectoring, OnFinal, Flare, Touchdown }

/// <summary>
/// What F reads (VG-22). <paramref name="CrossTrackNm"/> is positive right of the centreline facing
/// the runway; <paramref name="AltitudeErrorFt"/> positive above the path; <paramref name="TargetFpm"/>
/// the descent rate the guidance asks for, 0 or less. Joining and vectoring: <paramref name="Leg"/> the
/// leg being flown; <paramref name="InterceptDeg"/> the intercept toward final; <paramref name="Turning"/>
/// once the turn onto final has come; <paramref name="CrossesFinal"/> whether the heading flown crosses
/// final (joining); <paramref name="JoinAlongNm"/> where the aircraft will be lined up on final, nm from
/// the threshold; <paramref name="SecondsToTurn"/> the time to the turn onto final, if known;
/// <paramref name="FinalToTheRight"/> the side of the aircraft's track the nearest point of final lies on.
/// </summary>
internal readonly record struct VisualGuidanceStatus(VisualGuidanceReadoutPhase Phase, double DistanceNm, double CrossTrackNm,
    double AltitudeErrorFt, double TargetFpm, double WheelHeightFt,
    VisualGuidanceLegKind Leg = VisualGuidanceLegKind.Final, double InterceptDeg = 0, bool Turning = false,
    bool CrossesFinal = true, double JoinAlongNm = 0, double? SecondsToTurn = null, bool FinalToTheRight = false);

/// <summary>
/// Composes F's sentence: the phase first, then where the aircraft is sideways, then where it is
/// vertically, for example "On final, 6.2 miles. On the centreline. 40 feet high, target 750 down." or
/// "Joining final 6.4 miles out. 30 degree intercept, turn in about 85 seconds." Every number is
/// culture-invariant, thousands with a comma ("2,260"). Joining says no vertical half: the altitude is
/// ATC's or the pilot's there (the vertical path, stage 5 of the redesign, adds one). Nor do the
/// pattern's downwind and base, where the pitch tone sits on the aircraft's own pitch until stage 5.
/// </summary>
internal static class VisualGuidanceStatusText
{
    private const double OnCentrelineNm = 0.05;
    private const double OnPathFt = VisualGuidanceVertical.OnPathFt;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Compose(in VisualGuidanceStatus s) => s.Phase switch
    {
        VisualGuidanceReadoutPhase.Deciding => "Visual guidance starting, waiting for wings level.",
        VisualGuidanceReadoutPhase.Joining => Joining(s),
        VisualGuidanceReadoutPhase.Vectoring => Vectoring(s),
        VisualGuidanceReadoutPhase.OnFinal =>
            $"On final, {Tenths(s.DistanceNm)} miles. {Side(s.CrossTrackNm)}. {Vertical(s)}.",
        VisualGuidanceReadoutPhase.Flare =>
            $"Flare, {Math.Max(0, Math.Round(s.WheelHeightFt, MidpointRounding.AwayFromZero)).ToString("0", Inv)} feet. {Side(s.CrossTrackNm)}.",
        VisualGuidanceReadoutPhase.Touchdown =>
            $"Touchdown. {Side(s.CrossTrackNm)}.",
        _ => "Visual guidance starting.",
    };

    private static string Joining(in VisualGuidanceStatus s)
    {
        if (s.Turning) return $"Joining final, turning onto final. {FinalSide(s.CrossTrackNm)}.";
        if (!s.CrossesFinal) return $"Joining final. Heading does not cross final. {WhereFinalIs(s)}.";
        return $"Joining final {Tenths(s.JoinAlongNm)} miles out. {FivesAtLeastFive(s.InterceptDeg)} degree intercept{TurnIn(s)}.";
    }

    // The pattern names its leg: the downwind has no time to the turn onto final yet; the base counts down to it.
    private static string Vectoring(in VisualGuidanceStatus s) => s.Leg switch
    {
        VisualGuidanceLegKind.Downwind =>
            $"Vectoring to final, downwind, joining {Tenths(s.JoinAlongNm)} miles out. {FinalSide(s.CrossTrackNm)}.",
        VisualGuidanceLegKind.Base =>
            $"Vectoring to final, base, joining {Tenths(s.JoinAlongNm)} miles out. {FinalSide(s.CrossTrackNm)}{TurnIn(s)}.",
        _ => s.Turning
            ? $"Vectoring to final, turning onto final. {FinalSide(s.CrossTrackNm)}. {Vertical(s)}."
            : $"Vectoring to final, joining {Tenths(s.JoinAlongNm)} miles out. {FinalSide(s.CrossTrackNm)}{TurnIn(s)}. {Vertical(s)}.",
    };

    private static string TurnIn(in VisualGuidanceStatus s)
        => s.SecondsToTurn is double t ? $", turn in about {FivesAtLeastFive(t)} seconds" : "";

    private static string WhereFinalIs(in VisualGuidanceStatus s)
        => $"Final {Tenths(Math.Abs(s.CrossTrackNm))} miles to your {(s.FinalToTheRight ? "right" : "left")}";

    private static string Tenths(double v) => Math.Round(v, 1, MidpointRounding.AwayFromZero).ToString("0.0", Inv);

    private static string FivesAtLeastFive(double v)
        => Math.Max(5, RoundTo(v, 5)).ToString("#,0", Inv);

    // Sideways on final: "of the centreline".
    private static string Side(double crossTrackNm) => Sideways(crossTrackNm, "the centreline");

    // Sideways while joining or vectoring: "of final".
    private static string FinalSide(double crossTrackNm) => Sideways(crossTrackNm, "final");

    private static string Sideways(double crossTrackNm, string of)
    {
        double size = Math.Abs(crossTrackNm);
        if (size <= OnCentrelineNm) return "On the centreline";
        double tenths = Math.Max(0.1, Math.Round(size, 1, MidpointRounding.AwayFromZero));
        return $"{tenths.ToString("0.0", Inv)} miles {(crossTrackNm > 0 ? "right" : "left")} of {of}";
    }

    private static string Vertical(in VisualGuidanceStatus s)
    {
        string where = Math.Abs(s.AltitudeErrorFt) <= OnPathFt
            ? "On path"
            : $"{RoundTo(Math.Abs(s.AltitudeErrorFt), 10).ToString("#,0", Inv)} feet {(s.AltitudeErrorFt > 0 ? "high" : "low")}";
        double fpm = RoundTo(Math.Abs(s.TargetFpm), 50);
        return fpm == 0 ? $"{where}, target level" : $"{where}, target {fpm.ToString("#,0", Inv)} down";
    }

    private static double RoundTo(double v, double step) => Math.Round(v / step, MidpointRounding.AwayFromZero) * step;
}
