namespace MSFSBlindAssist.Navigation;

/// <summary>
/// When a second Continue counts as "cleared across both" for a paired-runway crossing
/// (<c>TaxiGuidanceManager.ContinuePastHoldShort</c>). The window opens a fixed delay after the
/// offer, never at once, so a key bounce or a quick double press cannot clear the second runway's
/// stop before the pilot has heard the offer (PR #324 review).
/// </summary>
public static class PairedCrossingHoldWindow
{
    /// <summary>True when <paramref name="now"/> lies inside [<paramref name="from"/>, <paramref name="until"/>].</summary>
    public static bool Accepts(DateTime now, DateTime from, DateTime until)
        => now >= from && now <= until;
}
