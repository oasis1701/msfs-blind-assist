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

    /// <summary>The feed, the speeds and their Ctrl+M rule (each call muted by its speed's row).</summary>
    public static readonly TakeoffCalloutKeys Keys = new(IasKey, V1Key, VrKey, V2Key);
}
