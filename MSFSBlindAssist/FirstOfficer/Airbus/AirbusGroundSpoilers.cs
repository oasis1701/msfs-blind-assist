namespace MSFSBlindAssist.FirstOfficer.Airbus;

/// <summary>
/// The A320-family Landing flow's ground-spoiler arm (Fenix A320, FBW A32NX, Headwind A330; FOA-11):
/// when the speed brake reads extended, the First Officer leaves the ground spoilers alone and says so.
/// Arming moves the Captain's speed brake: on the FlyByWire jets SPOILERS_ARM_ON snaps the handle to
/// retracted (FlyByWire's SpoilersHandler sets the handle to 0; measured on the A32NX 2026-10-16, the
/// handle went from 0.5 to 0 and the spoilers armed), and the Fenix's arm write puts its lever at ARM.
/// The real aircraft does not arm with the speed brake out either. An unread speed brake is not
/// extended: on the FlyByWire jets the handle is a continuous variable, read once the aircraft is
/// connected.
/// </summary>
public static class AirbusGroundSpoilers
{
    /// <summary>What the First Officer says when it leaves the arm alone.</summary>
    public const string LeaveAloneText = "Speed brake extended, ground spoilers not armed. Left as it is.";

    /// <summary>FlyByWire handle <c>A32NX_SPOILERS_HANDLE_POSITION</c> (0 retracted, also while armed;
    /// 1 full) above which the speed brake is extended.</summary>
    internal const double FbwExtendedAbove = 0.05;

    /// <summary>Fenix lever <c>A_FC_SPEEDBRAKE</c> (0 ARM, 1 stowed, 2 half, 3 full) above which the
    /// speed brake is extended. Unmeasured between the detents: past stowed with a margin.</summary>
    internal const double FenixExtendedAbove = 1.1;

    /// <summary>True when the FlyByWire speed brake handle reads extended; false when retracted or unread.</summary>
    public static bool FbwSpeedBrakeExtended(double handle) => handle > FbwExtendedAbove;

    /// <summary>True when the Fenix speed brake lever reads extended; false at ARM, stowed or unread.</summary>
    public static bool FenixSpeedBrakeExtended(double lever) => lever > FenixExtendedAbove;
}
