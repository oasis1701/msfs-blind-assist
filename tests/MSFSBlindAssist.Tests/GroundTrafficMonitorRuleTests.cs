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

    [Fact]
    public void An_aircraft_crossing_the_route_does_not_hide_the_stopped_aircraft_beyond_it()
    {
        // PR #247 integration review P3: rolling at 12 kt, an aircraft crossing the route 110 m ahead (north at
        // 10 kt, inside the 30 m band for a few seconds) and one stopped on the route 190 m ahead. Only traffic
        // that OCCUPIES the route can be the first: the crossing one hid the stopped one, which lost its "on
        // your route" and its "Slow down" and got only "Stop". The transcript is the one the monitor gave
        // before the author's first-on-the-route rule was ported.
        var h = new GroundTrafficHarness { Context = RouteContext(East(3000), null, departure: false) };
        var crosser = Ac(1, 110, -40, 10, "Delta", "DAL1", headingDeg: 0);
        h.Sim.Traffic.Add(crosser);
        h.Sim.Traffic.Add(Ac(2, 190, 0, 0, "Lufthansa", "DLH2"));
        double ownM = 0, crosserNorthM = -40;
        h.Sim.Position = Own(ownM, 12);
        while (ownM < 175)
        {
            crosserNorthM += 10 * Kt;
            crosser.Latitude = crosserNorthM * M;
            ownM = Math.Min(ownM + 12 * Kt, 175);
            h.Sim.Position = Own(ownM, ownM >= 175 ? 0 : 12);
            h.Tick();
        }

        Assert.Equal(new[]
        {
            "t=3 [INT] Stop, Delta A320 very close, ahead, 300 feet.",
            "t=3 Delta A320 on your route, taxiway A, 300 feet ahead, crossing.",
            "t=6 [INT] Slow down, Lufthansa A320 ahead, 500 feet.",
            "t=6 Lufthansa A320 on your route, taxiway A, 500 feet ahead, stopped.",
            "t=12 [INT] Stop, Lufthansa A320 very close, ahead, 400 feet.",
        }, h.Transcript);
    }

    [Fact]
    public void Head_on_traffic_beyond_an_aircraft_crossing_the_route_is_called_at_once()
    {
        // PR #247 integration review P4: rolling at 10 kt, an aircraft crossing the route 120 m ahead (north at
        // 6 kt) and another coming head-on along the route from 420 m at 15 kt. Neither hides anything: the
        // crossing one does not occupy the route, and head-on traffic is never queued behind the first. The
        // head-on aircraft's "coming toward you" is spoken at 1,100 feet, not held until 500 feet — the
        // transcript the monitor gave before the author's first-on-the-route rule was ported.
        var h = new GroundTrafficHarness { Context = RouteContext(East(3000), null, departure: false) };
        var crosser = Ac(1, 120, -40, 6, "Delta", "DAL1", headingDeg: 0);
        var headOn = Ac(2, 420, 0, 15, "Lufthansa", "DLH2", headingDeg: 270);
        h.Sim.Traffic.Add(crosser);
        h.Sim.Traffic.Add(headOn);
        double ownM = 0, crosserNorthM = -40, headOnM = 420;
        h.Sim.Position = Own(ownM, 10);
        while (headOnM - ownM >= 40)
        {
            crosserNorthM += 6 * Kt;
            crosser.Latitude = crosserNorthM * M;
            headOnM -= 15 * Kt;
            headOn.Longitude = headOnM * M;
            ownM += 10 * Kt;
            h.Sim.Position = Own(ownM, 10);
            h.Tick();
        }

        Assert.Equal(new[]
        {
            "t=3 [INT] Stop, Delta A320 very close, ahead, 350 feet.",
            "t=3 Delta A320 converging from ahead, about 20 seconds.",
            "t=6 Lufthansa A320 on your route, taxiway A, 1100 feet ahead, coming toward you.",
            "t=21 [INT] Slow down, Lufthansa A320 ahead, 500 feet.",
            "t=24 [INT] Stop, Lufthansa A320 very close, ahead, 350 feet.",
        }, h.Transcript);
    }

    [Fact]
    public void Head_on_traffic_beyond_the_first_aircraft_on_the_route_is_never_queued_behind_it()
    {
        // A real first this time: an aircraft stopped 150 m ahead, 25 m off the centreline — inside the 30 m
        // on-route band, so it is the first, though it does not block the taxiway. Beyond it, one comes head-on
        // along the route from 420 m at 15 kt while the pilot rolls at 10 kt. Head-on traffic is never queued
        // behind the first, so it keeps its own "coming toward you" — at 1,100 feet, on the alert line after
        // the first's own (one alert line per 3 s) — instead of nothing until "Stop".
        var h = new GroundTrafficHarness { Context = RouteContext(East(3000), null, departure: false) };
        h.Sim.Traffic.Add(Ac(1, 150, 25, 0, "British Airways", "BAW1"));
        var headOn = Ac(2, 420, 0, 15, "Lufthansa", "DLH2", headingDeg: 270);
        h.Sim.Traffic.Add(headOn);
        double ownM = 0, headOnM = 420;
        for (int s = 0; s < 6; s++)
        {
            headOnM -= 15 * Kt;
            headOn.Longitude = headOnM * M;
            ownM += 10 * Kt;
            h.Sim.Position = Own(ownM, 10);
            h.Tick();
        }

        Assert.Contains("t=6 Lufthansa A320 on your route, taxiway A, 1100 feet ahead, coming toward you.", h.Transcript);
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

    // ── A parked aircraft off the route: no "Slow down", and "Stop" on time ─────────────────────────────

    [Fact]
    public void A_pilot_who_misses_a_bend_hears_Stop_within_a_second_of_the_250_ft_line()
    {
        // PR #247 integration review P5 — an accepted gap, and its mitigation. The route turns north 80 m past
        // its start; the pilot misses the turn and rolls straight on at 12 kt toward an aircraft parked 90 m
        // beyond the bend, off the route. A parked aircraft off the route is no route threat, so there is no
        // "Slow down" (the author's fix — the straight-line closest approach assumed the pilot keeps going
        // straight and made 421 false calls), only "Stop" once it is inside the fixed 250 ft. Rolling at it
        // inside the Caution distance keeps the sweep at 1 s, so that "Stop" comes within one second of the
        // line: "250 feet", where the 3 s sweep made it "200 feet".
        var route = new List<GroundTrafficRoutePoint>
        {
            new(0, 0, "A", 0), new(0, 80 * M, "A", 80), new(300 * M, 80 * M, "B", 380),
        };
        var h = new GroundTrafficHarness { Context = RouteContext(route, null, departure: false) };
        h.Sim.Traffic.Add(Ac(1, 170, 0, 0, "British Airways", "BAW1"));
        double ownM = -150, gapFtAtStop = double.NaN;
        h.Sim.Position = Own(ownM, 12);
        while (170 - ownM >= 20 && double.IsNaN(gapFtAtStop))
        {
            ownM += 12 * Kt;
            h.Sim.Position = Own(ownM, 12);
            h.Tick();
            if (h.Said.Interrupts.Any(m => m.StartsWith("Stop, British Airways")))
                gapFtAtStop = (170 - ownM) * GroundTrafficLogic.FeetPerMetre;
        }

        Assert.Equal(new[]
        {
            "t=18 British Airways A320, ahead, 700 feet, stopped.",
            "t=40 [INT] Stop, British Airways A320 very close, ahead, 250 feet.",
        }, h.Transcript);
        // Within one second's travel at 12 kt (about 20 ft) of the 250 ft line.
        Assert.InRange(gapFtAtStop, 250 - 12 * Kt * GroundTrafficLogic.FeetPerMetre, 250);
    }

    // ── A Warning withheld outside the forward arc is not recorded ───────────────────────────────────

    [Fact]
    public void Traffic_very_close_behind_earns_Stop_once_the_pilot_turns_to_face_it()
    {
        // PR #247 integration review Q5: an aircraft parked 50 m behind the pilot — inside the Warning distance,
        // outside the ±120° forward arc, so "Stop" is withheld. The forward-arc branch recorded that Warning as
        // if it had been spoken, so when the pilot then turned to face the aircraft, Warning was no longer an
        // escalation and "Stop" never came. A withheld Warning is never recorded, here as everywhere else.
        var h = new GroundTrafficHarness { Context = RouteContext(East(1000), null, departure: false) };
        h.Sim.Traffic.Add(Ac(1, 450, 0, 0, "British Airways", "BAW1"));
        h.Sim.Position = Own(500, 3);                        // heading east: the aircraft is dead astern
        h.Tick(3);                                           // the first sweep, on the third tick
        Assert.Empty(h.Said.All);

        h.Sim.Position = Own(500, 3, headingDeg: 270);       // turned to face it
        h.Tick(3);

        Assert.Equal(new[] { "t=4 [INT] Stop, British Airways A320 very close, ahead, 175 feet." }, h.Transcript);
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

    // The watch's first status comes from a sweep requested AFTER the watch started, and two protections keep a
    // stale sweep from giving it: the READINESS GATE (a sweep requested before the watch started gives no runway
    // evaluation) and the CYCLE (a completion evaluates the watch and watch gate its own request saw, PR #247
    // review L9, never the watch in progress). A sweep requested before the watch existed at all is stopped by
    // both, so a test of that alone passes with either one removed; each test below leaves exactly one standing
    // (PR #247 integration review, Minor 8).
    private static AiTrafficDataEventArgs OnOneMileFinal()
        => Ac(1, Threshold09EastM - 1852, RunwayNorthM, 140, "British Airways", "BAW1", onGround: false, altitudeFt: 300);

    private const string FirstStatusWithTheFinal =
        "Runway 09: no traffic seen on the runway. British Airways A320 on final runway 09, 1.0 miles.";

    [Fact]
    public void The_first_runway_status_waits_for_a_sweep_requested_after_the_watch_restarted()
    {
        // Pins the READINESS GATE. A database switch restarts the watch under the SAME key within one tick, while the
        // sweep requested under its previous run is still outstanding: that sweep's cycle names the very watch in
        // progress, so the cycle lets it through and only its request time, before the restart, stops it. Its
        // completion gives no first status (with this gate removed it gave one); the next sweep does.
        var h = AtTheHold(holdingShort: true);
        h.Sim.Traffic.Add(OnOneMileFinal());

        h.TickOnly();                                // t=1: the watch starts; a sweep is requested
        h.Monitor.ClearRunwayCache();                // a database switch
        h.TickOnly();                                // t=2: the watch restarts under the same key
        h.Sim.CompleteSweep();                       // the sweep requested before the restart completes

        Assert.Empty(h.Said.All);

        h.Tick();                                    // t=3: the first sweep requested after the restart
        Assert.Equal(new[] { "t=3 " + FirstStatusWithTheFinal }, h.Transcript);
    }

    [Fact]
    public void The_first_runway_status_ignores_a_sweep_requested_while_the_watch_was_suspended()
    {
        // Pins the CYCLE. A watch suspended by its gate RESUMES with its original start time, so a sweep requested
        // during the suspension passes the readiness gate — but its entries came in unwatched and the aircraft on
        // final was dropped. Evaluated as the resumed watch (with the cycle removed) it said "Runway 09: no traffic
        // seen on the runway or on final." with an aircraft on a 1 nm final. Its own cycle had no watch and a
        // closed gate, so it gives no first status; the next sweep does, and names the aircraft on final.
        bool suppressed = false;
        var h = AtTheHold(holdingShort: true);
        h.Monitor.RunwayWatchSuppressCheck = () => suppressed;
        h.Sim.Traffic.Add(OnOneMileFinal());

        h.TickOnly();                                // t=1: the watch starts; a sweep is requested
        suppressed = true;
        h.TickOnly();                                // t=2: the gate closes — the watch is suspended
        h.Sim.CompleteSweep();                       // that sweep completes with the gate closed: nothing evaluated
        h.TickOnly();                                // t=3: still suspended — a sweep is requested with no watch
        h.Sim.DeliverEntries();                      // its entries arrive unwatched: the aircraft on final is dropped
        suppressed = false;
        h.TickOnly();                                // t=4: the gate reopens — the watch resumes, same start time
        h.Sim.DeliverCompletion();                   // the sweep requested while suspended completes

        Assert.Empty(h.Said.All);

        h.Tick();                                    // t=5: the first sweep requested after the resume
        Assert.Equal(new[] { "t=5 " + FirstStatusWithTheFinal }, h.Transcript);
    }
}
