namespace MSFSBlindAssist.Services;

internal static partial class GroundTrafficLogic
{
    // ── Departure queue: has the aircraft we pulled up behind left? ──────────
    /// <summary>At or below this ground speed an aircraft ahead counts as stopped in the queue.</summary>
    public const double QueueStoppedGs = 1.5;
    /// <summary>Speed-edge trigger: rolling this fast in the sample after a stopped one.</summary>
    public const double QueueMovingGs = 2.0;
    /// <summary>Gap trigger: the target must be moving at least this fast to have opened the gap itself.</summary>
    public const double QueueCreepGs = 1.0;
    /// <summary>Gap trigger: growth over the stopped-gap baseline that counts as departed.</summary>
    public const double QueueGapOpenedFt = 60.0;

    /// <summary>
    /// Largest gap between two aircraft that still counts as ONE departure queue, and the
    /// largest gap between the pilot and the queue's tail that still counts as joining it.
    /// <para>
    /// A queue is a contiguous line. Aircraft hold roughly 50-100 m apart, so 250 m is well
    /// clear of normal spacing while being far smaller than the distance between two
    /// SEPARATE queues — the line at an intermediate holding point and the line at the
    /// runway hold beyond it.
    /// </para>
    /// </summary>
    public const double QueueLinkMaxGapM = 250.0;

    /// <summary>
    /// How many aircraft are in the departure queue the pilot is actually in, given every
    /// slow aircraft on the route ahead, as along-route distances in metres.
    /// <para>
    /// Counting everything on the route ahead — which is what the scan window alone does —
    /// MERGES separate queues. At a busy field the 1,500 m of route in front of a stopped
    /// aircraft can hold the line it has just joined at a holding point AND a second line
    /// at the runway hold several hundred metres further on, so the pilot is told they are
    /// seventh when they are third in the queue they are in and the other four are a
    /// different queue entirely.
    /// </para>
    /// <para>
    /// So walk outward from the pilot and stop at the first gap wider than
    /// <see cref="QueueLinkMaxGapM"/>. That yields the queue being joined WHEREVER it is —
    /// which is the point: a queue at a holding point well back from the runway is still
    /// the pilot's queue, and may well be the longer of the two.
    /// </para>
    /// </summary>
    /// <param name="aheadMetres">Along-route distance to each qualifying aircraft ahead.
    /// Order does not matter; the method sorts.</param>
    public static int QueueAheadCount(IEnumerable<double> aheadMetres)
        => QueueAheadOf(aheadMetres).Count;

    /// <summary>The pilot’s own queue, and whether anything is holding beyond it.</summary>
    /// <param name="Count">Aircraft in the contiguous line the pilot is in.</param>
    /// <param name="MoreBeyond">At least one more aircraft sits past the gap that ended
    /// that line — a separate group further along the route.</param>
    public readonly record struct QueueCluster(int Count, bool MoreBeyond);

    /// <summary>
    /// As <see cref="QueueAheadCount"/>, and also reports whether the gap that ended the
    /// pilot’s queue had anything beyond it. Knowing you are third in YOUR line is only
    /// half the picture when another line is holding between you and the runway.
    /// </summary>
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
        return new QueueCluster(count, sorted.Count > count);
    }

    /// <summary>
    /// Has the aircraft ahead pulled away? TWO triggers, because one is not enough at a real
    /// holding point.
    ///
    /// The SPEED EDGE (stopped in the previous sample, rolling in this one) is the fast one, but a
    /// queue CREEPS: an aircraft easing forward to 2 kt and holding there shows one edge at most,
    /// and once <paramref name="previousGs"/> is above the stopped gate the edge can never fire
    /// again — the pilot then sits behind an opening gap in silence. The GAP trigger catches it:
    /// the distance has grown by <see cref="QueueGapOpenedFt"/> over the closest we were while it
    /// sat stopped ahead of us (<paramref name="stoppedGapFt"/>, NaN until it has been seen
    /// stopped). The target's OWN motion is required there — our own pushback opens the gap just
    /// as well, and must not be reported as the queue moving.
    /// </summary>
    public static bool QueueDeparted(double previousGs, double gs, double distFt, double stoppedGapFt)
    {
        bool speedEdge = previousGs <= QueueStoppedGs && gs >= QueueMovingGs;
        bool gapOpened = gs >= QueueCreepGs
                         && !double.IsNaN(stoppedGapFt)
                         && distFt - stoppedGapFt >= QueueGapOpenedFt;
        return speedEdge || gapOpened;
    }
}
