namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The A380's take-off roll callouts ("V1", "Rotate", "V2"): which variables feed
/// <see cref="TakeoffVSpeedCallouts"/> and which Ctrl+M row mutes each call — the MD-11's
/// <see cref="MD11.Md11TakeoffCallouts"/> for the FBW A380 (added 2026-09-25). The speeds are the
/// FMS's <c>L:AIRLINER_V1/VR/V2_SPEED</c> (knots; -1/0 when cleared — an FMS reset or a
/// departure-runway change sends them back for confirmation), which MSFSBA already reads and
/// announces as the pilot enters them (<c>PFD_V1</c>/<c>PFD_VR</c>/<c>PFD_V2</c>); airspeed comes
/// from a per-frame feed. The aircraft's own warning system also calls "V1" aloud; MSFSBA's call
/// arrives alongside it.
/// </summary>
public static class A380TakeoffCallouts
{
    /// <summary>The per-SIM_FRAME <c>AIRSPEED INDICATED</c> feed; consumed, never spoken, hidden from Ctrl+M.</summary>
    public const string IasKey = "A380_TAKEOFF_CALLOUT_IAS";

    public const string V1Key = "PFD_V1";
    public const string VrKey = "PFD_VR";
    public const string V2Key = "PFD_V2";

    /// <summary>The feed, the speeds and their Ctrl+M rule: each call is muted by its speed's row,
    /// which also mutes that speed's set-announce ("V1: 142 knots").</summary>
    public static readonly TakeoffCalloutKeys Keys = new(IasKey, V1Key, VrKey, V2Key);
}
