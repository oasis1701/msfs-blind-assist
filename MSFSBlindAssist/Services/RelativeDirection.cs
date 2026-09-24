namespace MSFSBlindAssist.Services;

/// <summary>
/// The one relative-bearing phrasing in the app, so the surroundings readout ("Concourse B,
/// ahead and to the right") and ground traffic (GroundTrafficLogic.DescribeDirection
/// delegates here) say the same words for the same angle. Thresholds pinned by
/// RelativeDirectionTests.
/// </summary>
public static class RelativeDirection
{
    public static double Normalize360(double deg) => ((deg % 360.0) + 360.0) % 360.0;

    /// <param name="relBearingDeg">0 = dead ahead, 90 = hard right, 180 = dead behind; any range.</param>
    public static string Describe(double relBearingDeg)
    {
        double rel = Normalize360(relBearingDeg);
        bool right = rel < 180.0;
        double abs = right ? rel : (360.0 - rel);

        if (abs <= 20.0) return "ahead";
        if (abs <= 70.0) return right ? "ahead and to the right" : "ahead and to the left";
        if (abs <= 110.0) return right ? "to the right" : "to the left";
        if (abs <= 160.0) return right ? "behind and to the right" : "behind and to the left";
        return "behind";
    }

    /// <summary>Side only — the passing-callout form ("Passing Concourse B, on the left.").</summary>
    public static string Side(double relBearingDeg)
    {
        double rel = Normalize360(relBearingDeg);
        bool right = rel < 180.0;
        double abs = right ? rel : (360.0 - rel);
        if (abs <= 20.0) return "ahead";
        if (abs >= 160.0) return "behind";
        return right ? "on the right" : "on the left";
    }
}
