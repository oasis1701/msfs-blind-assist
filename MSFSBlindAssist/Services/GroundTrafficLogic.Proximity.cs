namespace MSFSBlindAssist.Services;

internal static partial class GroundTrafficLogic
{
    /// <summary>Opening faster than this is "moving away": 20 ft per 3 s, the rule the 3 s poll had.</summary>
    public const double MovingAwayFtPerSec = 20.0 / 3.0;
    /// <summary>A same-or-lower zone re-entry within this of the last callout for that aircraft is not re-announced.</summary>
    public const int EscalationRepeatWindowMs = 15000;
    /// <summary>Caution/Warning — and now converging — are for traffic within ±this of the nose.</summary>
    public const double ForwardArcDeg = 120.0;
    /// <summary>A pilot moving at least this fast can act on traffic from any direction.</summary>
    public const double ConvergingOwnMovingKts = 3.0;
    /// <summary>
    /// Inside the repeat window, a Caution or Warning re-entry at (or below) the zone last spoken is
    /// spoken again once the aircraft is this much closer than when that zone was spoken — the pilot
    /// resumed closing on it. Boundary flicker at an unchanged distance is not.
    /// </summary>
    public const double EscalationReclosureFt = 50.0;

    /// <summary>
    /// Is the aircraft opening from us? Judged as a RATE (PR #247 review R8): the old per-evaluation
    /// 20 ft threshold was sized for a 3 s cycle and became three times stricter at 1 s.
    /// </summary>
    public static bool IsMovingAway(double previousDistFt, DateTime previousUtc, double distFt, DateTime nowUtc)
    {
        if (double.IsNaN(previousDistFt) || previousDistFt >= double.MaxValue) return false;
        double dt = (nowUtc - previousUtc).TotalSeconds;
        if (dt <= 0.0) return false;
        return (distFt - previousDistFt) / dt >= MovingAwayFtPerSec;
    }

    /// <summary>
    /// Announce this zone change? Only an escalation (<paramref name="newZone"/> above
    /// <paramref name="currentZone"/>). Awareness needs <see cref="EscalationRepeatWindowMs"/> since the
    /// last callout for this aircraft. Caution/Warning need a zone ABOVE the last one spoken for it, or
    /// the window — so complying with "Slow down" (which moves the speed-scaled boundary and silently
    /// drops the zone) does not earn another "Slow down" seconds later, while Caution → Warning is
    /// never suppressed (R8) — or, inside the window, the aircraft
    /// <see cref="EscalationReclosureFt"/> closer (<paramref name="distFt"/>) than when the last zone
    /// callout was spoken (<paramref name="lastSpokenDistFt"/>): the pilot resumed closing on it, and a
    /// "Stop" must not be swallowed (PR #247 B1 review I3). Without both distances (NaN) that last rule
    /// does not apply. It never applies to Awareness.
    /// </summary>
    public static bool ShouldAnnounceEscalation(GroundZone newZone, GroundZone currentZone,
        GroundZone lastSpokenZone, DateTime lastSpokenUtc, DateTime nowUtc,
        double distFt = double.NaN, double lastSpokenDistFt = double.NaN)
    {
        if (newZone == GroundZone.None || newZone <= currentZone) return false;
        bool windowElapsed = (nowUtc - lastSpokenUtc).TotalMilliseconds >= EscalationRepeatWindowMs;
        if (newZone == GroundZone.Awareness) return windowElapsed;
        if (newZone > lastSpokenZone || windowElapsed) return true;
        return double.IsFinite(distFt) && double.IsFinite(lastSpokenDistFt)
               && distFt <= lastSpokenDistFt - EscalationReclosureFt;
    }

    /// <summary>
    /// A converging callout needs the traffic in the forward arc, or the pilot moving (R9): a stopped
    /// pilot cannot act on traffic closing from behind, which in a queue is every aircraft joining it.
    /// </summary>
    public static bool ConvergingAllowed(double relBearingDeg, double ownGsKts)
        => Math.Abs(AngleDiff(relBearingDeg, 0.0)) <= ForwardArcDeg || ownGsKts >= ConvergingOwnMovingKts;
}
