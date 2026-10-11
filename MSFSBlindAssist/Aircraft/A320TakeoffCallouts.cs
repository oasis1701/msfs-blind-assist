namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The FBW A320's take-off roll callouts ("V1", "Rotate", "V2"): which variables feed
/// <see cref="TakeoffVSpeedCallouts"/> and which Ctrl+M row mutes each call — the A380's
/// <see cref="A380TakeoffCallouts"/> for the A32NX (added 2026-10-11). The speeds are the FMS's
/// <c>L:AIRLINER_V1/VR/V2_SPEED</c> (knots; -1 for a cleared V1/VR and 0 for V2 since FBW #10855,
/// 0 for all three before it), which MSFSBA already reads and announces as the pilot enters them
/// (<c>PFD_V1</c>/<c>PFD_VR</c>/<c>PFD_V2</c>); airspeed comes from a per-frame feed. Unlike the
/// A380, the A32NX plays no V-speed callout of its own (its FWS sounds are the radio heights,
/// "retard", minimums and the speed and pitch warnings — its sound.xml), so MSFSBA's is the only one. The Headwind A330 inherits all of it through
/// <see cref="HeadwindA330Definition"/>.
/// </summary>
public static class A320TakeoffCallouts
{
    /// <summary>The per-SIM_FRAME <c>AIRSPEED INDICATED</c> feed; consumed, never spoken, hidden from Ctrl+M.</summary>
    public const string IasKey = "A320_TAKEOFF_CALLOUT_IAS";

    public const string V1Key = "PFD_V1";
    public const string VrKey = "PFD_VR";
    public const string V2Key = "PFD_V2";

    /// <summary>The feed, the speeds and their Ctrl+M rule: each call is muted by its speed's row,
    /// which also mutes that speed's set-announce ("V1: 142 knots").</summary>
    public static readonly TakeoffCalloutKeys Keys = new(IasKey, V1Key, VrKey, V2Key);
}
