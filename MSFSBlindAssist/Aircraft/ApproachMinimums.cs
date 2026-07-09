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
}
