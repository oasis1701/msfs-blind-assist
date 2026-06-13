namespace MSFSBlindAssist.Services;

/// <summary>
/// Captures the most recent landing's touchdown vertical speed (landing rate) and the
/// PEAK vertical g-force of the touchdown, for the two output-mode readout hotkeys
/// (ReadLastLandingRate / ReadLastLandingPeakG). Aircraft-agnostic — fed by the always-on
/// continuous base SimVars in <see cref="Aircraft.BaseAircraftDefinition"/>:
///   • G FORCE (per-var SIM_FRAME subscription) → <see cref="ProcessG"/> every update
///   • PLANE TOUCHDOWN NORMAL VELOCITY          → read live from the SimConnect cache by the hotkey.
///
/// The SIM_ON_GROUND edge arrives via the 1 Hz continuous batch, so OnTouchdown can fire
/// up to ~1 s AFTER the impact spike has already happened. ProcessG therefore keeps a short
/// rolling history; OnTouchdown seeds the peak from the lookback window AND opens a forward
/// capture window for the oleo rebound.
/// </summary>
public class LandingRateAnnouncer
{
    // Forward window after the touchdown edge (impact + rebound, not later taxi bumps).
    private const long CaptureWindowMs = 4000;
    // Lookback window: covers the up-to-1 s SIM_ON_GROUND batch latency plus queue lag.
    private const long LookbackWindowMs = 3000;

    private double _peakG;
    private long _captureUntilTick;
    private bool _hasLanding;

    // Rolling (tick, g) samples for the lookback max. ~90–180 entries at SIM_FRAME rate.
    private readonly Queue<(long Tick, double G)> _recent = new();

    /// <summary>The peak g-force of the last landing's touchdown, or null if none recorded this session.</summary>
    public double? LastPeakG => _hasLanding ? _peakG : (double?)null;

    /// <summary>True once a touchdown edge has actually been observed this session —
    /// gates the landing-rate readout so a runway-spawn latched SimVar value isn't reported.</summary>
    public bool HasLanding => _hasLanding;

    /// <summary>
    /// Called on the airborne→on-ground edge (touchdown). Seeds the peak with the g at contact
    /// and the recent high-rate samples (the spike may precede this late-reported edge), then
    /// opens the forward capture window.
    /// </summary>
    public void OnTouchdown(double gAtContact)
    {
        _peakG = gAtContact;
        long now = System.Environment.TickCount64;
        foreach (var s in _recent)
        {
            if (now - s.Tick <= LookbackWindowMs && s.G > _peakG) _peakG = s.G;
        }
        _captureUntilTick = now + CaptureWindowMs;
        _hasLanding = true;
    }

    /// <summary>Feed every G FORCE update. Maxes inside the forward window and maintains the lookback history.</summary>
    public void ProcessG(double g)
    {
        long now = System.Environment.TickCount64;
        if (now <= _captureUntilTick && g > _peakG) _peakG = g;
        _recent.Enqueue((now, g));
        while (_recent.Count > 0 && now - _recent.Peek().Tick > LookbackWindowMs) _recent.Dequeue();
    }
}
