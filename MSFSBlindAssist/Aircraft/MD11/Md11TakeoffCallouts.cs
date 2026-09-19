using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// The MD-11's take-off roll callouts ("V1", "Rotate", "V2"): which variables feed
/// <see cref="TakeoffVSpeedCallouts"/> and which Ctrl+M row mutes each call. TFDi plays no aural
/// V-speed callouts of its own, so a blind pilot had nothing on the roll where the PMDGs speak;
/// the FMS's <c>MD11_V1</c>/<c>MD11_VR</c>/<c>MD11_V2</c> exports and a per-frame airspeed feed
/// give the app what it needs to say them at the right knot.
/// </summary>
public static class Md11TakeoffCallouts
{
    /// <summary>The per-SIM_FRAME <c>AIRSPEED INDICATED</c> feed; consumed, never spoken, hidden from Ctrl+M.</summary>
    public const string IasKey = "MD11_IAS";

    public const string V1Key = "MD11_V1";
    public const string VrKey = "MD11_VR";
    public const string V2Key = "MD11_V2";

    /// <summary>
    /// The Ctrl+M key whose mute silences <paramref name="callout"/> ("V1", "Rotate" or "V2"). A
    /// callout this table does not know maps to NO row and is therefore never muted — fail open:
    /// a new call the machine grows one day is spoken until someone gives it a row, never
    /// silently swallowed by the V2 checkbox.
    /// </summary>
    public static string MuteKeyFor(string callout) => callout switch
    {
        "V1" => V1Key,
        "Rotate" => VrKey,
        "V2" => V2Key,
        _ => "",
    };

    /// <summary>
    /// Whether the pilot muted <paramref name="callout"/> in Ctrl+M — its row
    /// (<see cref="MuteKeyFor"/>) is in <paramref name="muted"/> — the test the definition hands
    /// <see cref="TakeoffVSpeedCallouts.Compose"/>. A callout with no row is never muted (fail
    /// open), whatever the set holds.
    /// </summary>
    public static bool IsMuted(string callout, IReadOnlySet<string> muted)
    {
        var row = MuteKeyFor(callout);
        return row.Length != 0 && muted.Contains(row);
    }

    /// <summary>True for the three FMS V-speed exports that arm the machine.</summary>
    public static bool IsVSpeedKey(string varName) => varName is V1Key or VrKey or V2Key;

    /// <summary>Hands a delivered V-speed export to the machine; anything else is ignored.</summary>
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
