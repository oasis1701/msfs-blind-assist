namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// One airframe's variables for the take-off roll callouts (<see cref="TakeoffVSpeedCallouts"/>):
/// the per-frame airspeed feed and the three FMS V-speeds, and the Ctrl+M rule every airframe shares
/// — each call is muted by its speed's row, which also mutes that speed's set-announce ("V1: 142
/// knots"). One mapping for the MD-11 (<see cref="MD11.Md11TakeoffCallouts"/>), the FBW A380
/// (<see cref="A380TakeoffCallouts"/>) and the iFly 737 MAX8, instead of a hand-kept copy each.
/// </summary>
public sealed record TakeoffCalloutKeys(string IasKey, string V1Key, string VrKey, string V2Key)
{
    /// <summary>
    /// The Ctrl+M key whose mute silences <paramref name="callout"/> ("V1", "Rotate" or "V2"). A
    /// callout this table does not know maps to NO row and is therefore never muted — fail open:
    /// a new call the machine grows one day is spoken until someone gives it a row, never
    /// silently swallowed by the V2 checkbox.
    /// </summary>
    public string MuteKeyFor(string callout) => callout switch
    {
        "V1" => V1Key,
        "Rotate" => VrKey,
        "V2" => V2Key,
        _ => "",
    };

    /// <summary>
    /// Whether the pilot muted <paramref name="callout"/> in Ctrl+M — its row
    /// (<see cref="MuteKeyFor"/>) is in <paramref name="muted"/> — the test a definition hands
    /// <see cref="TakeoffVSpeedCallouts.Compose"/>. A callout with no row is never muted (fail
    /// open), whatever the set holds.
    /// </summary>
    public bool IsMuted(string callout, IReadOnlySet<string> muted)
    {
        var row = MuteKeyFor(callout);
        return row.Length != 0 && muted.Contains(row);
    }

    /// <summary>True for the three FMS V-speed variables that arm the machine.</summary>
    public bool IsVSpeedKey(string varName) => varName == V1Key || varName == VrKey || varName == V2Key;

    /// <summary>Hands a delivered V-speed to the machine; anything else is ignored.</summary>
    public void Feed(TakeoffVSpeedCallouts machine, string varName, double value)
    {
        if (varName == V1Key) machine.SetV1(value);
        else if (varName == VrKey) machine.SetVR(value);
        else if (varName == V2Key) machine.SetV2(value);
    }
}
