// The reviewed monitor's own highest-risk rules, driven through the headless harness
// (GroundTrafficHarness: the real tick, intake, sweep bookkeeping, evaluation and speech policy, one
// simulated second at a time). The pure units behind each rule have their own characterization tests;
// these pin that the monitor actually wires them together as documented.
//
// Geometry: the equator, heading east unless stated, so 1 m east = 1 / 111320 degrees of longitude.

using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;
using static MSFSBlindAssist.Tests.GroundTrafficHarness;

namespace MSFSBlindAssist.Tests;

public class GroundTrafficMonitorRuleTests
{
    private static GroundTrafficHarness OnRoute(double ownGs)
    {
        var h = new GroundTrafficHarness { Context = RouteContext(East(1000), null, departure: false) };
        h.Sim.Position = Own(0, ownGs);
        return h;
    }

    private static void MoveTo(AiTrafficDataEventArgs ac, double eastM, double northM, double gsKts,
        double altitudeFt = 0.0, bool onGround = true)
    {
        ac.Longitude = eastM * M;
        ac.Latitude = northM * M;
        ac.GroundSpeedKnots = gsKts;
        ac.AltitudeFt = altitudeFt;
        ac.OnGround = onGround;
    }

    // ── Only the first aircraft on the route is called — but "Stop" is never withheld ─────────────────

    [Fact]
    public void An_aircraft_queued_behind_the_first_still_earns_Stop_when_very_close()
    {
        // Both inside the Warning distance at 6 kt (250 ft + the speed lead, about 98 m): the first gets its
        // "Stop", and the one queued 30 m behind it still gets its own — the author's rule withholds only
        // Awareness and Caution for traffic behind the first.
        var h = OnRoute(ownGs: 6);
        h.Sim.Traffic.Add(Ac(1, 30, 0, 0, "British Airways", "BAW1"));
        h.Sim.Traffic.Add(Ac(2, 60, 0, 0, "Lufthansa", "DLH2"));

        h.Tick(6);

        Assert.Contains(h.Said.Interrupts, m => m.StartsWith("Stop, British Airways"));
        Assert.Contains(h.Said.Interrupts, m => m.StartsWith("Stop, Lufthansa"));
    }

    // ── A "Stop" withheld while traffic moves away is not swallowed ─────────────────────────────────

    [Fact]
    public void Traffic_that_pulls_away_then_stops_inside_the_Warning_distance_still_earns_Stop()
    {
        // 60 m ahead, inside the Warning distance (about 98 m at 6 kt), pulling away at 20 kt: moving
        // away, so no "Stop". Then it stops where it is. A withheld Warning is never recorded as if it had
        // been spoken, so the next evaluation says "Stop" — recording it silently while the aircraft
        // pulled away swallowed that "Stop" for good.
        var h = OnRoute(ownGs: 6);
        var leader = Ac(1, 60, 0, 20, "British Airways", "BAW1");
        h.Sim.Traffic.Add(leader);

        h.Tick(4);
        Assert.DoesNotContain(h.Said.All, m => m.StartsWith("Stop,"));

        leader.GroundSpeedKnots = 0;
        h.Tick(2);
        Assert.Contains(h.Said.Interrupts, m => m.StartsWith("Stop, British Airways"));
    }

    private const double Kt = 0.514444;   // metres a second per knot

    /// <summary>
    /// The pilot behind one leader on a straight route east, both starting stopped. Each <see cref="Step"/>
    /// is one simulated second: both move by their speed (<see cref="OwnMps"/>, <see cref="LeadMps"/>, set by
    /// the test), then the monitor ticks and the sweep is answered.
    /// </summary>
    private sealed class FollowingLeader
    {
        public GroundTrafficHarness H { get; } = new() { Context = RouteContext(East(3000), null, departure: false) };
        private readonly AiTrafficDataEventArgs _leader;
        public double OwnM, OwnMps, LeadM, LeadMps;
        public int Second { get; private set; }

        public FollowingLeader(double leadM)
        {
            LeadM = leadM;
            H.Sim.Position = Own(0, 0);
            _leader = Ac(1, leadM, 0, 0, "British Airways", "BAW1");
            H.Sim.Traffic.Add(_leader);
        }

        public double GapM => LeadM - OwnM;
        public bool StopSpoken => H.Said.Interrupts.Any(m => m.StartsWith("Stop, British Airways"));

        public void Step()
        {
            Second++;
            LeadM += LeadMps;
            OwnM += OwnMps;
            H.Sim.Position = Own(OwnM, OwnMps / Kt);
            _leader.Longitude = LeadM * M;
            _leader.GroundSpeedKnots = LeadMps / Kt;
            H.Tick();
        }
    }

    [Fact]
    public void A_leader_that_departs_and_stops_again_ahead_earns_Stop_as_soon_as_it_stops()
    {
        // PR #247 integration review P1 (a queue hop): stopped 85 m behind a stopped leader, the pilot follows
        // it at 10 kt as it departs at up to 16 kt; six seconds later it stops again, 102 m ahead — inside the
        // Warning distance (about 112 m at 10 kt). While it pulls away, no "Stop"; on the first evaluation
        // that sees it stopped, "Stop". On the reviewed base this "Stop" was swallowed: withheld while the
        // leader opened, the Warning was recorded as if spoken, and a stop was no longer an escalation.
        var s = new FollowingLeader(leadM: 85);
        int stoppedAt = -1, stopAt = -1;
        while (s.Second < 20 && stopAt < 0)
        {
            int t = s.Second + 1;
            if (t >= 5 && t < 11) s.LeadMps = Math.Min(16 * Kt, s.LeadMps + 2.5);
            else if (t >= 11) { s.LeadMps = 0; if (stoppedAt < 0) stoppedAt = t; }
            if (t >= 6) s.OwnMps = Math.Min(10 * Kt, s.OwnMps + 1.2);
            s.Step();
            if (s.StopSpoken) stopAt = s.Second;
        }

        Assert.Equal(11, stoppedAt);
        Assert.Equal(stoppedAt, stopAt);   // not while it pulled away, and not a second after it stopped
    }

    [Fact]
    public void Following_a_departing_leader_earns_no_Stop_while_it_pulls_away()
    {
        // PR #247 integration review P2: the leader departs and the pilot follows three seconds later with the
        // same acceleration; both settle at 12 kt with the gap, about 103 m, inside the Warning distance (about
        // 119 m at 12 kt). The gap never closes. The "Stop" withheld while the leader opened at 1 m/s or more
        // stays withheld once the pilot, catching up to its speed, brings the opening below that — it fired
        // there, at 350 feet, before the hold-down. Then the leader stops: "Stop" at once.
        var s = new FollowingLeader(leadM: 85);
        while (s.Second < 45)
        {
            int t = s.Second + 1;
            if (t >= 5) s.LeadMps = Math.Min(12 * Kt, s.LeadMps + 0.5);
            if (t >= 8) s.OwnMps = Math.Min(12 * Kt, s.OwnMps + 0.5);
            s.Step();
        }
        Assert.DoesNotContain(s.H.Said.All, m => m.StartsWith("Stop,"));
        Assert.InRange(s.GapM, 100, 106);   // still inside the Warning distance: it was held, not out of range

        s.LeadMps = 0;
        s.Step();
        Assert.True(s.StopSpoken, "no Stop once the leader stopped");
    }

    [Fact]
    public void A_leader_that_pulls_away_then_slows_to_4_kt_earns_Stop_once_the_pilot_closes_on_it()
    {
        // Why the withheld "Stop" is held down rather than recorded until the leader stops: this leader never
        // stops. As in the previous test both settle at 12 kt, 103 m apart; then it slows to 4 kt. It is still
        // MOVING, but the pilot closes on it at 1 m/s and more — "Stop" on that first evaluation. Recorded
        // until the leader stopped, the Warning would never have been an escalation again.
        var s = new FollowingLeader(leadM: 85);
        while (s.Second < 30)
        {
            int t = s.Second + 1;
            if (t >= 5) s.LeadMps = Math.Min(12 * Kt, s.LeadMps + 0.5);
            if (t >= 8) s.OwnMps = Math.Min(12 * Kt, s.OwnMps + 0.5);
            s.Step();
        }
        Assert.DoesNotContain(s.H.Said.All, m => m.StartsWith("Stop,"));

        int stopAt = -1;
        while (s.Second < 40 && stopAt < 0)
        {
            s.LeadMps = Math.Max(4 * Kt, s.LeadMps - 1.0);
            s.Step();
            if (s.StopSpoken) stopAt = s.Second;
        }
        Assert.Equal(31, stopAt);   // the first second it closes, at 1 m/s
    }

    // ── "Stop" is never withheld on a first Warning ──────────────────────────────────────────────────

    [Fact]
    public void A_Slow_down_that_keeps_closing_reaches_Stop_inside_the_repeat_window()
    {
        // Rolling at 10 kt toward an aircraft parked on the route 190 m ahead (Caution inside about 158 m,
        // Warning inside about 112 m at that speed). "Slow down" is spoken first; "Stop" follows nine
        // seconds later, well inside the 15 s repeat window — Caution → Warning is never suppressed.
        var h = OnRoute(ownGs: 10);
        h.Sim.Traffic.Add(Ac(1, 190, 0, 0, "British Airways", "BAW1"));
        double ownEastM = 0;

        int slowDownAt = -1, stopAt = -1;
        for (int s = 1; s <= 21; s++)
        {
            ownEastM += 10 * 0.514444;
            h.Sim.Position = Own(ownEastM, 10);
            h.Tick();
            if (slowDownAt < 0 && h.Said.Interrupts.Any(m => m.StartsWith("Slow down, British Airways"))) slowDownAt = s;
            if (stopAt < 0 && h.Said.Interrupts.Any(m => m.StartsWith("Stop, British Airways"))) stopAt = s;
        }

        Assert.True(slowDownAt > 0, "no Slow down");
        Assert.True(stopAt > slowDownAt, "no Stop after the Slow down");
        Assert.True(stopAt - slowDownAt < GroundTrafficLogic.EscalationRepeatWindowMs / 1000,
            $"Stop came {stopAt - slowDownAt} s after Slow down, not inside the repeat window");
    }

    [Fact]
    public void Stop_is_spoken_while_the_announcer_is_suppressed_and_nothing_queued_is_lost()
    {
        // The aircraft-switch grace suppresses the announcer: only an interrupt is planned, so "Stop" still
        // speaks, and a queued line is neither spoken nor marked spoken — it goes out once the grace ends.
        var h = OnRoute(ownGs: 6);
        h.Sim.Traffic.Add(Ac(1, 60, 0, 0, "British Airways", "BAW1"));
        h.Sim.Traffic.Add(Ac(2, 150, 60, 0, "Lufthansa", "DLH2"));   // off the route, ahead and to the left
        h.Said.Suppressed = true;

        h.Tick(3);
        Assert.Contains(h.Said.Interrupts, m => m.StartsWith("Stop, British Airways"));
        Assert.Equal(h.Said.Interrupts, h.Said.All);   // nothing queued went out (British Airways' "on your route" either)

        h.Said.Suppressed = false;
        h.Tick(3);
        Assert.Contains(h.Said.All, m => m.StartsWith("Lufthansa A320, ahead and to the left"));
    }

    // ── The runway watch ────────────────────────────────────────────────────────────────────────────

    // Runway 09/27, 3 km long, 1 km north of the origin: the 09 threshold 1 km east, the 27 threshold 4 km
    // east. The pilot holds short of it on the south side near the 27 end (70 m from the centreline), far
    // outside the proximity range of anything rolling on the 09 half.
    private const double RunwayNorthM = 1000, Threshold09EastM = 1000, Threshold27EastM = 4000;

    private static TaxiGraph.RunwayCenterline Runway0927() => new()
    {
        Name1 = "09", Name2 = "27", HeadingDeg1 = 90, HalfWidthMeters = 22.86,
        Lat1 = RunwayNorthM * M, Lon1 = Threshold09EastM * M,
        Lat2 = RunwayNorthM * M, Lon2 = Threshold27EastM * M,
    };

    private static GroundTrafficRouteContext Context(TaxiGraph.RunwayCenterline runway, bool holdingShort) => new()
    {
        Runways = new[] { runway },
        AirportIcao = "TEST",
        State = holdingShort ? TaxiGuidanceState.HoldShort : TaxiGuidanceState.Taxiing,
        HeldRunwayLabel = holdingShort ? "Runway 09" : null,
        RouteAhead = new List<GroundTrafficRoutePoint>
        {
            new((RunwayNorthM - 70) * M, 3800 * M, "C", 0), new((RunwayNorthM + 100) * M, 3800 * M, "C", 170),
        },
    };

    private static GroundTrafficHarness AtTheHold(bool holdingShort)
    {
        var runway = Runway0927();
        var h = new GroundTrafficHarness { Context = Context(runway, holdingShort) };
        h.Sim.Position = Own(3800, 0, northM: RunwayNorthM - 70, headingDeg: 0);
        return h;
    }

    [Fact]
    public void A_landing_aircraft_on_the_watched_runway_is_announced_once()
    {
        // On final → over the pavement (landing) → touchdown → a long roll on the runway. It is a known
        // final AND a known occupant for the grace period after touchdown; the final's cleanup must never
        // touch the occupant's record, or it is announced again (PR #247 re-review Critical 1 / M1).
        var h = AtTheHold(holdingShort: true);
        var ac = Ac(1, Threshold09EastM - 1852, RunwayNorthM, 140, "British Airways", "BAW1",
            onGround: false, altitudeFt: 300);                                  // 1.0 nm final
        h.Sim.Traffic.Add(ac);

        h.Tick();                                                                // the watch's first status
        MoveTo(ac, Threshold09EastM + 300, RunwayNorthM, 135, altitudeFt: 40, onGround: false);   // landing
        h.Tick();
        double east = Threshold09EastM + 600, gs = 120;
        MoveTo(ac, east, RunwayNorthM, gs);                                      // touchdown
        h.Tick();
        for (int s = 0; s < 8; s++)                                              // rolling out, well past the 3 s grace
        {
            gs -= 10;
            east += gs * 0.514444;
            MoveTo(ac, east, RunwayNorthM, gs);
            h.Tick();
        }

        Assert.Single(h.Said.All, m => m.Contains("British Airways A320 on final runway 09")
                                       || m.Contains("British Airways A320 landing runway 09"));
        Assert.Single(h.Said.All, m => m.Contains("British Airways A320 on runway 09"));
    }

    [Fact]
    public void The_first_runway_status_waits_for_a_sweep_requested_after_the_watch_started()
    {
        // A sweep requested BEFORE the watch started was taken in by an intake that drops airborne traffic —
        // here its entries arrive while nothing is watched, so the aircraft on final is dropped. Its
        // completion must not produce the watch's first status ("no traffic seen on the runway or on
        // final", said with an aircraft on short final); the next sweep, requested after the watch
        // started, does, and names the aircraft on final.
        var h = AtTheHold(holdingShort: false);
        h.Sim.Traffic.Add(Ac(1, Threshold09EastM - 1852, RunwayNorthM, 140, "British Airways", "BAW1",
            onGround: false, altitudeFt: 300));

        h.TickOnly();
        h.TickOnly();
        h.TickOnly();                                // the slow cadence requests a sweep on the third tick
        Assert.NotEqual(0u, h.Sim.PendingSweepId);
        h.Sim.DeliverEntries();                      // nothing watched yet: the airborne aircraft is dropped

        h.Context = Context(Runway0927(), holdingShort: true);
        h.TickOnly();                                // the watch starts; the old sweep is still outstanding
        h.Sim.DeliverCompletion();

        Assert.DoesNotContain(h.Said.All, m => m.StartsWith("Runway 09"));

        h.Tick();                                    // the first sweep requested after the watch started
        Assert.Contains(h.Said.All, m => m.StartsWith("Runway 09") && m.Contains("British Airways A320 on final runway 09"));
        Assert.DoesNotContain(h.Said.All, m => m.Contains("no traffic seen on the runway or on final"));
    }
}
