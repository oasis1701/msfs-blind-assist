namespace MSFSBlindAssist.Services;

internal static partial class GroundTrafficLogic
{
    // ── Has the aircraft we pulled up behind left? (QueueDeparted) ───────────────────────
    /// <summary>At or below this ground speed an aircraft ahead counts as stopped in the queue.</summary>
    public const double QueueStoppedGs = 1.5;
    /// <summary>Speed-edge trigger: rolling this fast in the sample after a stopped one.</summary>
    public const double QueueMovingGs = 2.0;
    /// <summary>Gap trigger: the target must be moving at least this fast to have opened the gap itself.</summary>
    public const double QueueCreepGs = 1.0;
    /// <summary>Gap trigger: growth over the stopped-gap baseline that counts as departed.</summary>
    public const double QueueGapOpenedFt = 60.0;

    // ── The departure queue ─────────────────────────────────────────────────────────────
    /// <summary>
    /// Largest gap between two aircraft that still counts as ONE queue, and the largest gap between
    /// the pilot and the queue's tail that still counts as joining it. Aircraft hold roughly
    /// 50-100 m apart, so 250 m is well clear of normal spacing while being far smaller than the
    /// distance between two SEPARATE queues.
    /// </summary>
    public const double QueueLinkMaxGapM = 250.0;
    /// <summary>Within this of the route line an aircraft is on the route.</summary>
    public const double QueueLateralMaxM = 30.0;
    public const double QueueMinAheadM = 10.0;
    /// <summary>How far along the route the queue looks (not where one queue ends — that is the gap).</summary>
    public const double QueueScanM = 1500.0;
    /// <summary>Faster than this an aircraft is taxiing, not queued.</summary>
    public const double QueueTrafficMaxGs = 6.0;
    /// <summary>A queued aircraft points along the route, within this of the route's direction (R12).</summary>
    public const double QueueAlignMaxDeg = 35.0;
    /// <summary>The head of the pilot's line is AT the runway hold when within this of the route end.</summary>
    public const double QueueAtRunwayHoldM = 150.0;

    // ── Intake, sweep radius and poll cadence ───────────────────────────────────────────
    public const double FeetPerMetre = 3.28084;
    /// <summary>Proximity tracking range; far aircraft never alert.</summary>
    public const double TrackRangeFt = 2000.0;
    /// <summary>Ground traffic faster than this is taking off or landing — not a taxi concern.</summary>
    public const double MaxTaxiGsKts = 60.0;
    /// <summary>While a runway is watched, ground traffic on a long runway stays in view to here.</summary>
    public const double RunwayWatchGroundRangeM = 5000.0;
    /// <summary>While a runway is watched, airborne traffic on a 6 nm final stays in view.</summary>
    public const double RunwayWatchAirRangeM = FinalMaxNm * MetresPerNm + 5000.0;
    /// <summary>Moving traffic this close keeps the sweep at 1 s.</summary>
    public const double FastPollRangeFt = 1500.0;
    /// <summary>"Directly ahead" for the queue: within ±this of the nose.</summary>
    public const double QueueAheadConeDeg = 30.0;
    /// <summary>The queue-moving watch looks this far ahead.</summary>
    public const double QueueAheadRangeFt = 600.0;

    /// <summary>One aircraft on or near the route ahead, as the queue sees it.</summary>
    /// <param name="AheadM">Along-route metres ahead of the pilot (NaN when not projected).</param>
    /// <param name="LateralM">Metres off the route line.</param>
    /// <param name="GsKts">Its ground speed.</param>
    /// <param name="DirectionTrue">The way it points/moves (the motion model's effective direction).</param>
    /// <param name="RouteBearingDeg">True bearing of the route segment it projects onto, in route order.</param>
    public readonly record struct QueueCandidate(
        double AheadM, double LateralM, double GsKts, double DirectionTrue, double RouteBearingDeg);

    /// <summary>The pilot's own queue, whether anything holds beyond it, and how far ahead its head is.</summary>
    /// <param name="Count">Aircraft in the contiguous line the pilot is in.</param>
    /// <param name="MoreBeyond">At least one more aircraft sits past the gap that ended that line.</param>
    /// <param name="HeadAheadM">Along-route metres to the line's farthest member; 0 when the line is empty.</param>
    public readonly record struct QueueCluster(int Count, bool MoreBeyond, double HeadAheadM);

    /// <summary>What the queue callout says.</summary>
    /// <param name="Position">1 = first.</param>
    /// <param name="AtRunwayHold">The head of the pilot's line stands at the route end on a route to the takeoff runway.</param>
    /// <param name="MoreBeyond">Another line holds past the gap (never said once at the runway hold).</param>
    public readonly record struct QueueReading(int Position, bool AtRunwayHold, bool MoreBeyond)
    {
        /// <summary>"departure queue" only where the queue feeds the runway; anywhere else "queue".</summary>
        public string Wording => AtRunwayHold ? "departure queue" : "queue";
    }

    /// <summary>
    /// Is this aircraft queued on the route ahead? On the route (≤ 30 m), 10–1,500 m ahead, stopped
    /// or creeping (≤ 6 kt), and pointing along the route (±35°) — which leaves out crossing traffic,
    /// head-on creepers, pushbacks moving tail-first, and aircraft parked nose-in beside the route.
    /// </summary>
    public static bool QualifiesForQueue(QueueCandidate c)
        => !double.IsNaN(c.AheadM)
           && c.LateralM <= QueueLateralMaxM
           && c.AheadM >= QueueMinAheadM && c.AheadM <= QueueScanM
           && c.GsKts <= QueueTrafficMaxGs
           && Math.Abs(AngleDiff(c.DirectionTrue, c.RouteBearingDeg)) <= QueueAlignMaxDeg;

    /// <summary>
    /// The contiguous line the pilot is in: walk outward from the pilot and stop at the first gap
    /// wider than <see cref="QueueLinkMaxGapM"/>. A queue well back from the runway is still the
    /// pilot's queue and may be the longer of the two; under-counting a line with a wide gap in it
    /// is the safe failure against merging two lines.
    /// </summary>
    /// <param name="aheadMetres">Along-route distance to each qualifying aircraft. Order does not matter.</param>
    public static QueueCluster QueueAheadOf(IEnumerable<double> aheadMetres)
    {
        var sorted = aheadMetres.Where(d => d >= 0).OrderBy(d => d).ToList();
        int count = 0;
        double previous = 0.0;          // the pilot
        foreach (double d in sorted)
        {
            if (d - previous > QueueLinkMaxGapM) break;
            count++;
            previous = d;
        }
        return new QueueCluster(count, sorted.Count > count, count > 0 ? previous : 0.0);
    }

    /// <summary>
    /// The queue reading. Both distances are measured FROM THE AIRCRAFT: <paramref name="routeEndAheadM"/>
    /// is the route end ahead of the pilot (the caller subtracts its own route position). "Departure
    /// queue" only when the route ends at the takeoff runway AND the head of the pilot's own line is
    /// within <see cref="QueueAtRunwayHoldM"/> of that end (R10/R13).
    /// </summary>
    public static QueueReading ReadQueue(IEnumerable<QueueCandidate> candidates,
        double? routeEndAheadM, bool routeEndsAtTakeoffRunway)
    {
        var cluster = QueueAheadOf(candidates.Where(QualifiesForQueue).Select(c => c.AheadM));
        bool atHold = routeEndsAtTakeoffRunway
                      && routeEndAheadM is double end
                      && end - cluster.HeadAheadM <= QueueAtRunwayHoldM;
        return new QueueReading(cluster.Count + 1, atHold, cluster.MoreBeyond && !atHold);
    }

    /// <summary>
    /// Has the aircraft ahead pulled away? TWO triggers, because one is not enough at a real
    /// holding point: the SPEED EDGE (stopped in the previous sample, rolling in this one) and the
    /// GAP (the distance has grown by <see cref="QueueGapOpenedFt"/> over the closest we were while
    /// it sat stopped ahead of us, <paramref name="stoppedGapFt"/>; NaN until seen stopped). The
    /// target's OWN motion is required for the gap trigger — our own pushback opens the gap too.
    /// </summary>
    public static bool QueueDeparted(double previousGs, double gs, double distFt, double stoppedGapFt)
    {
        bool speedEdge = previousGs <= QueueStoppedGs && gs >= QueueMovingGs;
        bool gapOpened = gs >= QueueCreepGs
                         && !double.IsNaN(stoppedGapFt)
                         && distFt - stoppedGapFt >= QueueGapOpenedFt;
        return speedEdge || gapOpened;
    }

    /// <summary>
    /// Keep this traffic sample? Ground: within the proximity range at taxi speed; or anything within
    /// <see cref="RunwayWatchGroundRangeM"/> while a runway is watched; or taxi-speed traffic within
    /// <see cref="QueueScanM"/> while on a queue route (R11 — the queue can see past 2,000 ft while
    /// queued, not only once a runway is watched). Airborne: only while a runway is watched, to the
    /// end of a 6 nm final.
    /// </summary>
    public static bool KeepInIntake(bool onGround, double distM, double gsKts, bool runwayWatchActive, bool queueScanActive)
    {
        if (onGround)
            return (distM * FeetPerMetre <= TrackRangeFt && gsKts <= MaxTaxiGsKts)
                   || (runwayWatchActive && distM <= RunwayWatchGroundRangeM)
                   || (queueScanActive && distM <= QueueScanM && gsKts <= MaxTaxiGsKts);
        return runwayWatchActive && distM <= RunwayWatchAirRangeM;
    }

    /// <summary>
    /// The ground-traffic sweep radius: just past what the intake can keep (L7 — was a fixed 278 km
    /// every second). The own aircraft is always inside, so a sweep always completes.
    /// </summary>
    public static uint SweepRadiusMeters(bool runwayWatchActive, bool queueScanActive)
        => runwayWatchActive ? (uint)(RunwayWatchAirRangeM + 500.0)
         : queueScanActive ? (uint)(QueueScanM + 500.0)
         : 1000u;

    /// <summary>Within ±<see cref="QueueAheadConeDeg"/> of the nose.</summary>
    public static bool IsDirectlyAhead(double relBearingDeg)
        => Math.Abs(AngleDiff(relBearingDeg, 0.0)) <= QueueAheadConeDeg;

    /// <summary>Directly ahead and within <see cref="QueueAheadRangeFt"/>.</summary>
    public static bool IsInQueueCone(double distFt, double relBearingDeg)
        => distFt <= QueueAheadRangeFt && IsDirectlyAhead(relBearingDeg);

    /// <summary>A pilot rolling at least this fast at ANY ground aircraft ahead keeps the sweep at 1 s (<see cref="NeedsFastPoll"/>).</summary>
    public const double FastPollOwnRollingKts = 3.0;

    /// <summary>
    /// Sweep every second? Only when it can change an answer: a runway watch, moving traffic within
    /// <see cref="FastPollRangeFt"/>, a stopped/creeping pilot (<paramref name="ownQueued"/>) with
    /// traffic in the queue cone ahead, or a pilot ROLLING (<paramref name="ownGsKts"/> at least
    /// <see cref="FastPollOwnRollingKts"/>) with any ground aircraft — parked included — directly ahead
    /// (±<see cref="QueueAheadConeDeg"/> of the nose) and within <paramref name="cautionDistFt"/>, the
    /// monitor's own speed-scaled Caution distance (<c>CAUTION_FT</c> plus the speed lead). Parked
    /// aircraft beside or behind the pilot, or further ahead, never keep it fast (L7); the slow 3 s
    /// cadence is what <c>ZONE_LEAD_SEC</c> was sized for.
    /// <para>The rolling case is PR #247 integration review Q4: a PARKED aircraft off the route is no route
    /// threat (<see cref="IsRouteThreat"/>), so it earns no "Slow down" — only "Stop", inside the fixed
    /// 250 ft — and with only parked traffic around the sweeps stayed on the 3 s cadence: a pilot who
    /// missed a bend and rolled at one at 12 kt heard "Stop" at about 200 ft. At 1 s it comes within a
    /// second's travel of the line.</para>
    /// </summary>
    public static bool NeedsFastPoll(bool runwayWatchActive, bool ownQueued, double ownGsKts, double cautionDistFt,
        IEnumerable<(double DistFt, double GsKts, double RelBearingDeg)> ground)
    {
        if (runwayWatchActive) return true;
        bool rolling = ownGsKts >= FastPollOwnRollingKts;
        foreach (var a in ground)
        {
            if (a.DistFt <= FastPollRangeFt && a.GsKts >= 1.0) return true;
            if (ownQueued && IsInQueueCone(a.DistFt, a.RelBearingDeg)) return true;
            if (rolling && a.DistFt <= cautionDistFt && IsDirectlyAhead(a.RelBearingDeg)) return true;
        }
        return false;
    }
}
