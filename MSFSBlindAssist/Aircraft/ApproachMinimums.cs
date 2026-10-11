namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Sentinel decoding for the FBW plain-feet approach-minimums L:vars
/// (AIRLINER_MINIMUM_DESCENT_ALTITUDE = baro MDA, AIRLINER_DECISION_HEIGHT = radio DH),
/// shared by the A32NX and A380 definitions so the announce path (ProcessSimVarUpdate)
/// and the display path (TryGetDisplayOverride) can never disagree.
/// The unset sentinels differ per var: FBW resets MDA to 0 and DH to -1 — an MDA of 0
/// is "not set", but a DH of 0 is a VALID CAT III entry and must read as set.
/// </summary>
public static class ApproachMinimums
{
    /// <summary>Rounded feet, or -1 when unset (baro MDA unset when &lt;= 0; DH unset
    /// when &lt; 0 — DH 0 is a valid CAT III entry).</summary>
    public static int ToFeet(bool isDecisionHeight, double value)
        => (isDecisionHeight ? value >= 0 : value > 0) ? (int)Math.Round(value) : -1;

    /// <summary>Panel display text: "N feet" or "Not set".</summary>
    public static string DisplayText(bool isDecisionHeight, double value)
    {
        int ft = ToFeet(isDecisionHeight, value);
        return ft >= 0 ? $"{ft} feet" : "Not set";
    }

    /// <summary>
    /// What to say when a minimum reads <paramref name="feet"/> (<see cref="ToFeet"/>; -1 = unset)
    /// after <paramref name="previousFeet"/> (-1 or -2 = nothing set or seen), or null. A set or
    /// changed minimum is spoken (one set at connect too), and so is a clear: the FMS clears both
    /// when the approach changes (FlightPlan.setApproach, FBW #10724), a change the pilot did not
    /// make. <paramref name="reseeding"/>: a flight load or reconnect is still settling, when the
    /// FMS starting over writes MDA 0 and DH -1, so a clear then is the new situation, not news.
    /// </summary>
    public static string? Announcement(bool isDecisionHeight, int previousFeet, int feet, bool reseeding = false)
    {
        if (feet == previousFeet) return null;
        string name = isDecisionHeight ? "Decision height" : "Baro minimum";
        if (feet >= 0) return $"{name} {feet} feet";
        return previousFeet >= 0 && !reseeding ? $"{name} cleared" : null;
    }
}
