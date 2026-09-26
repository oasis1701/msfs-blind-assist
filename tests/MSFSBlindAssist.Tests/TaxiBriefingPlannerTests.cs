// tests/MSFSBlindAssist.Tests/TaxiBriefingPlannerTests.cs
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Navigation.Briefing;
using MSFSBlindAssist.Services.SayIntentions;
using static MSFSBlindAssist.Tests.TaxiBriefingFixture;

namespace MSFSBlindAssist.Tests;

public class TaxiBriefingPlannerTests
{
    private static readonly AircraftProfile Md11F = AircraftSizeClass.Resolve("MD1F", "MD-11F", 0);
    private static readonly AircraftProfile B738 = AircraftSizeClass.Resolve("B738", "Boeing 737-800", 189);
    private static readonly AircraftProfile A388 = AircraftSizeClass.Resolve("A388", "Airbus A380-800", 500);

    // ── taxi out ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Freighter_taxis_out_from_the_cargo_stand_via_A_and_E1_with_both_hold_shorts()
    {
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(Md11F, airline: "UPS"), Airport());

        Assert.Null(leg.Unavailable);
        Assert.Equal(BriefingTier.Navdata, leg.Tier);
        Assert.StartsWith("representative stand C 1", leg.EndpointDescription);
        Assert.Equal(new[] { "A", "E1" }, leg.Taxiways);
        Assert.InRange(leg.DistanceMetres, 2600, 2800);
        Assert.Contains(leg.HoldShorts, h => (h.Runway == "18" || h.Runway == "36") && h.Taxiway == "A" && !h.BeforeEntering);
        Assert.Contains(leg.HoldShorts, h => h.Runway == "09" && h.Taxiway == "E1" && h.BeforeEntering);
        Assert.Equal(2, leg.HoldShorts.Count);
        Assert.Empty(leg.NarrowTaxiways);
    }

    [Fact]
    public void Parked_at_the_origin_the_taxi_out_starts_from_the_current_position()
    {
        var own = new OwnPosition(Lat(250), Lon(2500), OnGround: true);
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, own: own), Airport());

        Assert.Null(leg.Unavailable);
        Assert.StartsWith("current position", leg.EndpointDescription);
        Assert.Null(leg.Stand);
        Assert.Equal(new[] { "A", "E1" }, leg.Taxiways);
    }

    [Fact]
    public void Airborne_or_far_away_the_taxi_out_uses_a_representative_stand()
    {
        var airborne = new OwnPosition(Lat(250), Lon(2500), OnGround: false);
        Assert.StartsWith("representative stand", TaxiBriefingPlanner.PlanTaxiOut(Request(B738, own: airborne), Airport()).EndpointDescription);

        var elsewhere = new OwnPosition(Lat(20000), Lon(2500), OnGround: true);   // 19.5 km north of the field
        Assert.StartsWith("representative stand", TaxiBriefingPlanner.PlanTaxiOut(Request(B738, own: elsewhere), Airport()).EndpointDescription);
    }

    [Fact]
    public void Airliner_taxis_out_from_its_airline_s_gate()
    {
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, airline: "DAL"), Airport());
        Assert.StartsWith("representative stand G 1", leg.EndpointDescription);
        Assert.Equal(StandChoiceSource.AirlineMatch, leg.Stand!.Source);
    }

    [Fact]
    public void Unknown_runway_is_reported()
    {
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, originRunway: "04"), Airport());
        Assert.Equal("runway 04 is not in the navigation database for TEST", leg.Unavailable);
    }

    [Fact]
    public void A_flight_plan_without_a_runway_says_so_instead_of_naming_a_blank_one()
    {
        // An empty SimBrief runway gave "runway  is not in the navigation database for EGLL".
        Assert.Equal("the flight plan names no departure runway",
            TaxiBriefingPlanner.PlanTaxiOut(Request(B738, originRunway: ""), Airport()).Unavailable);
        Assert.Equal("the flight plan names no arrival runway",
            TaxiBriefingPlanner.PlanTaxiIn(Request(B738, destRunway: " "), Airport()).Unavailable);
    }

    [Fact]
    public void A_taxi_in_says_its_exits_were_searched_only_once_its_runway_was_found()
    {
        Assert.False(TaxiBriefingPlanner.PlanTaxiIn(Request(B738, destRunway: "04"), Airport()).ExitsSearched);   // runway unknown
        Assert.False(TaxiBriefingPlanner.PlanTaxiIn(Request(B738, destRunway: ""), Airport()).ExitsSearched);     // no runway at all
        Assert.True(TaxiBriefingPlanner.PlanTaxiIn(Request(B738, destRunway: "18"), Airport()).ExitsSearched);    // searched, none found
        Assert.True(TaxiBriefingPlanner.PlanTaxiIn(Request(B738), Airport()).ExitsSearched);                      // planned
        var noStands = Airport() with { Spots = new List<ParkingSpot>() };
        Assert.True(TaxiBriefingPlanner.PlanTaxiIn(Request(B738), noStands).ExitsSearched);                       // no stand
    }

    [Fact]
    public void An_unnamed_exit_reads_as_unnamed_in_the_reason_it_cannot_be_routed()
    {
        // "no taxi route connects exit  to …" named a blank.
        var c172 = AircraftSizeClass.Resolve("C172", "Cessna 172", 4);
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(c172), AirportWithUnnamedDeadEndExit());

        Assert.Equal("", leg.Exit!.Exit.TaxiwayName);
        Assert.StartsWith("no taxi route connects exit (unnamed) to ", leg.Unavailable);
    }

    [Fact]
    public void Runway_ids_match_without_leading_zeros()
    {
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, originRunway: "9"), Airport());
        Assert.Null(leg.Unavailable);
        Assert.Equal("09", leg.Runway);
    }

    // ── taxi in ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Airliner_lands_on_09_exits_left_at_C_and_taxis_to_the_say_intentions_gate()
    {
        var gate = new SayIntentionsGateHint("Terminal 1 Gate G1", null);
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, gate: gate), Airport());

        Assert.Null(leg.Unavailable);
        Assert.Equal("C", leg.Exit!.Exit.TaxiwayName);
        Assert.Equal("Left", leg.Exit.Exit.ExitSide);
        Assert.True(leg.Exit.ComfortablyReachable);
        Assert.Equal("D", leg.Exit.NextExit!.TaxiwayName);
        Assert.Equal(new[] { "B", "C", "D" }, leg.VacatingExits.Select(e => e.TaxiwayName));
        Assert.Equal("SayIntentions assigned gate G 1", leg.EndpointDescription);
        Assert.Equal(new[] { "A" }, leg.Taxiways);
        Assert.InRange(leg.DistanceMetres, 1550, 1750);
        var hold = Assert.Single(leg.HoldShorts);
        Assert.True(hold.Runway is "18" or "36");
        Assert.Equal("A", hold.Taxiway);
        Assert.False(hold.BeforeEntering);
    }

    [Fact]
    public void Freighter_lands_and_taxis_to_the_cargo_stand_with_no_crossing()
    {
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(Md11F, airline: "UPS"), Airport());

        Assert.Null(leg.Unavailable);
        Assert.Equal("C", leg.Exit!.Exit.TaxiwayName);
        Assert.StartsWith("representative stand C 1", leg.EndpointDescription);
        Assert.Empty(leg.HoldShorts);              // C at 1800 E to C 1 at 2500 E never meets 18/36
        Assert.Equal(new[] { "A" }, leg.Taxiways);
    }

    [Fact]
    public void A380_fits_only_the_large_gate()
    {
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(A388), Airport());
        Assert.StartsWith("representative stand G 1", leg.EndpointDescription);
        Assert.Equal("C", leg.Exit!.Exit.TaxiwayName);
    }

    [Fact]
    public void A_say_intentions_gate_already_called_gate_is_not_called_gate_twice()
    {
        // "SayIntentions assigned gate Gate 5": a letterless gate's own label already says what it is.
        var bundle = AirportWith(Array.Empty<TaxiPath>(), new[] { Spot("", 5, 10, 1000, 180, 150) });
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, gate: new SayIntentionsGateHint("Gate 5", null)), bundle);

        Assert.Null(leg.Unavailable);
        Assert.Equal("SayIntentions assigned Gate 5", leg.EndpointDescription);
    }

    [Fact]
    public void An_OpenStreetMap_leg_says_its_stand_types_are_unknown_once()
    {
        // The OpenStreetMap graph's own caveat ("stand types unknown (OpenStreetMap)") and the stand picker's ("stand
        // types unknown at this airport; the stand may not be a gate") were both briefed. The picker's, which also says
        // what the stand may not be, is kept.
        var osm = Airport(BriefingTier.OpenStreetMap) with
        {
            Note = OsmPlanningGraph.Note,
            Spots = Spots().Select(s => { s.Type = 0; return s; }).ToList(),
        };

        foreach (var leg in new[] { TaxiBriefingPlanner.PlanTaxiOut(Request(B738), osm), TaxiBriefingPlanner.PlanTaxiIn(Request(B738), osm) })
        {
            Assert.Null(leg.Unavailable);
            var note = Assert.Single(leg.Notes, n => n.StartsWith("stand types unknown", StringComparison.Ordinal));
            Assert.Equal("stand types unknown at this airport; the stand may not be a gate", note);
        }
        // With no stand briefed, the graph's own caveat still says it.
        Assert.Contains(OsmPlanningGraph.Note, TaxiBriefingPlanner.PlanTaxiIn(Request(B738, destRunway: "04"), osm).Notes);
    }

    [Fact]
    public void Say_intentions_gate_missing_at_the_airport_is_said_and_a_representative_stand_used()
    {
        var gate = new SayIntentionsGateHint("Terminal 4 Gate B99", null);
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, gate: gate), Airport());

        Assert.Null(leg.Unavailable);
        Assert.StartsWith("representative stand", leg.EndpointDescription);
        Assert.Contains(leg.Notes, n => n.Contains("B99", StringComparison.Ordinal) && n.Contains("not found", StringComparison.Ordinal));
    }

    [Fact]
    public void No_stand_on_the_network_is_reported_but_the_exit_is_still_briefed()
    {
        var bundle = Airport();
        var noStands = bundle with { Spots = new List<ParkingSpot>() };
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738), noStands);

        Assert.NotNull(leg.Unavailable);
        Assert.Contains("no stand", leg.Unavailable, StringComparison.Ordinal);
        Assert.Equal("C", leg.Exit!.Exit.TaxiwayName);
        Assert.NotEmpty(leg.VacatingExits);
    }

    [Fact]
    public void Landing_runway_with_no_exit_is_reported_with_the_stand()
    {
        // Runway 18 has no turn-offs at all.
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, destRunway: "18"), Airport());

        Assert.Equal("no exit taxiway is mapped clear of runway 18 in this scenery", leg.Unavailable);
        Assert.StartsWith("representative stand", leg.EndpointDescription);
        Assert.Empty(leg.VacatingExits);
    }

    [Fact]
    public void A_re_crossing_of_the_runway_just_landed_on_is_briefed_as_a_crossing_hold()
    {
        // Landing 09 and vacating LEFT at C onto the north side, to stand S 1 SOUTH of 09: the way in crosses
        // 09 again at X — with landing traffic behind, the hold short a pilot most needs briefed.
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, airline: "AAL"), AirportWithSouthStand());

        Assert.Null(leg.Unavailable);
        Assert.Equal("C", leg.Exit!.Exit.TaxiwayName);
        Assert.Equal("Left", leg.Exit.Exit.ExitSide);
        Assert.StartsWith("representative stand S 1", leg.EndpointDescription);
        Assert.Equal(new[] { "A", "X", "S" }, leg.Taxiways);
        Assert.Contains(new HoldShortNote("09", "X", BeforeEntering: false), leg.HoldShorts);
        Assert.Contains(leg.HoldShorts, h => (h.Runway == "18" || h.Runway == "36") && h.Taxiway == "A" && !h.BeforeEntering);
        Assert.Equal(2, leg.HoldShorts.Count);
        Assert.DoesNotContain(leg.Notes, n => n.Contains("no hold short point", StringComparison.Ordinal));
    }

    [Fact]
    public void A_crossing_of_the_landing_runway_straight_from_the_exit_is_briefed_although_it_is_the_start_hold()
    {
        // Vacating at C, taxiway Y leads from the vacate node straight back across 09 to stand S 1's side. Nothing
        // lies between that node and the runway, so the pass's only stop is the route's first node: the crossing
        // arrives as the START hold ("runway 27", the nearer end) and must still be briefed.
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, airline: "AAL"), AirportWithSouthStandAndTaxiwayY());

        Assert.Null(leg.Unavailable);
        Assert.Equal("C", leg.Exit!.Exit.TaxiwayName);
        Assert.StartsWith("representative stand S 1", leg.EndpointDescription);
        Assert.Equal(new[] { "Y", "S" }, leg.Taxiways);
        Assert.Equal(new[] { new HoldShortNote("27", "Y", BeforeEntering: false) }, leg.HoldShorts);
        Assert.DoesNotContain(leg.Notes, n => n.Contains("no hold short point", StringComparison.Ordinal));
    }

    [Fact]
    public void A_second_held_crossing_of_one_runway_is_briefed_too_in_route_order()
    {
        // Landing 09, vacating at C, to stand W 1: A crosses 18/36 at 36's end, then V and Z cross it again at 18's.
        // The pass places one stop per held crossing — two stops, one runway — and the pilot needs to hear both.
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, airline: "SWA"), AirportWithStandReachedByZ());

        Assert.Null(leg.Unavailable);
        Assert.Equal("C", leg.Exit!.Exit.TaxiwayName);
        Assert.StartsWith("representative stand W 1", leg.EndpointDescription);
        Assert.Equal(new[] { "A", "V", "Z" }, leg.Taxiways);
        Assert.Equal(new[]
        {
            new HoldShortNote("36", "A", BeforeEntering: false),
            new HoldShortNote("18", "Z", BeforeEntering: false),
        }, leg.HoldShorts);
    }

    [Fact]
    public void Landing_toward_a_stand_across_the_runway_the_exit_on_the_stand_s_side_is_briefed()
    {
        // The OMDB 12L shape (owner decision, 2026-09-26): C turns left and its route to S 1 crosses 09 — the runway
        // just landed on — at X, while CS, 30 ft further, turns right toward S 1. Briefing C sent the pilot back
        // across the landing runway, with traffic behind, for the want of an exit 30 ft away.
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, airline: "AAL"), AirportWithStandSideExit());

        Assert.Null(leg.Unavailable);
        Assert.Equal("CS", leg.Exit!.Exit.TaxiwayName);
        Assert.Equal("Right", leg.Exit.Exit.ExitSide);
        Assert.StartsWith("representative stand S 1", leg.EndpointDescription);
        Assert.Equal(new[] { "CS", "S" }, leg.Taxiways);
        Assert.Empty(leg.HoldShorts);
        Assert.Null(leg.Exit.NextExit);     // no exit further on to the right
    }

    [Fact]
    public void An_exit_whose_route_begins_on_the_other_side_of_the_runway_is_not_briefed()
    {
        // The first Q turns off to the LEFT, but its mapped route begins on the RIGHT, on the second Q's south side
        // (KLAX 25L "A7, LEFT side" with the route starting on H6 north of the runway). Briefed, the block told the
        // pilot to plan to leave to the left and then gave a route that leaves to the right and crosses back over
        // the runway just landed on. It is not a candidate; C, the next exit that leaves where it turns, is.
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, airline: "DAL"), AirportWithOppositeSideNamesake());

        Assert.Null(leg.Unavailable);
        Assert.Equal("C", leg.Exit!.Exit.TaxiwayName);
        Assert.Equal("Left", leg.Exit.Exit.ExitSide);
        Assert.Equal(new[] { "A" }, leg.Taxiways);
        Assert.DoesNotContain(leg.HoldShorts, h => TaxiBriefingPlanner.SameRunway(h.Runway, "09"));
        // C's route begins where C meets A: it leaves the runway on C, so there is nothing to add.
        Assert.DoesNotContain(leg.Notes, n => n.Contains("leaves the runway on taxiway", StringComparison.Ordinal));
    }

    [Fact]
    public void An_exit_whose_route_leaves_the_runway_on_another_taxiway_says_which()
    {
        // The exit is named R1, but the node its route begins at is reached from the runway by K: the block names the
        // exit the pilot will see signed and says which taxiway the mapped route actually takes off the runway.
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, airline: "DAL"), AirportWithMisnamedExit());

        Assert.Null(leg.Unavailable);
        Assert.Equal("R1", leg.Exit!.Exit.TaxiwayName);
        Assert.Equal("Left", leg.Exit.Exit.ExitSide);
        Assert.Equal(new[] { "A" }, leg.Taxiways);
        Assert.Contains("the mapped route leaves the runway on taxiway K", leg.Notes);
    }

    [Fact]
    public void A_route_that_starts_at_the_exit_s_own_junction_names_no_leaving_taxiway()
    {
        // With no way from the junction to judge, the only thing left to read is the route's own first taxiway — the
        // reading TaxiwayLeavingTheRunway exists to avoid. It says nothing rather than guess.
        // (A route from C's junction leaves the runway on C, so an exit named otherwise used to get a "C" note.)
        var g = Airport();
        var rwy = g.Runways.First(r => r.RunwayID == "09");
        var c = g.Graph.GetLandingExits(rwy).First(e => e.TaxiwayName == "C");
        var namedOtherwise = new LandingExit { NodeId = c.NodeId, TaxiwayName = "Q" };

        Assert.Null(TaxiBriefingPlanner.TaxiwayLeavingTheRunway(g.Graph, namedOtherwise, c.NodeId, rwy));
    }

    [Fact]
    public void An_unreachable_choice_names_the_comfortable_exits_set_aside_for_leaving_on_the_other_side()
    {
        // KPHL 17 at 130 kt: E (the last exit that leaves on its own side) is not comfortably reachable, but S is —
        // S was set aside because its mapped route leaves the runway on the other side. The block must not then
        // call the runway short.
        static LandingExit Ex(string name, double thresholdFt, double angle) => new()
        {
            TaxiwayName = name, DistanceFromThresholdFeet = thresholdFt, DistanceFromTouchdownFeet = thresholdFt - 1000,
            ExitAngleDegrees = angle, ExitType = angle <= 50 ? "High-speed" : "Normal", ExitSide = "Right", VacatesRunway = true,
        };
        var e = Ex("E", 4428, 6);       // briefable; 3,428 ft from touchdown, needs 3,565
        var k = Ex("K", 4939, 84);      // set aside; 3,939 ft, needs 4,020 — not reachable either
        var s = Ex("S", 6025, 85);      // set aside; 5,025 ft — reachable
        var vacating = new[] { e, k, s };
        var briefable = new HashSet<LandingExit> { e };

        var unreachable = new ExitChoice(e, null, ComfortablyReachable: false);
        Assert.Equal(new[] { s }, TaxiBriefingPlanner.WithReachableExitsSetAside(unreachable, vacating, briefable, 130.0).ReachableExitsSetAside);

        // A comfortably reachable choice needs no such caveat, and neither does an unreachable one with nothing set aside.
        var x = Ex("X", 5500, 90);      // briefable and reachable
        var reachable = new ExitChoice(x, null, ComfortablyReachable: true);
        Assert.Empty(TaxiBriefingPlanner.WithReachableExitsSetAside(reachable, new[] { e, k, x, s }, new HashSet<LandingExit> { e, x }, 130.0).ReachableExitsSetAside);
        Assert.Empty(TaxiBriefingPlanner.WithReachableExitsSetAside(unreachable, vacating, new HashSet<LandingExit> { e, k, s }, 130.0).ReachableExitsSetAside);
    }

    // ── hold-short bookkeeping ───────────────────────────────────────────────────────────────

    private static TaxiRoute RouteWithHold(string? holdLabel, string? startHold)
    {
        var n1 = RunwayFixture.Node(1, 0, 0);
        var n2 = RunwayFixture.Node(2, 100, 0, holdShortName: holdLabel);
        var n3 = RunwayFixture.Node(3, 200, 0);
        var route = new TaxiRoute { Segments = RunwayFixture.Route(n1, n2, n3), StartHoldRunway = startHold };
        route.Segments[0].TaxiwayName = "A";
        route.Segments[1].TaxiwayName = "A";
        route.Segments[0].IsHoldShortPoint = holdLabel != null;
        return route;
    }

    [Fact]
    public void A_start_hold_for_the_runway_just_landed_on_is_briefed_as_a_crossing_like_any_other_hold()
    {
        // Every briefed exit vacates the landing runway, so the way in starts clear of it, and the pass never holds
        // for LEAVING a runway: a hold naming it — the start hold included, either end — is the route crossing it again.
        // So CollectHoldShorts is not told the landing runway at all; landing on 09, these name 09 and its end 27.
        var notes = new List<string>();
        var noEvents = Array.Empty<TaxiRouteRunwayEvent>();

        Assert.Equal(new[] { new HoldShortNote("27", "A", BeforeEntering: false) },
            TaxiBriefingPlanner.CollectHoldShorts(RouteWithHold(null, startHold: "runway 27"), noEvents, notes));
        Assert.Equal(new[] { new HoldShortNote("09", "A", BeforeEntering: false) },
            TaxiBriefingPlanner.CollectHoldShorts(RouteWithHold(null, startHold: "runway 09"), noEvents, notes));
        Assert.Equal(new[] { new HoldShortNote("27", "A", BeforeEntering: false) },
            TaxiBriefingPlanner.CollectHoldShorts(RouteWithHold("runway 27 at A", startHold: null), noEvents, notes));
    }

    [Fact]
    public void Every_held_stop_is_its_own_hold_note_even_for_a_runway_already_held_short_of()
    {
        // Only the automatic pass flags a stop, one per held crossing, so two stops naming one runway — either
        // end, or the start hold and a later stop — are two crossings, each briefed where it is.
        var notes = new List<string>();
        var noEvents = Array.Empty<TaxiRouteRunwayEvent>();

        var n1 = RunwayFixture.Node(1, 0, 0);
        var n2 = RunwayFixture.Node(2, 100, 0, holdShortName: "runway 36 at A");
        var n3 = RunwayFixture.Node(3, 200, 0);
        var n4 = RunwayFixture.Node(4, 300, 0, holdShortName: "runway 18 at Z");
        var n5 = RunwayFixture.Node(5, 400, 0);
        var twoStops = new TaxiRoute { Segments = RunwayFixture.Route(n1, n2, n3, n4, n5) };
        string[] names = { "A", "A", "Z", "Z" };
        for (int i = 0; i < names.Length; i++) twoStops.Segments[i].TaxiwayName = names[i];
        twoStops.Segments[0].IsHoldShortPoint = true;
        twoStops.Segments[2].IsHoldShortPoint = true;
        Assert.Equal(new[]
        {
            new HoldShortNote("36", "A", BeforeEntering: false),
            new HoldShortNote("18", "Z", BeforeEntering: false),
        }, TaxiBriefingPlanner.CollectHoldShorts(twoStops, noEvents, notes));

        Assert.Equal(new[]
        {
            new HoldShortNote("09", "A", BeforeEntering: false),
            new HoldShortNote("27", "A", BeforeEntering: false),
        }, TaxiBriefingPlanner.CollectHoldShorts(RouteWithHold("runway 27 at A", startHold: "runway 09"), noEvents, notes));
    }

    [Fact]
    public void A_start_hold_is_a_hold_note_on_the_first_taxiway()
    {
        var notes = new List<string>();
        var route = RouteWithHold(null, startHold: "runway 18");
        var hold = Assert.Single(TaxiBriefingPlanner.CollectHoldShorts(route, Array.Empty<TaxiRouteRunwayEvent>(), notes));
        Assert.Equal("18", hold.Runway);
        Assert.Equal("A", hold.Taxiway);
        Assert.False(hold.BeforeEntering);
    }

    [Fact]
    public void An_unheld_crossing_becomes_a_note()
    {
        var notes = new List<string>();
        var events = new[] { new TaxiRouteRunwayEvent { Kind = RunwayEventKind.Crossing, Designator = "06L", Held = false } };
        TaxiBriefingPlanner.CollectHoldShorts(RouteWithHold(null, null), events, notes);
        Assert.Contains(notes, n => n.Contains("06L", StringComparison.Ordinal) && n.Contains("no hold short point", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unheld_re_crossing_of_the_runway_just_landed_on_still_becomes_a_note()
    {
        // Landing on 09 (other end 27): CollectHoldShorts is not told the landing runway, so nothing can silence this.
        var notes = new List<string>();
        var events = new[] { new TaxiRouteRunwayEvent { Kind = RunwayEventKind.Crossing, Designator = "27", Held = false } };
        TaxiBriefingPlanner.CollectHoldShorts(RouteWithHold(null, null), events, notes);
        Assert.Equal(new[] { "no hold short point could be placed for runway 27; cross with care" }, notes);
    }

    [Fact]
    public void Same_runway_is_reciprocal_aware()
    {
        Assert.True(TaxiBriefingPlanner.SameRunway("09", "27"));
        Assert.True(TaxiBriefingPlanner.SameRunway("9", "09"));
        Assert.True(TaxiBriefingPlanner.SameRunway("27L", "09R"));
        Assert.False(TaxiBriefingPlanner.SameRunway("09", "18"));
    }

    [Fact]
    public void Narrow_taxiway_note_only_below_the_code_minimum_and_never_for_unknown_width()
    {
        var route = RouteWithHold(null, null);
        route.Segments[0].TaxiwayName = "K"; route.Segments[0].PathWidth = 49.0;   // 14.9 m
        route.Segments[1].TaxiwayName = "A"; route.Segments[1].PathWidth = 0.0;    // unknown

        var notes = TaxiBriefingPlanner.NarrowTaxiways(route, A388);
        var k = Assert.Single(notes);
        Assert.Equal("K", k.Taxiway);
        Assert.Equal(14.9, k.WidthMetres, 1);
        Assert.Equal(25.0, k.MinimumMetres);

        Assert.Single(TaxiBriefingPlanner.NarrowTaxiways(route, B738));                                   // 14.9 m < code C's 15 m
        Assert.Empty(TaxiBriefingPlanner.NarrowTaxiways(route, AircraftSizeClass.Resolve("ZZZZ", "", null)));   // unknown letter → no minimum
    }

    [Fact]
    public void A_taxiway_that_reads_as_the_minimum_at_the_block_s_precision_is_not_narrower_than_it()
    {
        // 82 ft is 24.99 m: the most common taxiway width in navdata (62 % of fs2024 taxiway rows). It prints as
        // "25.0 m", the code F minimum, so an A380 must never hear "25.0 m in the navdata, below the 25.0 m code F
        // minimum" — on every such taxiway of its route. A width clearly below the minimum is still named.
        var route = RouteWithHold(null, null);
        route.Segments[0].TaxiwayName = "M"; route.Segments[0].PathWidth = 82.0;   // 24.99 m
        route.Segments[1].TaxiwayName = "K"; route.Segments[1].PathWidth = 60.0;   // 18.29 m

        var note = Assert.Single(TaxiBriefingPlanner.NarrowTaxiways(route, A388));
        Assert.Equal("K", note.Taxiway);
        Assert.Equal(18.3, note.WidthMetres, 1);
    }

    // ── departure-runway holds, the current-position endpoint, runway identity ───────────────

    [Fact]
    public void Crossing_the_departure_runway_on_the_way_to_its_threshold_is_a_crossing_and_the_entry_hold_still_follows()
    {
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, airline: "AAL"), AirportWithSouthStand());

        Assert.Null(leg.Unavailable);
        Assert.StartsWith("representative stand S 1", leg.EndpointDescription);
        Assert.Equal(new[] { "S", "X", "A", "E1" }, leg.Taxiways);
        Assert.Equal(new[]
        {
            new HoldShortNote("09", "X", BeforeEntering: false),
            new HoldShortNote("09", "E1", BeforeEntering: true),
        }, leg.HoldShorts);
    }

    [Fact]
    public void A_hold_found_on_the_route_is_never_the_before_entering_hold_even_for_the_departure_runway()
    {
        // CollectHoldShorts is not told the departure runway at all (PlanTaxiOut adds the entry hold itself),
        // so a hold it finds for that runway — here 09, whose other end is 27 — can only ever be a crossing.
        var notes = new List<string>();

        var crossing = RouteWithHold("runway 27 at A", startHold: null);   // 27 is the departure runway's other end
        var crossed = Assert.Single(TaxiBriefingPlanner.CollectHoldShorts(crossing, Array.Empty<TaxiRouteRunwayEvent>(), notes));
        Assert.Equal("27", crossed.Runway);
        Assert.False(crossed.BeforeEntering);

        var startHeld = RouteWithHold(null, startHold: "runway 09");
        var held = Assert.Single(TaxiBriefingPlanner.CollectHoldShorts(startHeld, Array.Empty<TaxiRouteRunwayEvent>(), notes));
        Assert.Equal("09", held.Runway);
        Assert.False(held.BeforeEntering);
    }

    [Fact]
    public void Current_position_names_a_stand_only_when_the_aircraft_is_at_one()
    {
        // A's east end is within 150 m of runway 27's start row, so TaxiGraph.Build labels that node
        // "Runway 27": a runway start, not a stand.
        var nearRunwayStart = new OwnPosition(Lat(100), Lon(3000), OnGround: true);
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, own: nearRunwayStart), Airport());
        Assert.Null(leg.Unavailable);
        Assert.Equal("current position", leg.EndpointDescription);

        var atStand = new OwnPosition(Lat(250), Lon(2500), OnGround: true);
        Assert.StartsWith("current position, stand ", TaxiBriefingPlanner.PlanTaxiOut(Request(B738, own: atStand), Airport()).EndpointDescription);
    }

    [Fact]
    public void Already_at_the_runway_entrance_the_taxi_out_says_so_instead_of_claiming_no_route()
    {
        // Stopped at E1's runway end: the very node a route to 09 would end on. There is nothing to route,
        // and "no taxi route connects current position to runway 09" would tell the pilot something false.
        var atEntrance = new OwnPosition(Lat(0), Lon(50), OnGround: true);
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, own: atEntrance), Airport(BriefingTier.OpenStreetMap));

        Assert.Equal("the aircraft is already at the runway 09 entrance", leg.Unavailable);
        Assert.Equal(BriefingTier.OpenStreetMap, leg.Tier);
        Assert.Equal("09", leg.Runway);
        Assert.Equal("current position", leg.EndpointDescription);
        Assert.Null(leg.Stand);

        // A REPRESENTATIVE stand whose node is that entrance is not the aircraft: never "already at".
        var standAtEntrance = AirportWith(Array.Empty<TaxiPath>(), new[] { Spot("R", 1, 10, 50, 0, 150, "RWY") });
        var fromStand = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, airline: "RWY"), standAtEntrance);
        Assert.StartsWith("representative stand R 1", fromStand.EndpointDescription);
        Assert.NotNull(fromStand.Unavailable);
        Assert.DoesNotContain("already", fromStand.Unavailable, StringComparison.Ordinal);
    }

    [Fact]
    public void Compass_point_runways_are_found_and_matched_to_their_reciprocal()
    {
        var runways = new List<Runway> { Runway("N", 0, 0, 0, 1000, 0), Runway("S", 0, 1000, 0, 0, 180) };

        Assert.Equal("N", TaxiBriefingPlanner.FindRunway(runways, "N")!.RunwayID);
        Assert.Equal("S", TaxiBriefingPlanner.FindRunway(runways, "s")!.RunwayID);
        Assert.Null(TaxiBriefingPlanner.FindRunway(runways, "NE"));
        Assert.True(TaxiBriefingPlanner.SameRunway("N", "S"));
        Assert.False(TaxiBriefingPlanner.SameRunway("N", "E"));
    }

    // ── the brief's consistency points: the tier on every leg, stub handling at a bridged stand ─

    [Fact]
    public void Every_leg_carries_the_graph_s_tier_and_its_note_whether_planned_or_not()
    {
        var osm = Airport(BriefingTier.OpenStreetMap) with { Note = OsmPlanningGraph.Note };
        var noStands = osm with { Spots = new List<ParkingSpot>() };
        var legs = new[]
        {
            TaxiBriefingPlanner.PlanTaxiOut(Request(B738, airline: "DAL"), osm),        // planned
            TaxiBriefingPlanner.PlanTaxiIn(Request(B738, airline: "DAL"), osm),         // planned
            TaxiBriefingPlanner.PlanTaxiOut(Request(B738, originRunway: "04"), osm),    // runway unknown
            TaxiBriefingPlanner.PlanTaxiIn(Request(B738, destRunway: "04"), osm),       // runway unknown
            TaxiBriefingPlanner.PlanTaxiIn(Request(B738, destRunway: "18"), osm),       // no exit
            TaxiBriefingPlanner.PlanTaxiOut(Request(B738), noStands),                   // no stand
            TaxiBriefingPlanner.PlanTaxiIn(Request(B738), noStands),                    // no stand
        };

        Assert.Equal(2, legs.Count(l => l.Unavailable == null));
        Assert.All(legs, l => Assert.Equal(BriefingTier.OpenStreetMap, l.Tier));
        Assert.All(legs, l => Assert.Contains(OsmPlanningGraph.Note, l.Notes));
    }

    [Fact]
    public void A_bridged_stand_is_briefed_from_and_to_its_own_node_but_a_sensed_position_is_never_snapped_onto_it()
    {
        var bundle = AirportWithBridgedStand();

        var outbound = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, airline: "JBU"), bundle);
        Assert.Null(outbound.Unavailable);
        Assert.StartsWith("representative stand T 1", outbound.EndpointDescription);
        Assert.Equal(new[] { "A", "E1" }, outbound.Taxiways);

        var inbound = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, airline: "JBU"), bundle);
        Assert.Null(inbound.Unavailable);
        Assert.StartsWith("representative stand T 1", inbound.EndpointDescription);
        Assert.Equal(new[] { "A" }, inbound.Taxiways);

        // Parked AT T 1: the nearest node that is not a bridge-only stub is A's, 160 m away — beyond the 150 m
        // own-position limit — so the leg falls back to a representative stand instead of starting on the stub.
        var parkedAtT1 = new OwnPosition(Lat(260), Lon(1500), OnGround: true);
        var own = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, airline: "JBU", own: parkedAtT1), bundle);
        Assert.Null(own.Unavailable);
        Assert.StartsWith("representative stand T 1", own.EndpointDescription);
    }

    // ── turn directions ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_taxi_out_gives_the_turn_onto_each_taxiway_after_the_first()
    {
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(Md11F, airline: "UPS"), Airport());
        Assert.Equal(new[] { "A", "E1" }, leg.Taxiways);
        Assert.Equal(new string?[] { null, "left" }, leg.TaxiwayTurns);   // west along A, then south down E1
        Assert.Null(leg.StandTurn);
    }

    [Fact]
    public void The_taxi_in_gives_the_turn_into_the_stand()
    {
        var toG1 = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, gate: new SayIntentionsGateHint("Terminal 1 Gate G1", null)), Airport());
        Assert.Equal(new[] { "A" }, toG1.Taxiways);
        Assert.Equal(new string?[] { null }, toG1.TaxiwayTurns);
        Assert.Equal("right", toG1.StandTurn);    // west along A, then north into G 1

        var toC1 = TaxiBriefingPlanner.PlanTaxiIn(Request(Md11F, airline: "UPS"), Airport());
        Assert.Equal("left", toC1.StandTurn);     // east along A, then north into C 1
    }

    // ── SayIntentions' parking-service gate ─────────────────────────────────────────────────

    [Fact]
    public void A_parking_service_gate_at_the_arrival_airport_is_briefed_as_SayIntentions_gate()
    {
        var gate = new SayIntentionsGateHint("Terminal 1 Gate G1", new GeoPoint(Lat(250), Lon(300)), SayIntentionsGateSource.ParkingService);
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, gate: gate), Airport());
        Assert.Equal("SayIntentions assigned gate G 1", leg.EndpointDescription);
    }

    [Fact]
    public void A_parking_service_gate_not_at_the_arrival_airport_is_not_briefed()
    {
        // SAPI does not say whether getParking means the arrival gate or the current parking; KMEM has a concourse B too.
        var gate = new SayIntentionsGateHint("Terminal 1 Gate G1", new GeoPoint(Lat(500_000), Lon(300)), SayIntentionsGateSource.ParkingService);
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, gate: gate), Airport());

        Assert.StartsWith("representative stand", leg.EndpointDescription);
        Assert.Contains("SayIntentions' parking service named Terminal 1 Gate G1, but its position is not at TEST; using a representative stand instead", leg.Notes);
    }

    [Fact]
    public void A_parking_service_gate_with_no_position_is_briefed_by_name()
    {
        var gate = new SayIntentionsGateHint("Terminal 1 Gate G1", null, SayIntentionsGateSource.ParkingService);
        Assert.Equal("SayIntentions assigned gate G 1", TaxiBriefingPlanner.PlanTaxiIn(Request(B738, gate: gate), Airport()).EndpointDescription);
    }

    [Fact]
    public void A_flight_file_gate_is_never_refused_for_its_position()
    {
        var gate = new SayIntentionsGateHint("Terminal 1 Gate G1", new GeoPoint(Lat(500_000), Lon(300)));
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, gate: gate), Airport());
        Assert.Equal("SayIntentions assigned gate G 1", leg.EndpointDescription);
        Assert.DoesNotContain(leg.Notes, n => n.Contains("parking service", StringComparison.Ordinal));
    }

    // ── runway notes ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_runway_note_leads_the_taxi_out_notes()
    {
        const string note = "runway 09 is the runway SayIntentions assigned; the flight plan names 27";
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738) with { OriginRunwayNote = note }, Airport());
        Assert.Null(leg.Unavailable);
        Assert.Equal(note, leg.Notes[0]);
    }

    [Fact]
    public void The_runway_note_leads_the_taxi_in_notes()
    {
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738) with { DestinationRunwayNote = BriefingRunwayChoice.AgreesNote }, Airport());
        Assert.Null(leg.Unavailable);
        Assert.Equal(BriefingRunwayChoice.AgreesNote, leg.Notes[0]);
    }

    [Fact]
    public void An_unavailable_leg_keeps_its_runway_note()
    {
        const string note = "runway 04 is the runway SayIntentions assigned; the flight plan names 09";
        var outLeg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, originRunway: "04") with { OriginRunwayNote = note }, Airport());
        var inLeg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, destRunway: "04") with { DestinationRunwayNote = note }, Airport());

        Assert.NotNull(outLeg.Unavailable);
        Assert.Contains(note, outLeg.Notes);
        Assert.NotNull(inLeg.Unavailable);
        Assert.Contains(note, inLeg.Notes);
    }
}
