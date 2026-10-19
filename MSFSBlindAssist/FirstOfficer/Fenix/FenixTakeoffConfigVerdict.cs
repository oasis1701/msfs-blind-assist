namespace MSFSBlindAssist.FirstOfficer.Fenix;

/// <summary>
/// What a Fenix ECAM TO CONFIG test says, from the master warning and master caution read while the
/// test button is still held; the First Officer and the panel's TO CONFIG button speak the same words.
/// The Fenix exposes no TO-CONFIG-only indicator (live L:var search, 2026-07), so the shared lights are
/// the signal, read the fail-safe way: any light lit is a "check", never masked by a pre-press
/// baseline. The caution counts too: a takeoff-config fault that is amber (speeds not inserted, brakes
/// hot, a door) lights the master caution, not the warning, and was once spoken as "normal". An unread
/// light is never "normal": it once defaulted to 0 (after a reconnect cleared the cache, or with the
/// First Officer window closed during the hold) and said "Takeoff config normal." over a bad config.
/// </summary>
public static class FenixTakeoffConfigVerdict
{
    public const string MasterWarningKey = "I_MIP_MASTER_WARNING_CAPT";
    public const string MasterCautionKey = "I_MIP_MASTER_CAUTION_CAPT";

    public static string Text(double? masterWarning, double? masterCaution)
    {
        if (masterWarning >= 0.5) return "Takeoff config: check configuration.";
        if (masterCaution >= 0.5) return "Takeoff config: caution. Check the ECAM.";
        if (masterWarning is not double w || double.IsNaN(w) || masterCaution is not double c || double.IsNaN(c))
            return "Takeoff config: no result read. Check the ECAM.";
        return "Takeoff config normal.";
    }
}
