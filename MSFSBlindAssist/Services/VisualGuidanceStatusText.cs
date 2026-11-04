using System.Globalization;

namespace MSFSBlindAssist.Services;

/// <summary>The phase F names, in the words visual guidance speaks it.</summary>
internal enum VisualGuidanceReadoutPhase { Starting, Extending, Intercepting, OnFinal, Flare, Touchdown }

/// <summary>
/// What F reads (VG-22). <paramref name="CrossTrackNm"/> is positive right of the centreline facing
/// the runway; <paramref name="AltitudeErrorFt"/> positive above the path; <paramref name="TargetFpm"/>
/// the descent rate the guidance asks for, 0 or less.
/// </summary>
internal readonly record struct VisualGuidanceStatus(VisualGuidanceReadoutPhase Phase, double DistanceNm, double CrossTrackNm,
    double AltitudeErrorFt, double TargetFpm, double WheelHeightFt, double ExtensionHeadingMag);

/// <summary>
/// Composes F's sentence: the phase first, then where the aircraft is sideways, then where it is
/// vertically, for example "On final, 6.2 miles. On the centreline. 40 feet high, target 750 down."
/// Every number is culture-invariant, thousands with a comma ("2,260").
/// </summary>
internal static class VisualGuidanceStatusText
{
    private const double OnCentrelineNm = 0.05;
    private const double OnPathFt = 30.0;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Compose(in VisualGuidanceStatus s) => s.Phase switch
    {
        VisualGuidanceReadoutPhase.Extending =>
            $"Extending, heading {Heading(s.ExtensionHeadingMag)}, {Tenths(s.DistanceNm)} miles behind the threshold. {Side(s.CrossTrackNm)}. {Vertical(s)}.",
        VisualGuidanceReadoutPhase.Intercepting =>
            $"Intercepting final, {Tenths(s.DistanceNm)} miles out. {Side(s.CrossTrackNm)}. {Vertical(s)}.",
        VisualGuidanceReadoutPhase.OnFinal =>
            $"On final, {Tenths(s.DistanceNm)} miles. {Side(s.CrossTrackNm)}. {Vertical(s)}.",
        VisualGuidanceReadoutPhase.Flare =>
            $"Flare, {Math.Max(0, Math.Round(s.WheelHeightFt, MidpointRounding.AwayFromZero)).ToString("0", Inv)} feet. {Side(s.CrossTrackNm)}.",
        VisualGuidanceReadoutPhase.Touchdown =>
            $"Touchdown. {Side(s.CrossTrackNm)}.",
        _ => "Visual guidance starting.",
    };

    private static string Tenths(double v) => Math.Round(v, 1, MidpointRounding.AwayFromZero).ToString("0.0", Inv);

    // Whole degrees, three digits, north as 360.
    private static string Heading(double degrees)
    {
        int h = (int)Math.Round(((degrees % 360) + 360) % 360, MidpointRounding.AwayFromZero) % 360;
        return (h == 0 ? 360 : h).ToString("000", Inv);
    }

    private static string Side(double crossTrackNm)
    {
        double size = Math.Abs(crossTrackNm);
        if (size <= OnCentrelineNm) return "On the centreline";
        double tenths = Math.Max(0.1, Math.Round(size, 1, MidpointRounding.AwayFromZero));
        return $"{tenths.ToString("0.0", Inv)} miles {(crossTrackNm > 0 ? "right" : "left")} of the centreline";
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
