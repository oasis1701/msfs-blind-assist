namespace MSFSBlindAssist.Services;

/// <summary>
/// Pure decision logic for the generic ice-accretion announcement (spec
/// 2026-07-11 §2.2). Binary with hysteresis, using the FBW A380 announcer's
/// sim-verified thresholds and mirrored-ternary semantics
/// (FlyByWireA380Definition: ICING_DETECT_RATIO / ICING_CLEAR_RATIO): rising
/// edge at ratio ≥ 0.05, falling edge at ratio ≤ 0.02, dead band silent.
/// First sample baseline-silenced — an app starting with ice already on the
/// airframe adopts the state without announcing.
/// </summary>
internal sealed class IceAccretionTracker
{
    internal const double DETECT_RATIO = 0.05;   // rising edge → "ice accumulating"
    internal const double CLEAR_RATIO = 0.02;    // falling edge → "cleared"

    private bool _icing;
    private bool _baselineDone;

    public string? Observe(double iceRatio)
    {
        bool now = _icing ? iceRatio > CLEAR_RATIO : iceRatio >= DETECT_RATIO;
        if (!_baselineDone)
        {
            _icing = now;
            _baselineDone = true;
            return null;
        }
        if (now == _icing) return null;
        _icing = now;
        return now ? "Icing conditions, ice accumulating" : "Icing conditions cleared";
    }

    public void Reset()
    {
        _icing = false;
        _baselineDone = false;
    }
}
