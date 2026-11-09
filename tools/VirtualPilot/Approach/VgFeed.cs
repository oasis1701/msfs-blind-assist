using MSFSBlindAssist.Services;

namespace VirtualPilot.Approach;

/// <summary>
/// Feeds one simulated frame to visual guidance the way the app does. This mirrors
/// MainForm.Announcers.cs (the VISUAL_GUIDANCE_* handlers, through the same SimConnect conversion
/// helpers) and SimConnectManager.Dispatch.cs, which delivers the AGL last: its handler is the one
/// that calls ProcessUpdate.
/// </summary>
static class VgFeed
{
    public static void Feed(VisualGuidanceManager vg, in VgSimFrame f)
    {
        vg.UpdateLatitude(f.Lat);
        vg.UpdateLongitude(f.Lon);
        vg.UpdateAltitudeMSL(f.AltMslFt);
        vg.UpdateHeading(f.HeadingMagDeg);
        vg.UpdateGroundSpeed(f.GsKt);
        vg.UpdateVerticalSpeed(f.VsFpm);
        vg.UpdateGroundTrack(f.GroundTrackMagDeg);
        vg.UpdatePitch(VisualGuidanceManager.PitchDegreesFromSimConnect(f.PitchRad));
        vg.UpdateBank(VisualGuidanceManager.BankDegreesFromSimConnect(f.BankRad));
        vg.UpdateAoA(VisualGuidanceManager.AoaDegreesFromSimConnect(f.AlphaRad));
        vg.UpdateAGL(f.AglFt);
        vg.ProcessUpdate();
    }
}
