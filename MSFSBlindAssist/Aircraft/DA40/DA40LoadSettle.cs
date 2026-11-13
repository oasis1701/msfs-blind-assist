namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// The quiet period after a flight load or a reconnect, during which the DA40's own
/// announcers RECORD what they see without speaking it.
///
/// ⚠️ WHY. Every DA40 announcer compares a new reading against the last one it spoke —
/// eight radio frequencies, both altimeters, the doors, the lamps, the CAS list, the graded
/// failures, engine health, the XLS mixture states. The definition object outlives a flight
/// load (and a reconnect), so the first values of flight 2 were compared against flight 1's
/// and every difference was announced as if it had just happened: a new airport's radios read
/// out one by one, the G1000's start-up cautions spoken as fresh. Wiping the baselines instead
/// is worse — a value that does NOT change on the load is never re-delivered, so its next
/// real change would be taken as a first reading and swallowed.
///
/// So the baselines are KEPT and quietly brought up to date: from a flight load until the
/// aircraft has published something and then stayed quiet for
/// <see cref="QuietDeliveries"/> batch cycles, or <see cref="MaxDeliveries"/> cycles
/// regardless. The same shape, and the same numbers, as the airliner FCU callouts' settle
/// (<c>FcuValueAnnouncer</c>).
/// </summary>
internal sealed class DA40LoadSettle
{
    internal const int QuietDeliveries = 5;
    internal const int MaxDeliveries = 30;

    private bool _published;
    private bool _movedThisCycle;
    private int _quiet;
    private int _total;

    public bool Settling { get; private set; }

    /// <summary>A flight load, a reconnect or a cache clear: start (or restart) the quiet period.</summary>
    public void Begin()
    {
        Settling = true;
        _published = false;
        _movedThisCycle = false;
        _quiet = 0;
        _total = 0;
    }

    /// <summary>A value was delivered — the aircraft is publishing.</summary>
    public void NoteChange()
    {
        if (!Settling) return;
        _published = true;
        _movedThisCycle = true;
    }

    /// <summary>One continuous-batch delivery; only batch 1 counts, once per cycle.</summary>
    public void OnBatchDelivered(int batchNum)
    {
        if (!Settling || batchNum != 1) return;

        _total++;
        if (_movedThisCycle) { _quiet = 0; _movedThisCycle = false; }
        else _quiet++;

        if ((_published && _quiet >= QuietDeliveries) || _total >= MaxDeliveries)
            Settling = false;
    }
}
