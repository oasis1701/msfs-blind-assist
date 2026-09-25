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

    /// <summary>
    /// The Ctrl+M key whose mute silences <paramref name="callout"/> ("V1", "Rotate" or "V2") — the
    /// row that also mutes that speed's set-announce ("V1: 142 knots"). A callout this table does
    /// not know maps to NO row and is never muted: fail open, as on the MD-11 and the iFly.
    /// </summary>
    public static string MuteKeyFor(string callout) => callout switch
    {
        "V1" => V1Key,
        "Rotate" => VrKey,
        "V2" => V2Key,
        _ => "",
    };

    /// <summary>Whether the pilot muted <paramref name="callout"/> in Ctrl+M (its <see cref="MuteKeyFor"/>
    /// row is in <paramref name="muted"/>); a callout with no row never is.</summary>
    public static bool IsMuted(string callout, IReadOnlySet<string> muted)
    {
        var row = MuteKeyFor(callout);
        return row.Length != 0 && muted.Contains(row);
    }

    /// <summary>True for the three FMS V-speed variables that arm the machine.</summary>
    public static bool IsVSpeedKey(string varName) => varName is V1Key or VrKey or V2Key;

    /// <summary>Hands a delivered V-speed to the machine; anything else is ignored.</summary>
    public static void Feed(TakeoffVSpeedCallouts machine, string varName, double value)
    {
        switch (varName)
        {
            case V1Key: machine.SetV1(value); break;
            case VrKey: machine.SetVR(value); break;
            case V2Key: machine.SetV2(value); break;
        }
    }
}
