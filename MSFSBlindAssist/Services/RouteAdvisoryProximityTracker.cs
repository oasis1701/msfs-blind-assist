namespace MSFSBlindAssist.Services;

/// <summary>The five spoken events a route advisory can produce (design 2026-07-14 §2).</summary>
internal enum RouteAdvisoryEvent { AnnounceOnce, Approach, AtPosition, Enter, Leave }

/// <summary>
/// Proximity zone state machine for en-route advisories (design 2026-07-14 §2) — the
/// replacement for the seen-set + 15-minute-reminder tracker it supersedes. Pure:
/// one <see cref="Observe"/> call per weather tick maps a full <see cref="LocationFact"/>
/// set to the announcement events that just became true, keeping per-key zone
/// (<see cref="Zone"/>), an Approach latch, an ever-inside latch, and an outside-tick
/// counter. Announce when the aircraft first comes within <see cref="ApproachNm"/> nm of an
/// area (ahead), when it Enters, and when it Leaves — nothing else repeats.
///
/// Design invariants (see the §2 table — do not "simplify" these away):
/// <list type="bullet">
/// <item><b>NOT baseline-first as a whole.</b> Unlike the old tracker, a brand-new key
/// announces its current zone immediately (Near-ahead → Approach, Inside → AtPosition,
/// no-geometry → AnnounceOnce). "First sight far = silent" (distance &gt; 100) is what
/// replaces the baseline burst and also silences the hourly SIGMET re-issue churn — a
/// re-numbered area &gt;100 nm away stays quiet.</item>
/// <item><b>Behind-suppression is Approach-only.</b> A Near crossing while the area is
/// behind the aircraft latches Approach off silently (treated as announced). Enter and
/// Leave fire regardless of bearing — a behind-latched area you then fly into still
/// Enters.</item>
/// <item><b>Probe only strengthens.</b> A no-geometry key can gain Enter when the
/// positional probe later matches it (<c>Inside</c> becomes true), but probe-match loss is
/// NOT "outside" (another overlapping advisory may simply have become the probe response's
/// first block), so a no-geometry key can never Leave — once Inside it stays Inside
/// silently. A geometry hiccup (a previously-placed key that momentarily loses geometry,
/// tier-2 feed stall) freezes zoning: zone and latches are held, no distance transitions,
/// and only the probe can still drive Enter.</item>
/// <item><b>Silent expiry.</b> A key absent from the fact set is pruned with no event —
/// even while Inside. Announcing expiry would resurrect the hourly-churn double-announce
/// (old number expires + new number appears); the Shift+R box shows currency instead.</item>
/// <item><b>Inside latches Approach off forever.</b> Once a key has been Inside, Approach
/// never fires for it again; Enter/Leave still do (re-entry is a real event).</item>
/// </list>
///
/// <b>Complete-fact-set contract.</b> <see cref="Observe"/> MUST be called only with a fact
/// set covering EVERY current advisory. Any tracked key missing from <paramref name="facts"/>
/// is treated as expired and pruned (silently), so a partial dict from a mid-loop failure
/// would silently lose live zone state and re-announce those keys as first-sight next tick.
/// The caller therefore freezes the tick (does not call Observe) whenever the fact count
/// does not exactly match the advisory count; a genuinely empty feed (0 advisories → 0
/// facts) is the one valid empty call and correctly prunes everything.
///
/// <see cref="Reset"/> re-baselines fully (on connect / aircraft switch, as the old tracker did).
/// </summary>
internal sealed class RouteAdvisoryProximityTracker
{
    /// <summary>Ring at which an ahead advisory first announces (nm). Fixed constant, no setting.</summary>
    public const double ApproachNm = 100;
    /// <summary>Hysteresis band: a Near key re-arms Approach only after receding past this (nm).</summary>
    public const double RearmNm = 110;
    /// <summary>Consecutive not-inside ticks required to confirm a Leave (~1 min at the 30 s cadence).</summary>
    public const int LeaveConfirmTicks = 2;

    private enum Zone { Unplaced, Far, Near, Inside }

    private sealed class KeyState
    {
        public Zone Zone;
        public bool ApproachLatched;
        public bool EverInside;
        public int OutsideTicks;
    }

    private readonly Dictionary<string, KeyState> _states = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Maps the full fact set for this tick to the events that just became true.
    /// See the class summary for the complete-fact-set contract — callers freeze the tick
    /// on anything short of an exact-match fact set.</summary>
    public IReadOnlyList<(string Key, RouteAdvisoryEvent Event, double? DistanceNm)>
        Observe(IReadOnlyDictionary<string, LocationFact> facts)
    {
        var events = new List<(string, RouteAdvisoryEvent, double?)>();

        // Rule 1 — prune: any tracked key absent from the fact set has expired (silent),
        // even while Inside. Collect first to avoid mutating during enumeration.
        var stale = _states.Keys.Where(k => !facts.ContainsKey(k)).ToList();
        foreach (var k in stale) _states.Remove(k);

        foreach (var (key, fact) in facts)
        {
            if (!_states.TryGetValue(key, out var st))
            {
                // --- New key ---------------------------------------------------------
                st = new KeyState();
                _states[key] = st;
                if (!fact.HasGeometry)
                {
                    // Rule 2 — no geometry at first sight.
                    if (fact.Inside)
                    {
                        st.Zone = Zone.Inside;
                        st.EverInside = true;
                        events.Add((key, RouteAdvisoryEvent.AtPosition, fact.DistanceNm));
                    }
                    else
                    {
                        st.Zone = Zone.Unplaced;
                        events.Add((key, RouteAdvisoryEvent.AnnounceOnce, fact.DistanceNm));
                    }
                }
                else
                {
                    // Rule 4 — new geometry key.
                    double d = fact.DistanceNm ?? double.PositiveInfinity;
                    if (fact.Inside)
                    {
                        st.Zone = Zone.Inside;
                        st.EverInside = true;
                        st.ApproachLatched = true;
                        events.Add((key, RouteAdvisoryEvent.AtPosition, fact.DistanceNm));
                    }
                    else if (d <= ApproachNm)
                    {
                        st.Zone = Zone.Near;
                        st.ApproachLatched = true;
                        if (!fact.Behind)
                            events.Add((key, RouteAdvisoryEvent.Approach, fact.DistanceNm));
                        // behind → latched silently, no event
                    }
                    else
                    {
                        st.Zone = Zone.Far;
                    }
                }
                continue;
            }

            // --- Existing key --------------------------------------------------------
            if (st.Zone == Zone.Unplaced)
            {
                // Rule 3 — a no-geometry key never gains distance zoning ("nothing else
                // ever"). Its one possible transition: the probe matches → Enter, once.
                if (fact.Inside)
                {
                    st.Zone = Zone.Inside;
                    st.EverInside = true;
                    events.Add((key, RouteAdvisoryEvent.Enter, fact.DistanceNm));
                }
                continue;
            }

            if (!fact.HasGeometry)
            {
                // Rule 7 — geometry hiccup: freeze zoning (hold zone + latches, no distance
                // transitions); only the probe (fact.Inside) can still drive rule 5.
                if (fact.Inside && st.Zone != Zone.Inside)
                {
                    st.OutsideTicks = 0;
                    st.Zone = Zone.Inside;
                    st.EverInside = true;
                    st.ApproachLatched = true;
                    events.Add((key, RouteAdvisoryEvent.Enter, fact.DistanceNm));
                }
                else if (fact.Inside)
                {
                    st.OutsideTicks = 0;   // already Inside: hold, no re-Enter
                }
                continue;
            }

            if (fact.Inside)
            {
                // Rule 5 — inside (geometry).
                st.OutsideTicks = 0;
                if (st.Zone != Zone.Inside)
                {
                    st.Zone = Zone.Inside;
                    st.EverInside = true;
                    st.ApproachLatched = true;
                    events.Add((key, RouteAdvisoryEvent.Enter, fact.DistanceNm));
                }
                continue;
            }

            // Rule 6 — not inside (geometry).
            double dist = fact.DistanceNm ?? double.PositiveInfinity;
            switch (st.Zone)
            {
                case Zone.Inside:
                    st.OutsideTicks++;
                    if (st.OutsideTicks >= LeaveConfirmTicks)
                    {
                        st.Zone = dist <= ApproachNm ? Zone.Near : Zone.Far;
                        st.OutsideTicks = 0;
                        events.Add((key, RouteAdvisoryEvent.Leave, fact.DistanceNm));
                    }
                    break;

                case Zone.Near:
                    if (dist > RearmNm)
                    {
                        st.Zone = Zone.Far;
                        // Re-arm Approach only if the area has never been Inside — once
                        // Inside, Approach is dead forever.
                        if (!st.EverInside) st.ApproachLatched = false;
                    }
                    break;

                case Zone.Far:
                    if (dist <= ApproachNm)
                    {
                        st.Zone = Zone.Near;
                        if (!st.ApproachLatched)
                        {
                            st.ApproachLatched = true;
                            if (!fact.Behind)
                                events.Add((key, RouteAdvisoryEvent.Approach, fact.DistanceNm));
                            // behind → latched silently
                        }
                    }
                    break;
            }
        }

        return events;
    }

    /// <summary>Forget every key's zone/latches — re-baselines on connect / aircraft switch.</summary>
    public void Reset() => _states.Clear();
}
