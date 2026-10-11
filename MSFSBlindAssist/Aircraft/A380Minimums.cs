namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The A380's approach-minimum read-out and call-out, over the shared
/// <see cref="ApproachMinimums"/> decode of AIRLINER_MINIMUM_DESCENT_ALTITUDE (baro) and
/// AIRLINER_DECISION_HEIGHT (radio).
///
/// A DH of 0 is NO DH here: the MFD parses "NO"/"NODH" to RADIO_ALTITUDE_NODH_VALUE = 0 and
/// takes no typed 0 (minimum 1), and the PFD shows NO DH for it (FBW DataEntryFormats.tsx,
/// FmcAircraftInterface.ts). The A32NX accepts a typed 0, so the shared decode keeps "0 feet".
///
/// A cleared minimum is spoken: since FBW #10929 the L:vars mirror the active plan, so the FMS's
/// own clear of both minimums when the approach changes (FlightPlan.setApproach, #10724) reaches
/// them — a change the pilot did not make.
/// </summary>
public static class A380Minimums
{
    public const string NoDh = "No DH";

    /// <summary>Panel text: "No DH", "N feet" or "Not set".</summary>
    public static string DisplayText(bool isDecisionHeight, double value)
        => isDecisionHeight && ApproachMinimums.ToFeet(true, value) == 0
            ? NoDh
            : ApproachMinimums.DisplayText(isDecisionHeight, value);

    /// <summary>
    /// <see cref="ApproachMinimums.Announcement"/>, with a DH of 0 spoken as NO DH (FBW's
    /// initSimVars writes MDA 0 and DH -1 when the MFD starts: the <paramref name="reseeding"/> case).
    /// </summary>
    public static string? Announcement(bool isDecisionHeight, int previousFeet, int feet, bool reseeding = false)
        => isDecisionHeight && feet == 0 && previousFeet != 0
            ? NoDh
            : ApproachMinimums.Announcement(isDecisionHeight, previousFeet, feet, reseeding);
}
