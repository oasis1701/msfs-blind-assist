using System.Globalization;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// The sentences of the ILS guidance report (output Ctrl+I). The geometry is worked out in
/// <c>SimConnectManager.ProcessILSGuidance</c>; this only words it, with every number read
/// with a dot whatever the Windows number format.
///
/// <para>A localizer without a glideslope, an LDA or an IGS opens with its own line,
/// "Localizer 108.70, course 152.", named by <see cref="LandingAidText"/> as the EFB names it,
/// and a localizer gets no glideslope deviation: there is no glideslope to be above or below
/// (ESST 16 used to get one, from a default 3 degree slope). A plain ILS opens straight with
/// the distance, as before.</para>
///
/// <para>On the far side of the threshold the report gives the distance and the range
/// warning too. Inside the aid's published range it says the heading to extend outbound;
/// beyond it, that heading means nothing (it was said from far outside ESOH 18's range), so
/// it gives the threshold's bearing instead.</para>
/// </summary>
public static class IlsGuidanceText
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>True when the aid is anything but a plain ILS: a localizer, LDA or IGS.</summary>
    public static bool NeedsOpening(ILSData ils)
        => !ils.HasGlideslope || LandingAidText.OffsetFacility(ils.Type) != null;

    /// <summary>"Localizer 108.70, course 152. " for an aid that is not a plain ILS, else "".
    /// The course is the localizer's own magnetic course, from its own variation when the row
    /// has one, else <paramref name="fallbackMagVar"/>.</summary>
    public static string Opening(ILSData ils, double fallbackMagVar)
    {
        if (!NeedsOpening(ils))
            return "";
        int course = LandingAidText.MagneticCourse(ils.LocalizerHeading, ils.MagVar ?? fallbackMagVar);
        return LandingAidText.Describe(ils.HasGlideslope, ils.Type, ils.Frequency, course, ils.GlideslopePitch) + ". ";
    }

    /// <summary>True when the aid has a published range and the aircraft is beyond it.</summary>
    public static bool IsBeyondRange(ILSData ils, double distanceToThresholdNm)
        => ils.Range > 0 && distanceToThresholdNm > ils.Range;

    /// <summary>"Warning: ILS signal range is 18 nautical miles. " when beyond range, else "".</summary>
    public static string RangeWarning(ILSData ils, double distanceToThresholdNm)
        => IsBeyondRange(ils, distanceToThresholdNm)
            ? $"Warning: {LandingAidText.Name(ils.HasGlideslope, ils.Type)} signal range is {ils.Range.ToString(Inv)} nautical miles. "
            : "";

    /// <summary>" 120 feet below glideslope." when the aid has a glideslope and a deviation was
    /// worked out, else "".</summary>
    public static string Glideslope(ILSData ils, double? deviationFeet)
    {
        if (!ils.HasGlideslope || deviationFeet is not double dev)
            return "";
        string direction = dev > 0 ? "above" : "below";
        return $" {Math.Abs(dev).ToString("F0", Inv)} feet {direction} glideslope.";
    }

    /// <summary>The far side of the threshold: "Opposite side of runway 18, 24.3 nautical miles
    /// from threshold. Threshold bearing 008." beyond range, "… Fly heading 352 to extend
    /// outbound." inside it.</summary>
    public static string Behind(ILSData ils, double fallbackMagVar, string runwayId,
        double distanceToThresholdNm, double extensionHeadingMag, double thresholdBearingMag)
    {
        string advice = IsBeyondRange(ils, distanceToThresholdNm)
            ? $"Threshold bearing {Heading(thresholdBearingMag)}."
            : $"Fly heading {Heading(extensionHeadingMag)} to extend outbound.";
        return $"{Opening(ils, fallbackMagVar)}{RangeWarning(ils, distanceToThresholdNm)}" +
               $"Opposite side of runway {runwayId}, {Nm(distanceToThresholdNm)} nautical miles from threshold. {advice}";
    }

    /// <summary>"12.0 nautical miles from threshold, on centerline. Runway heading 171."</summary>
    public static string OnCenterline(ILSData ils, double fallbackMagVar, double distanceToThresholdNm,
        double? glideslopeDeviationFeet, double runwayHeadingMag)
        => $"{Opening(ils, fallbackMagVar)}{RangeWarning(ils, distanceToThresholdNm)}" +
           $"{Nm(distanceToThresholdNm)} nautical miles from threshold, on centerline." +
           $"{Glideslope(ils, glideslopeDeviationFeet)} Runway heading {Heading(runwayHeadingMag)}.";

    /// <summary>Off centerline, with the three intercept headings.</summary>
    public static string OffCenterline(ILSData ils, double fallbackMagVar, double distanceToThresholdNm,
        double distanceToCenterlineNm, double angularDeviationDeg, bool left, double? glideslopeDeviationFeet,
        double heading60, double heading45, double heading30)
    {
        string side = left ? "left" : "right";
        return $"{Opening(ils, fallbackMagVar)}{RangeWarning(ils, distanceToThresholdNm)}" +
               $"{Nm(distanceToThresholdNm)} nautical miles from threshold, " +
               $"{Nm(distanceToCenterlineNm)} nautical miles {side} of centerline, " +
               $"{angularDeviationDeg.ToString("F0", Inv)} degrees {side} of centerline." +
               $"{Glideslope(ils, glideslopeDeviationFeet)} " +
               $"Fly heading {Heading(heading60)} for 60 degree intercept, " +
               $"{Heading(heading45)} for 45 degree intercept, " +
               $"or {Heading(heading30)} for 30 degree intercept.";
    }

    private static string Nm(double nm) => nm.ToString("F1", Inv);

    private static string Heading(double degrees) => degrees.ToString("000", Inv);
}
