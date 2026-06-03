namespace MSFSBlindAssist.Services;

/// <summary>
/// Captures the most recent landing's touchdown vertical speed (landing rate) and the
/// PEAK vertical g-force of the touchdown, for the two output-mode readout hotkeys
/// (ReadLastLandingRate / ReadLastLandingPeakG). Aircraft-agnostic — fed by the always-on
/// continuous base SimVars in <see cref="Aircraft.BaseAircraftDefinition"/>:
///   • G FORCE                          → <see cref="ProcessG"/> every update (peak tracker)
///   • PLANE TOUCHDOWN NORMAL VELOCITY  → read live from the SimConnect cache by the hotkey
///     (the sim latches it at touchdown and it persists until the next landing).
///
/// The landing RATE is read straight from the persistent touchdown-velocity SimVar at hotkey
/// time, so it needs no capture here. The peak G, by contrast, is a transient spike that
/// occurs in the moment(s) AFTER the wheels contact (the impact), so we open a short capture
/// window on the touchdown edge and keep the running maximum of G FORCE across it.
/// </summary>
public class LandingRateAnnouncer
{
    // How long after the touchdown edge to keep tracking the peak g-force. The impact spike
    // lands within ~1 s; 4 s comfortably covers a firm landing plus any oleo rebound, without
    // catching a later taxi/turn bump.
    private const long CaptureWindowMs = 4000;

    private double _peakG;
    private long _captureUntilTick;
    private bool _hasLanding;

    /// <summary>The peak g-force of the last landing's touchdown, or null if none recorded this session.</summary>
    public double? LastPeakG => _hasLanding ? _peakG : (double?)null;

    /// <summary>
    /// Called on the airborne→on-ground edge (touchdown). Seeds the peak with the g at contact
    /// and opens the capture window so the impact spike that follows is caught.
    /// </summary>
    public void OnTouchdown(double gAtContact)
    {
        _peakG = gAtContact;
        _captureUntilTick = System.Environment.TickCount64 + CaptureWindowMs;
        _hasLanding = true;
    }

    /// <summary>Feed every G FORCE update. Updates the running peak only inside the capture window.</summary>
    public void ProcessG(double g)
    {
        if (System.Environment.TickCount64 <= _captureUntilTick && g > _peakG)
        {
            _peakG = g;
        }
    }
}
