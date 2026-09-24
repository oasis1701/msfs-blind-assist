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
    /// Inside the repeat window, a WARNING re-entry is spoken again once the aircraft is this much closer
    /// than when "Stop" was last spoken — the pilot resumed closing on it. Caution keeps R8: complying
    /// with "Slow down" never earns another one inside the window.
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

    // ── Moving away judged from MOTION (PR #247 author's fix, 9190e869) ─────────────────────────────
    // IsMovingAway needs two evaluations, so traffic pulling away was still "Stop, … very close" on the
    // first evaluation that saw it — a pilot creeping up behind a departing aircraft, in the author's
    // simulated traffic. These are OR'd with it.

    /// <summary>Opening this fast (m/s, about 2 kt) is moving away: <see cref="IsOpeningByMotion"/>, <see cref="IsLeadGrowing"/>.</summary>
    public const double MovingAwayMinOpeningMps = 1.0;

    /// <summary>
    /// How fast the gap is opening NOW, in m/s — negative when closing: the traffic's velocity relative to
    /// ours (<paramref name="relVx"/>, <paramref name="relVy"/>, m/s east and north) along the line of sight
    /// to it (<paramref name="rx"/>, <paramref name="ry"/>, its offset in metres, <see cref="ToLocal"/>).
    /// 0 when the two are within a metre.
    /// </summary>
    public static double OpeningSpeedMps(double rx, double ry, double relVx, double relVy)
    {
        double r = Math.Sqrt(rx * rx + ry * ry);
        return r < 1.0 ? 0.0 : (rx * relVx + ry * relVy) / r;
    }

    /// <summary>
    /// Is the traffic's lead ALONG THE ROUTE growing at <see cref="MovingAwayMinOpeningMps"/> or more? For
    /// traffic on the route ahead the straight-line gap is the wrong measure through a bend: an aircraft
    /// rounding a corner ahead moves sideways to the line of sight while pulling away along the route.
    /// <paramref name="previousAheadM"/> is its lead at the previous evaluation (NaN when it was not on the
    /// route ahead then); two leads less than 0.2 s or 10 s or more apart are no basis for a rate.
    /// </summary>
    public static bool IsLeadGrowing(double previousAheadM, DateTime previousUtc, double aheadM, DateTime nowUtc)
    {
        if (double.IsNaN(previousAheadM) || double.IsNaN(aheadM)) return false;
        double dt = (nowUtc - previousUtc).TotalSeconds;
        if (dt <= 0.2 || dt >= 10.0) return false;
        return (aheadM - previousAheadM) / dt >= MovingAwayMinOpeningMps;
    }

    /// <summary>
    /// Moving away judged from motion: MOVING traffic (at least <see cref="MovingTrafficKts"/>) whose
    /// relative velocity opens the gap at <see cref="MovingAwayMinOpeningMps"/> or more
    /// (<paramref name="openingMps"/>, <see cref="OpeningSpeedMps"/>), or whose lead along the route is
    /// growing that fast (<paramref name="leadGrowing"/>, <see cref="IsLeadGrowing"/>). The monitor ORs it
    /// with <see cref="IsMovingAway"/>. A parked aircraft never moves away by this test.
    /// </summary>
    public static bool IsOpeningByMotion(double trafficGsKts, double openingMps, bool leadGrowing)
        => trafficGsKts >= MovingTrafficKts && (openingMps >= MovingAwayMinOpeningMps || leadGrowing);

    // ── A "Stop" withheld while the traffic opens stays withheld while it keeps pulling away ──────────
    // (PR #247 integration review Q2.) A withheld Warning is never recorded, so once the traffic stops
    // opening the next evaluation says "Stop" — which closed the swallowed "Stop" of a leader that pulls
    // away and stops again ahead. But "stops opening" also comes while it is still pulling away slowly: a
    // pilot following a departing leader out of a queue, catching up to its speed, brought the opening
    // below MovingAwayMinOpeningMps and heard "Stop" with the gap still growing. So once a "Stop" has been
    // withheld BECAUSE the traffic was opening, it is HELD — still unrecorded — until the traffic stops or
    // closes on the pilot. Never "recorded until the traffic stops": a leader that slows to 4 kt without
    // stopping would then never earn "Stop" as the pilot closes on it.

    /// <summary>
    /// A held "Stop" (<see cref="IsMovingAwayOrHeld"/>) is released once the pilot closes on the traffic at
    /// this speed (m/s) or faster.
    /// </summary>
    public const double HeldStopReleaseClosingMps = 0.5;

    /// <summary>
    /// Is the traffic moving away, for its zone callouts — withheld, as traffic pulling away is? Yes while it
    /// is opening now (<paramref name="openingNow"/>: <see cref="IsMovingAway"/> or
    /// <see cref="IsOpeningByMotion"/>). And while a "Stop" withheld because it was opening is held
    /// (<paramref name="stopHeld"/>, <see cref="StopHeldAfterMovingAway"/>), still yes as long as the traffic
    /// is MOVING (at least <see cref="MovingTrafficKts"/>) and not closing at
    /// <see cref="HeldStopReleaseClosingMps"/> or more (<paramref name="openingMps"/>,
    /// <see cref="OpeningSpeedMps"/>, negative when closing). It speaks once the traffic stops or closes.
    /// </summary>
    public static bool IsMovingAwayOrHeld(bool openingNow, bool stopHeld, double trafficGsKts, double openingMps)
        => openingNow
           || (stopHeld && trafficGsKts >= MovingTrafficKts && openingMps > -HeldStopReleaseClosingMps);

    /// <summary>
    /// After a zone callout was withheld as moving away (<see cref="IsMovingAwayOrHeld"/>): is a "Stop" held
    /// now? Yes when what was withheld is a Warning ESCALATION (<paramref name="newZone"/> Warning, above
    /// <paramref name="currentZone"/> — left unrecorded by <see cref="ZoneToRecordWhenWithheld"/>); no once
    /// the zone is below Warning, where no "Stop" is due; otherwise (a Warning already recorded, so no
    /// escalation) as it was (<paramref name="stopHeld"/>).
    /// </summary>
    public static bool StopHeldAfterMovingAway(GroundZone newZone, GroundZone currentZone, bool stopHeld)
        => newZone == GroundZone.Warning && (currentZone < GroundZone.Warning || stopHeld);

    /// <summary>
    /// Announce this zone change? Only an escalation (<paramref name="newZone"/> above
    /// <paramref name="currentZone"/>). Awareness needs <see cref="EscalationRepeatWindowMs"/> since the
    /// last callout for this aircraft. Caution/Warning need a zone ABOVE the last one spoken for it, or
    /// the window — so complying with "Slow down" (which moves the speed-scaled boundary and silently
    /// drops the zone) does not earn another "Slow down" seconds later, while Caution → Warning is
    /// never suppressed (R8). A WARNING re-entry inside the window is also spoken once the aircraft is
    /// <see cref="EscalationReclosureFt"/> closer (<paramref name="distFt"/>) than when "Stop" was last
    /// spoken (<paramref name="lastSpokenDistFt"/>): the pilot resumed closing on it, and a "Stop" must
    /// not be swallowed (PR #247 B1 review I3). Without both distances (NaN) that rule does not apply;
    /// it never applies to Caution (PR #247 B2 review: it reversed R8 for a pilot who COMPLIED with
    /// "Slow down") or to Awareness.
    /// </summary>
    public static bool ShouldAnnounceEscalation(GroundZone newZone, GroundZone currentZone,
        GroundZone lastSpokenZone, DateTime lastSpokenUtc, DateTime nowUtc,
        double distFt = double.NaN, double lastSpokenDistFt = double.NaN)
    {
        if (newZone == GroundZone.None || newZone <= currentZone) return false;
        bool windowElapsed = (nowUtc - lastSpokenUtc).TotalMilliseconds >= EscalationRepeatWindowMs;
        if (newZone == GroundZone.Awareness) return windowElapsed;
        if (newZone > lastSpokenZone || windowElapsed) return true;
        return newZone == GroundZone.Warning
               && double.IsFinite(distFt) && double.IsFinite(lastSpokenDistFt)
               && distFt <= lastSpokenDistFt - EscalationReclosureFt;
    }

    /// <summary>
    /// The zone to record when an escalation was NOT announced. A withheld WARNING escalation is not
    /// recorded — it is judged again next evaluation, so "Stop" is not swallowed for good (I3). Everything
    /// else withheld — a de-escalation, a Caution re-entry, an Awareness ping — is recorded silently (R8).
    /// </summary>
    public static GroundZone ZoneToRecordWhenWithheld(GroundZone newZone, GroundZone currentZone)
        => newZone > currentZone && newZone == GroundZone.Warning ? currentZone : newZone;

    /// <summary>
    /// A converging callout needs the traffic in the forward arc, or the pilot moving (R9): a stopped
    /// pilot cannot act on traffic closing from behind, which in a queue is every aircraft joining it.
    /// </summary>
    public static bool ConvergingAllowed(double relBearingDeg, double ownGsKts)
        => Math.Abs(AngleDiff(relBearingDeg, 0.0)) <= ForwardArcDeg || ownGsKts >= ConvergingOwnMovingKts;

    /// <summary>Traffic at or above this ground speed is MOVING: its straight-line track predicts something.</summary>
    public const double MovingTrafficKts = 3.0;
    /// <summary>A predicted closest approach under this makes moving traffic a route threat (<see cref="IsRouteThreat"/>).</summary>
    public const double ThreatDcpaM = 60.0;
    /// <summary>That closest approach must come within this many seconds (<see cref="IsRouteThreat"/>).</summary>
    public const double ThreatMaxTcpaSec = 30.0;

    /// <summary>
    /// With a taxi route to judge by, may this aircraft earn "Slow down"/"Stop"? Only as a real threat:
    /// near the route ahead (<paramref name="nearRoute"/>), genuinely very close (<paramref name="veryClose"/>,
    /// inside the fixed Warning distance), or MOVING (at least <see cref="MovingTrafficKts"/>) with a
    /// predicted closest approach under <see cref="ThreatDcpaM"/> within <see cref="ThreatMaxTcpaSec"/>.
    /// Anything else drops to an Awareness ping, which can still escalate later.
    /// <para>The closest approach is a threat test for moving traffic ONLY (PR #247 author's fix, 9190e869).
    /// For a parked aircraft it assumes the pilot keeps going straight, and where the route bends toward
    /// one before turning away it predicted a near pass the route never makes: "Slow down, … ahead, 160
    /// metres" (and "Stop" at speed) for aircraft parked 100 m beside the route — 421 such calls in his
    /// simulated-traffic runs over 100 airports. A parked aircraft is a threat through the route or by
    /// being very close, as before.</para>
    /// </summary>
    public static bool IsRouteThreat(bool nearRoute, double dcpaM, double tcpaSec, double trafficGsKts, bool veryClose)
        => nearRoute
           || (trafficGsKts >= MovingTrafficKts && dcpaM < ThreatDcpaM && tcpaSec <= ThreatMaxTcpaSec)
           || veryClose;

    // ── Only the first aircraft on the route ahead is called (PR #247 author's fix, 9190e869) ─────────
    // The author's "shadowed" traffic: an aircraft queued beyond the FIRST one on the route ahead cannot
    // be reached without passing it, so it is not called on its own — a three-aircraft queue was announced
    // as three "on your route" calls, then three "Slow down"s, each cutting off the last. The queue
    // position covers them.

    /// <summary>
    /// How much further along the route than the first aircraft on it another must be to count as queued
    /// behind it: two side by side on one taxiway are both first.
    /// </summary>
    public const double QueuedBehindFirstGapM = 10.0;

    /// <summary>
    /// The along-route distance of the FIRST aircraft on the route ahead — among the traffic ON it
    /// (<c>OnRouteAhead</c>), the smallest distance ahead, the one the pilot would reach first — or null
    /// when nothing is on the route ahead.
    /// </summary>
    public static double? FirstOnRouteAheadM(IEnumerable<(bool OnRouteAhead, double AheadM)> traffic)
    {
        double? first = null;
        foreach (var (onRouteAhead, aheadM) in traffic)
            if (onRouteAhead && (first is not double f || aheadM < f)) first = aheadM;
        return first;
    }

    /// <summary>
    /// Queued behind the first aircraft on the route ahead: on the route ahead itself, and more than
    /// <see cref="QueuedBehindFirstGapM"/> further along than that first one (<paramref name="firstAheadM"/>,
    /// <see cref="FirstOnRouteAheadM"/>). It gets no on-route callout, and its zone callouts are withheld
    /// unless it is very close (<see cref="WithholdsZoneBehindFirst"/>).
    /// </summary>
    public static bool IsQueuedBehindFirst(bool onRouteAhead, double aheadM, double? firstAheadM)
        => onRouteAhead && firstAheadM is double first && aheadM > first + QueuedBehindFirstGapM;

    /// <summary>
    /// A zone callout for an aircraft queued behind the first (<see cref="IsQueuedBehindFirst"/>) is
    /// withheld unless it is very close: "Stop" (<see cref="GroundZone.Warning"/>) is never withheld.
    /// </summary>
    public static bool WithholdsZoneBehindFirst(bool queuedBehindFirst, GroundZone zone)
        => queuedBehindFirst && zone < GroundZone.Warning;
}
