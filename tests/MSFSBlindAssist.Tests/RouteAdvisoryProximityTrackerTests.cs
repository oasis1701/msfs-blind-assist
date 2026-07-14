// Proximity zone state machine (design 2026-07-14 §2). Each test asserts the FULL
// event list returned by every Observe call (empty where silent) — not just presence —
// so a spurious extra event or a missing one both fail. The state machine is the
// authority table in the design doc; one test per row plus the latches and wording.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class RouteAdvisoryProximityTrackerTests
{
    private const string K = "SIGMET B4";

    private static LocationFact Fact(bool geom, bool inside, double? dist, bool behind)
        => new(geom, inside, dist, behind);

    /// <summary>One-key fact dictionary for the single-advisory tests.</summary>
    private static Dictionary<string, LocationFact> F(LocationFact f)
        => new(StringComparer.OrdinalIgnoreCase) { [K] = f };

    private static (string, RouteAdvisoryEvent, double?) Ev(RouteAdvisoryEvent e, double? dist)
        => (K, e, dist);

    private static void AssertEvents(
        (string, RouteAdvisoryEvent, double?)[] expected,
        IReadOnlyList<(string Key, RouteAdvisoryEvent Event, double? DistanceNm)> actual)
        => Assert.Equal(expected, actual);

    // --- first-sight matrix ------------------------------------------------------------

    [Fact]
    public void FirstSight_far_is_silent()
    {
        var t = new RouteAdvisoryProximityTracker();
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(F(Fact(true, false, 250, false))));        // far: silent, but tracked
        AssertEvents(new[] { Ev(RouteAdvisoryEvent.Approach, 90.0) },
            t.Observe(F(Fact(true, false, 90, false))));         // later inbound → Approach
    }

    [Fact]
    public void FirstSight_near_ahead_announces_approach()
    {
        var t = new RouteAdvisoryProximityTracker();
        AssertEvents(new[] { Ev(RouteAdvisoryEvent.Approach, 87.0) },
            t.Observe(F(Fact(true, false, 87, false))));
    }

    [Fact]
    public void FirstSight_near_behind_is_silent_and_latches()
    {
        var t = new RouteAdvisoryProximityTracker();
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(F(Fact(true, false, 87, true))));          // near but behind: silent, latched
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(F(Fact(true, false, 60, false))));         // dips ahead within ring: still silent
    }

    [Fact]
    public void FirstSight_inside_announces_at_position()
    {
        var t = new RouteAdvisoryProximityTracker();
        AssertEvents(new[] { Ev(RouteAdvisoryEvent.AtPosition, 0.0) },
            t.Observe(F(Fact(true, true, 0, false))));
    }

    [Fact]
    public void FirstSight_no_geometry_announces_once()
    {
        var t = new RouteAdvisoryProximityTracker();
        AssertEvents(new[] { Ev(RouteAdvisoryEvent.AnnounceOnce, (double?)null) },
            t.Observe(F(Fact(false, false, null, false))));
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(F(Fact(false, false, null, false))));      // identical next tick: nothing
    }

    [Fact]
    public void FirstSight_no_geometry_probe_inside_is_at_position()
    {
        var t = new RouteAdvisoryProximityTracker();
        AssertEvents(new[] { Ev(RouteAdvisoryEvent.AtPosition, (double?)null) },
            t.Observe(F(Fact(false, true, null, false))));
    }

    // --- transitions -------------------------------------------------------------------

    [Fact]
    public void Far_to_near_ahead_announces_once_with_hysteresis()
    {
        var t = new RouteAdvisoryProximityTracker();
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(F(Fact(true, false, 120, false))));        // Far
        AssertEvents(new[] { Ev(RouteAdvisoryEvent.Approach, 95.0) },
            t.Observe(F(Fact(true, false, 95, false))));         // Far→Near: Approach
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(F(Fact(true, false, 105, false))));        // still ≤110: no re-arm, silent
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(F(Fact(true, false, 95, false))));         // 105→95: silent (latched)
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(F(Fact(true, false, 115, false))));        // >110: re-arm, silent
        AssertEvents(new[] { Ev(RouteAdvisoryEvent.Approach, 95.0) },
            t.Observe(F(Fact(true, false, 95, false))));         // Far→Near again: Approach
    }

    [Fact]
    public void Behind_crossing_latches_silently()
    {
        var t = new RouteAdvisoryProximityTracker();
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(F(Fact(true, false, 120, true))));         // Far (behind)
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(F(Fact(true, false, 90, true))));          // Far→Near behind: silent, latched
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(F(Fact(true, false, 115, true))));         // >110: re-arm
        AssertEvents(new[] { Ev(RouteAdvisoryEvent.Approach, 90.0) },
            t.Observe(F(Fact(true, false, 90, false))));         // Far→Near ahead: announces
    }

    [Fact]
    public void Enter_fires_regardless_of_bearing()
    {
        var t = new RouteAdvisoryProximityTracker();
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(F(Fact(true, false, 87, true))));          // Near, behind-latched
        AssertEvents(new[] { Ev(RouteAdvisoryEvent.Enter, 0.0) },
            t.Observe(F(Fact(true, true, 0, false))));           // →Inside: Enter regardless of bearing
    }

    [Fact]
    public void Leave_needs_two_consecutive_outside_ticks()
    {
        var t = new RouteAdvisoryProximityTracker();
        AssertEvents(new[] { Ev(RouteAdvisoryEvent.AtPosition, 0.0) },
            t.Observe(F(Fact(true, true, 0, false))));           // Inside
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(F(Fact(true, false, 50, false))));         // out 1 tick
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(F(Fact(true, true, 0, false))));           // back inside: counter reset, no Leave
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(F(Fact(true, false, 50, false))));         // out 1 tick
        AssertEvents(new[] { Ev(RouteAdvisoryEvent.Leave, 50.0) },
            t.Observe(F(Fact(true, false, 50, false))));         // out 2 ticks: Leave once
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(F(Fact(true, false, 50, false))));         // still out: no repeat
    }

    [Fact]
    public void After_inside_approach_never_fires_again()
    {
        var t = new RouteAdvisoryProximityTracker();
        AssertEvents(new[] { Ev(RouteAdvisoryEvent.AtPosition, 0.0) },
            t.Observe(F(Fact(true, true, 0, false))));           // Inside (everInside latches)
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(F(Fact(true, false, 50, false))));         // out 1
        AssertEvents(new[] { Ev(RouteAdvisoryEvent.Leave, 50.0) },
            t.Observe(F(Fact(true, false, 50, false))));         // out 2: Leave → Near
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(F(Fact(true, false, 115, false))));        // drift >110: Near→Far, NO re-arm (everInside)
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(F(Fact(true, false, 90, false))));         // back ≤100: NO Approach (approach dead)
        AssertEvents(new[] { Ev(RouteAdvisoryEvent.Enter, 0.0) },
            t.Observe(F(Fact(true, true, 0, false))));           // re-entering: Enter again
    }

    [Fact]
    public void No_geometry_key_never_leaves()
    {
        var t = new RouteAdvisoryProximityTracker();
        AssertEvents(new[] { Ev(RouteAdvisoryEvent.AnnounceOnce, (double?)null) },
            t.Observe(F(Fact(false, false, null, false))));      // announce-once
        AssertEvents(new[] { Ev(RouteAdvisoryEvent.Enter, (double?)null) },
            t.Observe(F(Fact(false, true, null, false))));       // probe matches → Enter
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(F(Fact(false, false, null, false))));      // probe lost: NO Leave, silent
    }

    [Fact]
    public void Expiry_is_silent_even_while_inside()
    {
        var t = new RouteAdvisoryProximityTracker();
        AssertEvents(new[] { Ev(RouteAdvisoryEvent.AtPosition, 0.0) },
            t.Observe(F(Fact(true, true, 0, false))));           // Inside
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(new Dictionary<string, LocationFact>()));  // key gone: silent prune, even while Inside
        AssertEvents(new[] { Ev(RouteAdvisoryEvent.AtPosition, 0.0) },
            t.Observe(F(Fact(true, true, 0, false))));           // reappears: fresh first-sight
    }

    [Fact]
    public void Geometry_hiccup_freezes_zoning()
    {
        var t = new RouteAdvisoryProximityTracker();
        AssertEvents(new[] { Ev(RouteAdvisoryEvent.Approach, 95.0) },
            t.Observe(F(Fact(true, false, 95, false))));         // Near, latched
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(F(Fact(false, false, null, false))));      // tier-2 hiccup: frozen, silent
        // Zone/latch preserved through the hiccup — a fresh first-sight at 80 nm ahead
        // WOULD announce; the frozen-latched Near stays silent, proving no reset.
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(F(Fact(true, false, 80, false))));
    }

    [Fact]
    public void Reset_forgets_everything()
    {
        var t = new RouteAdvisoryProximityTracker();
        AssertEvents(new[] { Ev(RouteAdvisoryEvent.Approach, 95.0) },
            t.Observe(F(Fact(true, false, 95, false))));
        t.Reset();
        AssertEvents(new[] { Ev(RouteAdvisoryEvent.Approach, 95.0) },
            t.Observe(F(Fact(true, false, 95, false))));         // post-reset: fresh first-sight
    }

    [Fact]
    public void After_probe_at_position_inside_approach_never_fires_when_geometry_appears()
    {
        var t = new RouteAdvisoryProximityTracker();
        // No geometry, but probe says Inside at first sight → AtPosition
        AssertEvents(new[] { Ev(RouteAdvisoryEvent.AtPosition, (double?)null) },
            t.Observe(F(Fact(false, true, null, false))));
        // Now geometry appears: outside at 50 nm (first tick outside)
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(F(Fact(true, false, 50, false))));
        // Still outside at 50 nm (second tick outside → Leave)
        AssertEvents(new[] { Ev(RouteAdvisoryEvent.Leave, 50.0) },
            t.Observe(F(Fact(true, false, 50, false))));
        // Recede past rearm threshold (120 nm → Far)
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(F(Fact(true, false, 120, false))));
        // Re-approach within Approach ring at 90 nm ahead
        // BUG: before fix, this spuriously announces Approach (because ApproachLatched wasn't set)
        // FIXED: this must be silent (EverInside latches Approach off forever)
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(F(Fact(true, false, 90, false))));
    }

    [Fact]
    public void After_probe_enter_inside_approach_never_fires_when_geometry_appears()
    {
        var t = new RouteAdvisoryProximityTracker();
        // No geometry at first sight → AnnounceOnce
        AssertEvents(new[] { Ev(RouteAdvisoryEvent.AnnounceOnce, (double?)null) },
            t.Observe(F(Fact(false, false, null, false))));
        // Probe matches (becomes Inside) → Enter
        AssertEvents(new[] { Ev(RouteAdvisoryEvent.Enter, (double?)null) },
            t.Observe(F(Fact(false, true, null, false))));
        // Geometry appears: outside at 50 nm (first tick outside)
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(F(Fact(true, false, 50, false))));
        // Still outside at 50 nm (second tick outside → Leave)
        AssertEvents(new[] { Ev(RouteAdvisoryEvent.Leave, 50.0) },
            t.Observe(F(Fact(true, false, 50, false))));
        // Recede past rearm threshold (120 nm → Far)
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(F(Fact(true, false, 120, false))));
        // Re-approach within Approach ring at 90 nm ahead
        // BUG: before fix, this spuriously announces Approach
        // FIXED: this must be silent (EverInside latches Approach off forever)
        AssertEvents(Array.Empty<(string, RouteAdvisoryEvent, double?)>(),
            t.Observe(F(Fact(true, false, 90, false))));
    }

    // --- wording -----------------------------------------------------------------------

    [Fact]
    public void BuildProximityAnnouncement_wording_per_event()
    {
        var a = Assert.Single(ActiveSkyFormatting.ParseRouteAdvisories(
            "MHTG SIGMET J5 EMBD TS\r\nValid until: 2200z\r\n" +
            "MHCC CENTRAL AMERICAN FIR EMBD TS OBS TOPS FL520 STNR NC"));
        const string core = "MHTG SIGMET J5, Central American FIR, embedded thunderstorms, tops FL520";

        Assert.Equal($"Route advisory: {core}.",
            ActiveSkyFormatting.BuildProximityAnnouncement(RouteAdvisoryEvent.AnnounceOnce, a, null));
        Assert.Equal($"Route advisory, 87 nautical miles ahead: {core}.",
            ActiveSkyFormatting.BuildProximityAnnouncement(RouteAdvisoryEvent.Approach, a, 87));
        Assert.Equal($"Route advisory at your position: {core}.",
            ActiveSkyFormatting.BuildProximityAnnouncement(RouteAdvisoryEvent.AtPosition, a, 0));
        Assert.Equal($"Entering advisory area: {core}.",
            ActiveSkyFormatting.BuildProximityAnnouncement(RouteAdvisoryEvent.Enter, a, 0));
        Assert.Equal("Left advisory area: MHTG SIGMET J5.",
            ActiveSkyFormatting.BuildProximityAnnouncement(RouteAdvisoryEvent.Leave, a, null));
    }
}
