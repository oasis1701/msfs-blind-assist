namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The Fenix A320's take-off roll callouts ("V1", "Rotate", "V2"): which variables feed
/// <see cref="TakeoffVSpeedCallouts"/> and which Ctrl+M row mutes each call (TKO-5). The speeds are
/// the MCDU performance page's <c>N_MISC_PERF_TO_V1/VR/V2</c> (knots; 0 when cleared), which MSFSBA
/// already reads and announces as the pilot enters them; airspeed comes from a per-frame feed. The
/// Fenix's own FWC also calls "V1" aloud (sound.xml <c>FWC_V_ONE_L/_R</c>, <c>A320_Audio:v1</c>); it
/// plays no Rotate or V2 call. MSFSBA's call arrives alongside it, as on the A380 (owner decision,
/// 2026-10-22).
/// </summary>
public static class FenixTakeoffCallouts
{
    /// <summary>The per-SIM_FRAME <c>AIRSPEED INDICATED</c> feed; consumed, never spoken, hidden from Ctrl+M.</summary>
    public const string IasKey = "FENIX_TAKEOFF_CALLOUT_IAS";

    public const string V1Key = "N_MISC_PERF_TO_V1";
    public const string VrKey = "N_MISC_PERF_TO_VR";
    public const string V2Key = "N_MISC_PERF_TO_V2";

    /// <summary>The feed, the speeds and their Ctrl+M rule: each call is muted by its speed's row,
    /// which also mutes that speed's set-announce ("MCDU V1 Speed: 140 knots").</summary>
    public static readonly TakeoffCalloutKeys Keys = new(IasKey, V1Key, VrKey, V2Key);
}
