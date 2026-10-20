// tests/MSFSBlindAssist.Tests/TaxiBriefingPlannerTests.cs
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Navigation.Briefing;
using MSFSBlindAssist.Services.SayIntentions;
using static MSFSBlindAssist.Tests.TaxiBriefingFixture;
using MSFSBlindAssist.Settings;

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
    public void The_airport_s_own_answer_decides_where_the_aircraft_is()
    {
        var own = new OwnPosition(Lat(250), Lon(2500), OnGround: true);
        var nowhere = Airport() with { IsAtAirport = (_, _) => false };
        Assert.StartsWith("representative stand", TaxiBriefingPlanner.PlanTaxiOut(Request(B738, own: own), nowhere).EndpointDescription);
    }

    [Fact]
    public void Without_the_airport_s_answer_the_5_km_circle_still_decides()
        => Assert.False(TaxiBriefingPlanner.AtAirport(Airport(), Lat(20000), Lon(0)));

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
    public void A_dead_end_exit_gives_way_to_one_that_routes()
    {
        // The unnamed exit at 1300 E stops 20 m short of A; C, beyond the preference window, routes to every stand.
        var c172 = AircraftSizeClass.Resolve("C172", "Cessna 172", 4);
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(c172), AirportWithUnnamedDeadEndExit());
        Assert.Null(leg.Unavailable);
        Assert.Equal("C", leg.Exit!.Exit.TaxiwayName);
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
    public void When_every_stand_is_an_excluded_kind_both_legs_say_why_not_that_none_connect()
    {
        // KTCM/KSKA/RJTI/EGVG/KNUW: every stand at the airport is a military ramp or a fuel/vehicle stand —
        // "no stand … connects to the taxiway network" would be false; the reason must name the kinds instead.
        var bundle = Airport();
        var onlyExcluded = bundle with
        {
            Spots = new List<ParkingSpot> { Spot("M", 1, 8, 0, 0, 100), Spot("F", 1, 16, 50, 0, 100) },
        };
        string expected = "the only stands at TEST in this scenery are military ramps and fuel stands, " +
                           "and none of them is briefed as a representative stand";

        Assert.Equal(expected, TaxiBriefingPlanner.PlanTaxiOut(Request(B738), onlyExcluded).Unavailable);
        Assert.Equal(expected, TaxiBriefingPlanner.PlanTaxiIn(Request(B738), onlyExcluded).Unavailable);
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
    public void A_same_named_turnoff_far_down_the_runway_does_not_steal_the_exit_s_route()
    {
        // The two Qs are 1,312 ft apart: separate turnoffs, not one arc. The first Q vacates on its own (left) side,
        // so it is a candidate, and as the high-speed exit within 1,500 ft it is preferred to C.
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, airline: "DAL"), AirportWithDistantOppositeSideNamesake());

        Assert.Null(leg.Unavailable);
        Assert.Equal("Q", leg.Exit!.Exit.TaxiwayName);
        Assert.Equal("Left", leg.Exit.Exit.ExitSide);
        Assert.DoesNotContain(leg.HoldShorts, h => TaxiBriefingPlanner.SameRunway(h.Runway, "09"));
    }

    [Fact]
    public void An_exit_whose_route_leaves_the_runway_on_another_taxiway_says_which()
    {
        // The exit is named R1, but the node its route begins at is reached from the runway by K: the block names the
        // exit the pilot will see signed and says which taxiway the mapped route actually takes off the runway.
        // A C172: R1's 10° spur never clears the runway, so its route really turns 90° onto K and R1 is listed as
        // the Normal exit it is (GetLandingExits' CorroborateHighSpeed) — at 1,500 m that is no longer a turn a
        // B738 is offered, while a C172 still takes it.
        var c172 = AircraftSizeClass.Resolve("C172", "Cessna 172", 4);
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(c172, airline: "DAL"), AirportWithMisnamedExit());

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
        Assert.Equal(new[] { s }, TaxiBriefingPlanner.WithReachableExitsSetAside(unreachable, vacating, briefable, 130.0, BriefingExitPicker.JetAimPointFeet).ReachableExitsSetAside);

        // A comfortably reachable choice needs no such caveat, and neither does an unreachable one with nothing set aside.
        var x = Ex("X", 5500, 90);      // briefable and reachable
        var reachable = new ExitChoice(x, null, ComfortablyReachable: true);
        Assert.Empty(TaxiBriefingPlanner.WithReachableExitsSetAside(reachable, new[] { e, k, x, s }, new HashSet<LandingExit> { e, x }, 130.0, BriefingExitPicker.JetAimPointFeet).ReachableExitsSetAside);
        Assert.Empty(TaxiBriefingPlanner.WithReachableExitsSetAside(unreachable, vacating, new HashSet<LandingExit> { e, k, s }, 130.0, BriefingExitPicker.JetAimPointFeet).ReachableExitsSetAside);
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
        var unheldRunways = new List<string>();
        var events = new[] { new TaxiRouteRunwayEvent { Kind = RunwayEventKind.Crossing, Designator = "27", Held = false } };
        TaxiBriefingPlanner.CollectHoldShorts(RouteWithHold(null, null), events, notes, unheldRunways);
        Assert.Equal(new[] { "no hold short point could be placed for runway 27; cross with care" }, notes);
        Assert.Equal(new[] { "27" }, unheldRunways);

        // A held-only call — the ordinary case — leaves the list empty: the renderer's hold line must not read a
        // held crossing as one the automatic pass could not place.
        var heldNotes = new List<string>();
        var heldUnheldRunways = new List<string>();
        var heldEvents = new[] { new TaxiRouteRunwayEvent { Kind = RunwayEventKind.Crossing, Designator = "27", Held = true } };
        TaxiBriefingPlanner.CollectHoldShorts(RouteWithHold(null, null), heldEvents, heldNotes, heldUnheldRunways);
        Assert.Empty(heldUnheldRunways);
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

        // 49 ft is code C's 15 m in whole feet, the unit navdata stores widths in: it IS the minimum, not below it.
        Assert.Empty(TaxiBriefingPlanner.NarrowTaxiways(route, B738));
        Assert.Empty(TaxiBriefingPlanner.NarrowTaxiways(route, AircraftSizeClass.Resolve("ZZZZ", "", null)));   // unknown letter → no minimum
    }

    [Fact]
    public void A_taxiway_of_the_minimum_width_in_whole_feet_is_not_narrower_than_it()
    {
        // 82 ft is 24.99 m: the most common taxiway width in navdata (62 % of fs2024 taxiway rows). Judged in whole
        // feet — the unit navdata stores widths in — 82 ft IS the code F minimum (25 m), so an A380 must never hear
        // "24.99 m in the navdata, below the 25.0 m code F minimum" on every such taxiway of its route. A width
        // clearly below the minimum is still named.
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

        // A REPRESENTATIVE stand whose node is that entrance is not the aircraft: never "already at". Since review T10(2)
        // it is a route with no taxiways (it was "no taxi route connects", which was false), like the taxi-in's exit
        // leading straight onto its stand.
        var standAtEntrance = AirportWith(Array.Empty<TaxiPath>(), new[] { Spot("R", 1, 10, 50, 0, 150, "RWY") });
        var fromStand = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, airline: "RWY"), standAtEntrance);
        Assert.StartsWith("representative stand R 1", fromStand.EndpointDescription);
        Assert.Null(fromStand.Unavailable);
        Assert.Empty(fromStand.Taxiways);
    }

    // ── the departure runway: closed records, runway identity, reaching the runway ──────────

    private static Runway CloneClosed(Runway r) => new()
    {
        Id = r.Id, AirportICAO = r.AirportICAO, RunwayID = r.RunwayID, Heading = r.Heading, HeadingMag = r.HeadingMag,
        StartLat = r.StartLat, StartLon = r.StartLon, EndLat = r.EndLat, EndLon = r.EndLon,
        Length = r.Length, Width = r.Width, Surface = r.Surface, ILSFreq = r.ILSFreq, ILSHeading = r.ILSHeading,
        ThresholdOffset = r.ThresholdOffset, ThresholdElevation = r.ThresholdElevation, GlideslopeAngleDeg = r.GlideslopeAngleDeg,
        IsLanding = r.IsLanding, IsTakeoff = r.IsTakeoff, IsClosed = true,
    };

    [Fact]
    public void A_closed_runway_is_reported_not_planned()
    {
        var bundle = Airport();
        var runways = bundle.Runways.Select(r => r.RunwayID == "09" ? CloneClosed(r) : r).ToList();
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738), bundle with { Runways = runways });
        Assert.Equal("runway 09 is marked closed in this scenery", leg.Unavailable);
    }

    [Fact]
    public void A_closed_arrival_runway_is_reported_not_planned()
    {
        var bundle = Airport();
        var runways = bundle.Runways.Select(r => r.RunwayID == "09" ? CloneClosed(r) : r).ToList();
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738), bundle with { Runways = runways });
        Assert.Equal("runway 09 is marked closed in this scenery", leg.Unavailable);
    }

    [Fact]
    public void An_open_record_wins_over_a_closed_one_of_the_same_runway()
    {
        var open = new Runway { RunwayID = "09" }; var closed = new Runway { RunwayID = "09", IsClosed = true };
        Assert.Same(open, TaxiBriefingPlanner.FindRunway(new[] { closed, open }, "09"));
    }

    [Theory]
    [InlineData("16W", "16", false)] [InlineData("22A", "22", false)] [InlineData("9", "09", true)]
    [InlineData("09L", "9L", true)] [InlineData("Runway 9L", "09L", true)] [InlineData("rwy 27", "27", true)]
    [InlineData("N", "n", true)]
    // Review M-2: the "RW"/"RWY" prefix glued to the designator, as navdata and start rows write it.
    [InlineData("RW09L", "09L", true)] [InlineData("rwy27", "27", true)] [InlineData("RWY 27", "27", true)]
    [InlineData("RW09L", "09R", false)]
    public void A_runway_suffix_is_part_of_its_identity(string a, string b, bool same)
        => Assert.Equal(same, TaxiBriefingPlanner.RunwayIdsMatch(a, b));

    [Fact]
    public void With_no_entrance_at_the_runway_start_the_taxi_out_enters_down_the_runway_and_says_so()
    {
        var bundle = AirportWithEntranceOnlyDownTheRunway();
        // Precondition: the lineup search hands back the plain nearest node, well off the centreline.
        var rwy = TaxiBriefingPlanner.FindRunway(bundle.Runways, "10")!;
        var starts = bundle.Starts.Where(s => s.RunwayName == "10").ToList();
        var target = RunwayLineupTarget.Resolve(bundle.Graph, rwy, starts, Lat(360), Lon(0));
        Assert.NotNull(target.EntryNode);
        Assert.True(Math.Abs(RunwayFrame.For(rwy, rwy.StartLat).SignedCrossTrack(target.EntryNode!.Latitude, target.EntryNode.Longitude))
                    > Services.TaxiGuidanceManager.RUNWAY_REACH_MAX_CROSS_M);

        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, originRunway: "10"), bundle);
        Assert.Null(leg.Unavailable);
        Assert.Equal(new[] { "G", "F" }, leg.Taxiways);
        Assert.Contains(new HoldShortNote("10", "F", BeforeEntering: true), leg.HoldShorts);
        Assert.Contains(leg.Notes, n => n.Contains("backtracking", StringComparison.Ordinal));
    }

    [Fact]
    public void The_backtrack_note_names_the_taxiway_and_the_distance_in_the_pilots_unit()
    {
        // F meets the runway 1,490 fixture metres past the start row; RunwayFrame measures with 111,320 m per degree of
        // latitude against the fixture's 111,132, so it reads 1,492.5 m (4,897 ft).
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, originRunway: "10"), AirportWithEntranceOnlyDownTheRunway());
        Assert.Contains("no taxiway meets runway 10 where a full-length departure begins in this scenery: the route enters it " +
                        "on taxiway F, 1,493 m along, so a full-length departure means backtracking on the runway", leg.Notes);

        var inFeet = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, originRunway: "10") with { Unit = DistanceUnit.Feet },
                                                     AirportWithEntranceOnlyDownTheRunway());
        Assert.Contains(inFeet.Notes, n => n.Contains("on taxiway F, 4,897 ft along,", StringComparison.Ordinal));
    }

    [Fact]
    public void An_entrance_at_the_runway_start_gets_no_backtrack_note()
    {
        // E1 meets 09 40 m past the start row: connector slop, not a backtrack.
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738), Airport());
        Assert.Null(leg.Unavailable);
        Assert.DoesNotContain(leg.Notes, n => n.Contains("backtracking", StringComparison.Ordinal));
    }

    [Fact]
    public void With_no_taxiway_reaching_the_runway_the_taxi_out_is_unavailable()
    {
        var bundle = AirportWithEntranceOnlyDownTheRunway(entranceTouchesRunway: false);
        // Precondition: the plain nearest node is well off the centreline, and no node of the graph touches the runway.
        var rwy = TaxiBriefingPlanner.FindRunway(bundle.Runways, "10")!;
        var frame = RunwayFrame.For(rwy, rwy.StartLat);
        var target = RunwayLineupTarget.Resolve(bundle.Graph, rwy, bundle.Starts.Where(s => s.RunwayName == "10"), Lat(360), Lon(0));
        Assert.True(Math.Abs(frame.SignedCrossTrack(target.EntryNode!.Latitude, target.EntryNode.Longitude))
                    > Services.TaxiGuidanceManager.RUNWAY_REACH_MAX_CROSS_M);
        Assert.All(bundle.Graph.Nodes.Values, n => Assert.True(Math.Abs(frame.SignedCrossTrack(n.Latitude, n.Longitude)) > 50));

        Assert.Equal("no taxiway reaches runway 10 in this scenery",
            TaxiBriefingPlanner.PlanTaxiOut(Request(B738, originRunway: "10"), bundle).Unavailable);
    }

    [Fact]
    public void Parked_at_the_node_nearest_the_lineup_point_is_not_being_at_the_entrance()
    {
        var own = new OwnPosition(Lat(300), Lon(0), OnGround: true);
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, originRunway: "10", own: own), AirportWithEntranceOnlyDownTheRunway());
        Assert.Null(leg.Unavailable);
        Assert.Equal(new[] { "G", "F" }, leg.Taxiways);
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
    public void A_bridged_stand_is_reached_by_the_taxi_in_but_no_route_starts_on_its_stub()
    {
        var bundle = AirportWithBridgedStand();
        var t1 = bundle.Spots.Single(s => s.Name == "T");
        var nearest = bundle.Graph.FindNearestNode(t1.Latitude, t1.Longitude)!;
        Assert.True(bundle.Graph.IsBridgeOnlyStandStub(nearest.NodeId));                                  // precondition: T 1 sits on a stub

        // The taxi-out STARTS at its stand, so T 1's stub is never its start: the nearest node that is not a stub is
        // A's, 160 m away — beyond the 100 m stand reach — so another stand is the representative.
        var outbound = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, airline: "JBU"), bundle);
        Assert.Null(outbound.Unavailable);
        Assert.StartsWith("representative stand", outbound.EndpointDescription);
        Assert.DoesNotContain("T 1", outbound.EndpointDescription);

        var inbound = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, airline: "JBU"), bundle);
        Assert.Null(inbound.Unavailable);
        Assert.StartsWith("representative stand T 1", inbound.EndpointDescription);
        Assert.Equal(new[] { "A" }, inbound.Taxiways);

        // Parked AT T 1: the nearest node that is not a bridge-only stub is A's, 160 m away — beyond the 150 m
        // own-position limit — so the leg falls back to a representative stand instead of starting on the stub.
        var parkedAtT1 = new OwnPosition(Lat(260), Lon(1500), OnGround: true);
        var own = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, airline: "JBU", own: parkedAtT1), bundle);
        Assert.Null(own.Unavailable);
        Assert.StartsWith("representative stand", own.EndpointDescription);
        Assert.DoesNotContain("T 1", own.EndpointDescription);
    }

    // ── the taxi network: never an island ───────────────────────────────────────────────────

    [Fact]
    public void A_stand_on_an_island_is_never_the_representative_stand()
    {
        var bundle = AirportWithIslandStand();
        int network = TaxiBriefingPlanner.NetworkComponentId(bundle.Graph);
        var j1 = bundle.Spots.Single(s => s.Name == "J");
        Assert.NotNull(bundle.Graph.FindNearestNode(j1.Latitude, j1.Longitude));                         // a node is near…
        Assert.Null(TaxiBriefingPlanner.StandNode(bundle.Graph, j1, network, asRouteStart: false));       // …but off the network

        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, airline: "JBU"), bundle);
        Assert.Null(leg.Unavailable);
        Assert.DoesNotContain("J 1", leg.EndpointDescription);
    }

    [Fact]
    public void Parked_on_an_island_the_taxi_out_starts_from_the_network()
    {
        var bundle = AirportWithIslandStand();
        // Precondition: the node nearest the aircraft is on the island, not the network.
        var nearest = bundle.Graph.FindNearestNode(Lat(190), Lon(1550))!;
        Assert.NotEqual(TaxiBriefingPlanner.NetworkComponentId(bundle.Graph), nearest.ComponentId);

        var own = new OwnPosition(Lat(190), Lon(1550), OnGround: true);
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, own: own), bundle);
        Assert.Null(leg.Unavailable);
        Assert.Equal("current position", leg.EndpointDescription);
    }

    [Fact]
    public void A_stand_that_leads_straight_onto_the_runway_is_a_route_with_no_taxiways()
    {
        // Review T10(2): the taxi-in's rule for an exit leading straight onto the stand, on the taxi-out. Stand R 1 sits
        // 22 m from E1's runway node — the node 09's departure begins from — with no lead-in of its own.
        var bundle = AirportWith(Array.Empty<TaxiPath>(), new[] { Spot("R", 1, 10, 60, 20, 150, "RRR") });
        int network = TaxiBriefingPlanner.NetworkComponentId(bundle.Graph);
        var rwy = TaxiBriefingPlanner.FindRunway(bundle.Runways, "09")!;
        var standNode = TaxiBriefingPlanner.StandNode(bundle.Graph, bundle.Spots.Single(s => s.Name == "R"), network, asRouteStart: true)!;
        var target = RunwayLineupTarget.Resolve(bundle.Graph, rwy, bundle.Starts.Where(s => s.RunwayName == "09"),
                                                standNode.Latitude, standNode.Longitude);
        Assert.Equal(target.EntryNode!.NodeId, standNode.NodeId);   // precondition: the stand's node IS the entrance

        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, airline: "RRR"), bundle);
        Assert.Null(leg.Unavailable);
        Assert.StartsWith("representative stand R 1", leg.EndpointDescription);
        Assert.Empty(leg.Taxiways);
        Assert.Equal(0.0, leg.DistanceMetres);
        var hold = Assert.Single(leg.HoldShorts);
        Assert.True(hold.BeforeEntering);
        Assert.Equal("09", hold.Runway);

        string text = TaxiBriefingRenderer.Render(new TaxiBriefing(B738, leg,
            TaxiLegBriefing.UnavailableLeg("TEST", "09", BriefingTier.Navdata, "x")), DistanceUnit.Feet);
        Assert.Contains("  Taxiways: none (the stand leads straight onto the runway)\n", text);
        Assert.DoesNotContain("no taxi route connects", text);
    }

    // ── a second piece of taxi network, reached only across a runway (I-1) ────────────────────

    [Fact]
    public void Parked_on_a_piece_that_meets_the_rest_only_across_a_runway_the_taxi_out_starts_from_the_aircraft()
    {
        var bundle = AirportWithSouthPieceAcrossTheRunway();
        // Preconditions: the aircraft's nearest node is on the south piece, which is not the largest component, and no
        // node of the largest lies within the 150 m own-position reach.
        int network = TaxiBriefingPlanner.NetworkComponentId(bundle.Graph);
        var nearest = bundle.Graph.FindNearestNode(Lat(-200), Lon(2500), excludeBridgeOnlyStandStubs: true)!;
        Assert.NotEqual(network, nearest.ComponentId);
        var onNetwork = bundle.Graph.FindNearestNode(Lat(-200), Lon(2500), requiredComponentId: network)!;
        Assert.True(TaxiGraph.FastDistanceMeters(Lat(-200), Lon(2500), onNetwork.Latitude, onNetwork.Longitude) > 150);

        var own = new OwnPosition(Lat(-200), Lon(2500), OnGround: true);
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, own: own), bundle);

        Assert.Null(leg.Unavailable);
        Assert.StartsWith("current position", leg.EndpointDescription);
        Assert.Null(leg.Stand);
        Assert.Equal(new[] { "P" }, leg.Taxiways);
        Assert.Contains(leg.HoldShorts, h => h.Runway == "09" && h.Taxiway == "P" && h.BeforeEntering);
        Assert.Contains(leg.Notes, n => n.Contains("backtracking", StringComparison.Ordinal));
    }

    [Fact]
    public void SayIntentions_gate_on_a_piece_that_meets_the_rest_only_across_a_runway_is_reached_by_that_piece_s_exit()
    {
        var bundle = AirportWithSouthPieceAcrossTheRunway();
        // Precondition: K 1 is off the largest component, with none of its nodes within the 100 m stand reach.
        int network = TaxiBriefingPlanner.NetworkComponentId(bundle.Graph);
        var k1 = bundle.Spots.Single(s => s.Name == "K");
        Assert.Null(TaxiBriefingPlanner.StandNode(bundle.Graph, k1, network, asRouteStart: false));

        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, gate: new SayIntentionsGateHint("K 1", null)), bundle);

        Assert.Null(leg.Unavailable);
        Assert.Equal(StandChoiceSource.SayIntentions, leg.Stand!.Source);
        Assert.Equal("K", leg.Stand.Spot.Name);
        Assert.Equal("P", leg.Exit!.Exit.TaxiwayName);
        Assert.Equal(new[] { "P" }, leg.Taxiways);
        Assert.DoesNotContain(leg.Notes, n => n.Contains("does not connect", StringComparison.Ordinal));
    }

    [Fact]
    public void An_island_that_reaches_no_runway_is_still_never_a_leg_s_end()
    {
        // The fallback to the largest component stands for a piece that reaches neither end of the leg (KTUL G 19).
        var bundle = AirportWithIslandStand();
        var own = new OwnPosition(Lat(190), Lon(1550), OnGround: true);
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, own: own), bundle);
        Assert.Null(leg.Unavailable);
        Assert.DoesNotContain("GI", leg.Taxiways);
        var inbound = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, gate: new SayIntentionsGateHint("J 1", null)), bundle);
        Assert.NotEqual("J", inbound.Stand?.Spot.Name);
    }

    [Fact]
    public void A_piece_whose_own_full_length_entrance_is_near_the_threshold_departs_from_it_not_by_backtracking()
    {
        // Re-review N-1: the lineup search's plain nearest node is on ANOTHER piece (N1, 8 m away), and the backtrack
        // search skips an entrance under 40 m along — so the south piece was told to backtrack from S2, 1,000 m down.
        var bundle = AirportWithTwoThresholdStubs();
        int network = TaxiBriefingPlanner.NetworkComponentId(bundle.Graph);
        var nearest = bundle.Graph.FindNearestNode(Lat(-160), Lon(300), excludeBridgeOnlyStandStubs: true)!;
        Assert.NotEqual(network, nearest.ComponentId);                                   // precondition: the south piece
        var plain = bundle.Graph.FindNearestNode(Lat(0), Lon(10))!;
        Assert.Equal(network, plain.ComponentId);                                        // precondition: N1 is nearest the lineup point
        var onNetwork = bundle.Graph.FindNearestNode(Lat(-160), Lon(300), requiredComponentId: network)!;
        Assert.True(TaxiGraph.FastDistanceMeters(Lat(-160), Lon(300), onNetwork.Latitude, onNetwork.Longitude) > 150);

        var own = new OwnPosition(Lat(-160), Lon(300), OnGround: true);
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, own: own), bundle);

        Assert.Null(leg.Unavailable);
        Assert.StartsWith("current position", leg.EndpointDescription);
        Assert.Equal(new[] { "S", "S1" }, leg.Taxiways);
        Assert.Contains(leg.HoldShorts, h => h.Runway == "09" && h.Taxiway == "S1" && h.BeforeEntering);
        Assert.DoesNotContain(leg.Notes, n => n.Contains("backtracking", StringComparison.Ordinal));
    }

    [Fact]
    public void An_island_whose_node_nearest_the_lineup_point_is_off_the_runway_pavement_does_not_reach_it()
    {
        // Re-review N-2: RP's end is the node nearest 27's lineup point, 70 m off the centreline — within the 120 m the
        // lineup search returns a plain node for, but no entrance: a run-up pad beside the runway, meeting nothing.
        var bundle = AirportWithRunUpPadIsland();
        int network = TaxiBriefingPlanner.NetworkComponentId(bundle.Graph);
        var plain = bundle.Graph.FindNearestNode(Lat(0), Lon(2990))!;
        Assert.NotEqual(network, plain.ComponentId);                                     // precondition: RP's end
        var rwy = TaxiBriefingPlanner.FindRunway(bundle.Runways, "27")!;
        double offCentre = Math.Abs(RunwayFrame.For(rwy, rwy.StartLat).SignedCrossTrack(plain.Latitude, plain.Longitude));
        Assert.InRange(offCentre, 60, 100);
        var onNetwork = bundle.Graph.FindNearestNode(Lat(-210), Lon(2990), requiredComponentId: network)!;
        Assert.True(TaxiGraph.FastDistanceMeters(Lat(-210), Lon(2990), onNetwork.Latitude, onNetwork.Longitude) > 150);

        var own = new OwnPosition(Lat(-210), Lon(2990), OnGround: true);
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, own: own, originRunway: "27"), bundle);

        Assert.StartsWith("representative stand", leg.EndpointDescription);
        Assert.DoesNotContain("RP", leg.Taxiways);
        Assert.NotEqual("K", leg.Stand?.Spot.Name);
    }

    [Fact]
    public void A_stand_whose_node_is_a_downfield_entrance_still_hears_that_a_full_length_departure_means_backtracking()
    {
        // Re-review N-4: the route with no taxiways returned before the backtrack note was written.
        var bundle = AirportWithEntranceOnlyDownTheRunway(standAtTheEntrance: true);
        int network = TaxiBriefingPlanner.NetworkComponentId(bundle.Graph);
        var standNode = TaxiBriefingPlanner.StandNode(bundle.Graph, bundle.Spots.Single(s => s.Name == "R"), network, asRouteStart: true)!;
        var entrance = bundle.Graph.FindNearestNode(Lat(0), Lon(1500))!;
        Assert.Equal(entrance.NodeId, standNode.NodeId);                                 // precondition: the stand's node is F's runway node

        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, airline: "RRR", originRunway: "10"), bundle);

        Assert.Null(leg.Unavailable);
        Assert.StartsWith("representative stand R 1", leg.EndpointDescription);
        Assert.Empty(leg.Taxiways);
        Assert.Contains(leg.Notes, n => n.Contains("the route enters it, ", StringComparison.Ordinal) &&
                                        n.EndsWith("so a full-length departure means backtracking on the runway", StringComparison.Ordinal));
    }

    [Fact]
    public void Beside_a_runway_on_the_main_network_the_taxi_out_never_starts_from_the_piece_across_it()
    {
        // Re-review N-5: on PN, 30 m north of 09's centreline, the nearest node is P's runway node on the south piece —
        // which reaches the runway — while PN's own node is 100 m away. The aircraft is on the main network.
        var bundle = AirportWithSouthPieceAndParallelBesideTheRunway();
        int network = TaxiBriefingPlanner.NetworkComponentId(bundle.Graph);
        var nearest = bundle.Graph.FindNearestNode(Lat(30), Lon(2200), excludeBridgeOnlyStandStubs: true)!;
        Assert.NotEqual(network, nearest.ComponentId);                                   // precondition: P's node, across the runway
        var onNetwork = bundle.Graph.FindNearestNode(Lat(30), Lon(2200), requiredComponentId: network)!;
        Assert.True(TaxiGraph.FastDistanceMeters(Lat(30), Lon(2200), onNetwork.Latitude, onNetwork.Longitude) <= 150);

        var own = new OwnPosition(Lat(30), Lon(2200), OnGround: true);
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, own: own), bundle);

        Assert.Null(leg.Unavailable);
        Assert.StartsWith("current position", leg.EndpointDescription);
        Assert.Equal("PN", leg.Taxiways[0]);
        Assert.DoesNotContain("P", leg.Taxiways);
    }

    [Fact]
    public void Parked_at_a_stand_on_the_piece_across_the_runway_the_taxi_out_starts_from_that_stand()
    {
        // The other side of N-5: the nearest node is a stand's own node on the south piece, and a main-network node is
        // within reach too — a stand is where the aircraft is, so its piece still wins.
        var bundle = AirportWithSouthPieceAndParallelBesideTheRunway(standBesideTheRunway: true);
        int network = TaxiBriefingPlanner.NetworkComponentId(bundle.Graph);
        var nearest = bundle.Graph.FindNearestNode(Lat(-30), Lon(2230), excludeBridgeOnlyStandStubs: true)!;
        Assert.NotEqual(network, nearest.ComponentId);                                   // precondition: M 1's node
        Assert.Equal(TaxiNodeType.Parking, nearest.Type);
        var onNetwork = bundle.Graph.FindNearestNode(Lat(-30), Lon(2230), requiredComponentId: network)!;
        Assert.True(TaxiGraph.FastDistanceMeters(Lat(-30), Lon(2230), onNetwork.Latitude, onNetwork.Longitude) <= 150);

        var own = new OwnPosition(Lat(-30), Lon(2230), OnGround: true);
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, own: own), bundle);

        Assert.Null(leg.Unavailable);
        Assert.StartsWith("current position", leg.EndpointDescription);
        Assert.Contains("P", leg.Taxiways);
        Assert.DoesNotContain("PN", leg.Taxiways);
    }

    [Fact]
    public void With_no_main_network_node_in_reach_the_taxi_out_never_starts_from_the_piece_across_the_runway()
    {
        // Fix wave 3 (Minor 3a): north of 09, clear of its pavement, the nearest node is P's runway node — its piece lies
        // SOUTH — and no node of the largest piece is within 150 m. Snapped to it, the route began across the runway.
        var bundle = AirportWithSouthPieceAcrossTheRunway();
        int network = TaxiBriefingPlanner.NetworkComponentId(bundle.Graph);
        var nearest = bundle.Graph.FindNearestNode(Lat(40), Lon(2230), excludeBridgeOnlyStandStubs: true)!;
        Assert.NotEqual(network, nearest.ComponentId);                                   // precondition: P's runway node
        Assert.True(TaxiGraph.FastDistanceMeters(Lat(40), Lon(2230), nearest.Latitude, nearest.Longitude) <= 150);
        var onNetwork = bundle.Graph.FindNearestNode(Lat(40), Lon(2230), requiredComponentId: network)!;
        Assert.True(TaxiGraph.FastDistanceMeters(Lat(40), Lon(2230), onNetwork.Latitude, onNetwork.Longitude) > 150);

        var own = new OwnPosition(Lat(40), Lon(2230), OnGround: true);
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, own: own), bundle);

        Assert.StartsWith("representative stand", leg.EndpointDescription);
    }

    [Fact]
    public void On_the_piece_s_own_side_of_the_runway_the_taxi_out_still_starts_from_it()
    {
        // The other side of Minor 3a: on P, south of 09, its runway node is the nearest and its piece lies on this side.
        var bundle = AirportWithSouthPieceAcrossTheRunway();
        int network = TaxiBriefingPlanner.NetworkComponentId(bundle.Graph);
        var nearest = bundle.Graph.FindNearestNode(Lat(-30), Lon(2200), excludeBridgeOnlyStandStubs: true)!;
        Assert.NotEqual(network, nearest.ComponentId);                                   // precondition: P's runway node
        Assert.Equal(0.0, TaxiGraph.FastDistanceMeters(Lat(0), Lon(2200), nearest.Latitude, nearest.Longitude), 1);

        var own = new OwnPosition(Lat(-30), Lon(2200), OnGround: true);
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, own: own), bundle);

        Assert.StartsWith("current position", leg.EndpointDescription);
    }

    [Fact]
    public void Well_away_from_a_stand_on_the_other_piece_the_taxi_out_starts_from_the_main_network()
    {
        // Fix wave 3 (Minor 3b): the stand exemption is for an aircraft AT the stand. Here, on 09's northern half, the
        // nearest node is M 1's stand node 54 m away — beyond OwnPositionStandMaxMetres — and PN is within reach.
        var bundle = AirportWithSouthPieceAndParallelBesideTheRunway(standBesideTheRunway: true);
        int network = TaxiBriefingPlanner.NetworkComponentId(bundle.Graph);
        var nearest = bundle.Graph.FindNearestNode(Lat(15), Lon(2260), excludeBridgeOnlyStandStubs: true)!;
        Assert.NotEqual(network, nearest.ComponentId);                                   // precondition: M 1's node
        Assert.Equal(TaxiNodeType.Parking, nearest.Type);
        Assert.True(TaxiGraph.FastDistanceMeters(Lat(15), Lon(2260), nearest.Latitude, nearest.Longitude)
                    > TaxiBriefingPlanner.OwnPositionStandMaxMetres);
        var onNetwork = bundle.Graph.FindNearestNode(Lat(15), Lon(2260), requiredComponentId: network)!;
        Assert.True(TaxiGraph.FastDistanceMeters(Lat(15), Lon(2260), onNetwork.Latitude, onNetwork.Longitude) <= 150);

        var own = new OwnPosition(Lat(15), Lon(2260), OnGround: true);
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, own: own), bundle);

        Assert.StartsWith("current position", leg.EndpointDescription);
        Assert.Equal("PN", leg.Taxiways[0]);
        Assert.DoesNotContain("P", leg.Taxiways);
    }

    [Fact]
    public void On_the_small_piece_s_side_the_taxi_out_never_starts_from_the_main_network_across_the_runway()
    {
        // Fix wave 4 (I-1): south of 09, on P, the nearest node is the south piece's — its piece lies on this side —
        // while PN, the main network's nearest node, is within reach ACROSS 09. The main network outranked the small
        // piece's node, so the route began across the runway. At north -60 the nearest is P's corner node 40 m away
        // (PN's 135 m); at north -30 it is P's runway node 30 m away (PN's 117 m), which is 09's entrance itself.
        var bundle = AirportWithSouthPieceAndParallelBesideTheRunway();
        int network = TaxiBriefingPlanner.NetworkComponentId(bundle.Graph);
        foreach (double north in new[] { -60.0, -30.0 })
        {
            var nearest = bundle.Graph.FindNearestNode(Lat(north), Lon(2200), excludeBridgeOnlyStandStubs: true)!;
            Assert.NotEqual(network, nearest.ComponentId);                               // precondition: a P node
            var onNetwork = bundle.Graph.FindNearestNode(Lat(north), Lon(2200), requiredComponentId: network)!;
            Assert.True(onNetwork.Latitude > Lat(0));                                    // precondition: across 09
            Assert.True(TaxiGraph.FastDistanceMeters(Lat(north), Lon(2200), onNetwork.Latitude, onNetwork.Longitude) <= 150);
        }

        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, own: new OwnPosition(Lat(-60), Lon(2200), OnGround: true)), bundle);
        Assert.Null(leg.Unavailable);
        Assert.StartsWith("current position", leg.EndpointDescription);
        Assert.Contains("P", leg.Taxiways);
        Assert.DoesNotContain("PN", leg.Taxiways);

        var atEntrance = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, own: new OwnPosition(Lat(-30), Lon(2200), OnGround: true)), bundle);
        Assert.StartsWith("current position", atEntrance.EndpointDescription);
        Assert.Equal("the aircraft is already at the runway 09 entrance", atEntrance.Unavailable);
        Assert.DoesNotContain("PN", atEntrance.Taxiways);
    }

    [Fact]
    public void With_only_the_main_network_in_reach_and_across_the_runway_the_taxi_out_falls_back_to_a_stand()
    {
        // Fix wave 4 (I-1): south of 09 at 1800 E, 40 m off its centreline and clear of its pavement, the nearest node of
        // all is C's runway node — the main network, whose piece lies NORTH — and no node on this side is within reach.
        // Never a start across the runway: no start node, so a representative stand.
        var bundle = Airport();
        int network = TaxiBriefingPlanner.NetworkComponentId(bundle.Graph);
        var nearest = bundle.Graph.FindNearestNode(Lat(-40), Lon(1800), excludeBridgeOnlyStandStubs: true)!;
        Assert.Equal(network, nearest.ComponentId);                                      // precondition: C's runway node
        Assert.Equal(0.0, TaxiGraph.FastDistanceMeters(Lat(0), Lon(1800), nearest.Latitude, nearest.Longitude), 1);
        Assert.DoesNotContain(bundle.Graph.Nodes.Values, n => n.Latitude < Lat(-1) &&
            TaxiGraph.FastDistanceMeters(Lat(-40), Lon(1800), n.Latitude, n.Longitude) <= 150);   // nothing on this side

        var own = new OwnPosition(Lat(-40), Lon(1800), OnGround: true);
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, own: own), bundle);

        Assert.StartsWith("representative stand", leg.EndpointDescription);
    }

    [Fact]
    public void A_stand_whose_only_exit_is_not_comfortable_is_briefed_by_that_exit_not_by_one_with_no_route()
    {
        // Re-review N-6: the comfortable exits (C, D) lead north and have no route to K 1; the picker fell back to the
        // first of them, and the leg said "no taxi route connects exit C to K 1". P, the one exit that routes, is briefed
        // — flagged, with the reason the pilot needs.
        var bundle = AirportWithEarlySouthPiece();
        var rwy = TaxiBriefingPlanner.FindRunway(bundle.Runways, "09")!;
        var exits = bundle.Graph.GetLandingExits(rwy);
        double aim = BriefingExitPicker.AimPointFeet(rwy);
        var p = exits.Single(e => e.TaxiwayName == "P");
        Assert.False(BriefingExitPicker.IsComfortablyReachable(p, B738.TouchdownSpeedKts, aim));     // precondition
        Assert.Contains(exits, e => BriefingExitPicker.IsComfortablyReachable(e, B738.TouchdownSpeedKts, aim));

        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, airline: "SWA"), bundle);

        Assert.Null(leg.Unavailable);
        Assert.Equal("K", leg.Stand!.Spot.Name);
        Assert.Equal("P", leg.Exit!.Exit.TaxiwayName);
        Assert.False(leg.Exit.ComfortablyReachable);
        Assert.Contains("P", leg.Taxiways);
        string text = TaxiBriefingRenderer.Render(new TaxiBriefing(B738,
            TaxiLegBriefing.UnavailableLeg("TEST", "09", BriefingTier.Navdata, "x"), leg), DistanceUnit.Feet);
        string kt = B738.TouchdownSpeedKts.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        // Fix wave 3: P lies BEHIND the comfortable exits on a runway long enough to stop on — a backtrack, said so only then.
        Assert.True(leg.Exit.BriefedExitBehindReachable);
        Assert.Equal(UnreachableRunway.LongEnoughToBacktrack, leg.Exit.RunwayLength);
        Assert.Contains($" No exit comfortably reachable at {kt} kt has a mapped route to the stand: expect to stop on the " +
                        "runway and backtrack to the briefed exit, the last one before them with a mapped route.", text);
    }

    [Fact]
    public void An_exit_that_leads_straight_onto_the_stand_is_a_route_with_no_taxiways()
    {
        var bundle = AirportWithExitStraightOntoStand();
        // Precondition: exit Z's route begins on the very node stand Z 1 is reached at.
        int network = TaxiBriefingPlanner.NetworkComponentId(bundle.Graph);
        var rwy = TaxiBriefingPlanner.FindRunway(bundle.Runways, "09")!;
        var exits = bundle.Graph.GetLandingExits(rwy);
        LandingExitVacateScreen.Mark(bundle.Graph, exits, rwy);
        var starts = TaxiBriefingPlanner.BriefableExitRouteStarts(bundle.Graph, exits, rwy);
        var z = starts.Keys.Single(e => e.TaxiwayName == "Z");
        var z1 = bundle.Spots.Single(s => s.Name == "Z");
        Assert.Equal(TaxiBriefingPlanner.StandNode(bundle.Graph, z1, network, asRouteStart: false)!.NodeId, starts[z]);

        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, gate: new SayIntentionsGateHint("Z 1", null)), bundle);
        Assert.Null(leg.Unavailable);
        Assert.Equal("Z", leg.Exit!.Exit.TaxiwayName);
        Assert.Empty(leg.Taxiways);
        Assert.Equal(0.0, leg.DistanceMetres);
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
    public void The_taxi_in_gives_the_turn_into_a_stand_at_the_end_of_a_short_lead_in()
    {
        // A 70 m lead-in north off A: west along A from exit C, then right into the stand.
        var bundle = AirportWith(new[] { LeadIn(1000, 100, 1000, 170) }, new[] { Spot("", 5, 10, 1000, 170, 150) });
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, gate: new SayIntentionsGateHint("Gate 5", null)), bundle);
        Assert.Equal("SayIntentions assigned Gate 5", leg.EndpointDescription);
        Assert.Equal("right", leg.StandTurn);
    }

    [Fact]
    public void No_turn_into_the_stand_is_given_beyond_a_lead_in()
    {
        // G 1's and C 1's lead-ins in the TEST fixture are 150 m, longer than a stand lead-in (100 m).
        var toG1 = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, gate: new SayIntentionsGateHint("Terminal 1 Gate G1", null)), Airport());
        Assert.Equal(new string?[] { null }, toG1.TaxiwayTurns);
        Assert.Null(toG1.StandTurn);

        var toC1 = TaxiBriefingPlanner.PlanTaxiIn(Request(Md11F, airline: "UPS"), Airport());
        Assert.Null(toC1.Unavailable);   // otherwise a null StandTurn below would pass vacuously
        Assert.Null(toC1.StandTurn);
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
    public void A_parking_service_gate_beyond_5_km_is_briefed_when_the_airport_says_it_is_there()
    {
        var gate = new SayIntentionsGateHint("G 1", new GeoPoint(Lat(6000), Lon(300)), SayIntentionsGateSource.ParkingService);
        var here = Airport() with { IsAtAirport = (_, _) => true };
        Assert.Equal(StandChoiceSource.SayIntentions, TaxiBriefingPlanner.PlanTaxiIn(Request(B738, gate: gate), here).Stand!.Source);
    }

    [Fact]
    public void A_parking_service_gate_with_no_position_is_found_by_name_in_the_arrival_scenery()
    {
        // Live KMEM→KATL 2026-09-26: getParking answered "B3" with no position and the flight file said "Gate B3" nine
        // seconds later, with the aircraft at KMEM Gate 17 — the parking service meant the ARRIVAL gate. It is looked up
        // by name in the arrival airport's scenery rather than replaced by a representative stand (owner, 2026-09-26).
        var gate = new SayIntentionsGateHint("Terminal 1 Gate G1", null, SayIntentionsGateSource.ParkingService);
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, gate: gate), Airport());

        Assert.Equal("SayIntentions assigned gate G 1", leg.EndpointDescription);
        Assert.Contains("SayIntentions' parking service gave no position for Terminal 1 Gate G1, so it was matched by name in this scenery", leg.Notes);
    }

    [Fact]
    public void A_parking_service_gate_with_no_position_and_no_matching_name_falls_back_to_a_representative_stand()
    {
        var gate = new SayIntentionsGateHint("Gate Q99", null, SayIntentionsGateSource.ParkingService);
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, gate: gate), Airport());

        Assert.StartsWith("representative stand", leg.EndpointDescription);
        Assert.Contains(leg.Notes, n => n.Contains("Gate Q99", StringComparison.Ordinal) &&
                                        n.Contains("was not found at this airport", StringComparison.Ordinal));
        Assert.DoesNotContain(leg.Notes, n => n.Contains("matched by name", StringComparison.Ordinal));
    }

    [Fact]
    public void The_stand_notes_follow_the_pilot_s_distance_setting()
    {
        var gate = new SayIntentionsGateHint("Terminal 1 Gate G1", new GeoPoint(Lat(500_000), Lon(300)));
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, gate: gate) with { Unit = DistanceUnit.Feet }, Airport());
        Assert.Contains(leg.Notes, n => n.StartsWith("SayIntentions' position is ", StringComparison.Ordinal) &&
                                        n.EndsWith(" ft from this stand", StringComparison.Ordinal));
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
    public void The_runway_note_comes_before_the_graph_s_own_note()
    {
        const string note = "runway 09 is the runway SayIntentions assigned; the flight plan names 27";
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738) with { OriginRunwayNote = note }, Airport() with { Note = "graph caveat" });
        Assert.Equal(note, leg.Notes[0]);
        Assert.Equal("graph caveat", leg.Notes[1]);
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

    // ── the airport's taxiway names (owner, 2026-09-26) ──────────────────────────────────────

    [Fact]
    public void A_planned_leg_carries_every_taxiway_name_of_its_airport_sorted()
    {
        var bundle = Airport();
        Assert.Equal(new[] { "A", "B", "C", "D", "E1" }, TaxiBriefingPlanner.PlanTaxiOut(Request(B738), bundle).AirportTaxiways);
        Assert.Equal(new[] { "A", "B", "C", "D", "E1" }, TaxiBriefingPlanner.PlanTaxiIn(Request(B738), bundle).AirportTaxiways);
    }

    [Fact]
    public void An_unavailable_leg_planned_on_a_graph_still_carries_the_names()
    {
        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, originRunway: "04"), Airport());
        Assert.NotNull(leg.Unavailable);
        Assert.Equal(new[] { "A", "B", "C", "D", "E1" }, leg.AirportTaxiways);
    }

    [Fact]
    public async Task A_leg_never_planned_on_a_graph_has_no_names()
    {
        var b = await TaxiBriefingPlanner.PlanAsync(Request(B738), provider: null, gateSource: null, TimeSpan.FromSeconds(5));
        Assert.Empty(b.TaxiOut.AirportTaxiways);
        Assert.Empty(b.TaxiIn.AirportTaxiways);
    }

    // ── "short" only when true: the downfield rescue and backtrack runways ─────────────────────

    [Fact]
    public void A_long_runway_whose_exits_are_all_behind_the_touchdown_is_a_backtrack_not_short()
    {
        var bundle = AirportWithOnlyEarlyExits(2500);
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, destRunway: "05"), bundle);
        Assert.False(leg.Exit!.ComfortablyReachable);
        Assert.Equal(UnreachableRunway.LongEnoughToBacktrack, leg.Exit.RunwayLength);
    }

    [Fact]
    public void A_runway_too_short_to_stop_on_comfortably_is_short()
    {
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, destRunway: "05"), AirportWithOnlyEarlyExits(1200));
        Assert.Equal(UnreachableRunway.Short, leg.Exit!.RunwayLength);
    }

    [Fact]
    public void A_Cessna_on_a_1500_ft_strip_is_never_briefed_that_the_runway_is_short()
    {
        // Review I-2: the planner's "short" verdict is the jet re-plan's rule (≈1,250 ft of lead plus a 492 ft aim at
        // 70 kt), so on a 457 m (1,500 ft) strip a C172 gets it — and the block must not say so.
        var c172 = AircraftSizeClass.Resolve("C172", "Cessna 172", 4);
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(c172, destRunway: "05"), AirportWithOnlyEarlyExits(457.2));
        Assert.False(leg.Exit!.ComfortablyReachable);                  // preconditions: nothing comfortable,
        Assert.Equal(UnreachableRunway.Short, leg.Exit.RunwayLength);  // and the jet rule's verdict is "short"

        var taxiOut = TaxiLegBriefing.UnavailableLeg("TEST", "05", BriefingTier.Navdata, "x");
        string text = TaxiBriefingRenderer.Render(new TaxiBriefing(c172, taxiOut, leg), DistanceUnit.Feet);
        Assert.Contains("No exit is comfortably reachable at 70 kt; the briefed exit is the last one with a mapped route.", text);
        Assert.DoesNotContain("short for this aircraft", text);
    }

    [Fact]
    public void Before_calling_no_exit_comfortable_the_graph_is_asked_for_exits_the_list_left_out()
    {
        var bundle = AirportWithUnmarkedExit(600);   // C at 1,969 ft: too early for a 737
        var rwy = TaxiBriefingPlanner.FindRunway(bundle.Runways, "09")!;
        Assert.DoesNotContain(bundle.Graph.GetLandingExits(rwy), e => e.TaxiwayName == "U");   // the list is lossy here

        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738), bundle);
        Assert.Equal("U", leg.Exit!.Exit.TaxiwayName);
        Assert.True(leg.Exit.ComfortablyReachable);
    }

    [Fact]
    public void Before_saying_no_exit_follows_the_briefed_one_the_graph_is_asked_too()
    {
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738), AirportWithUnmarkedExit(1800));   // C at 5,906 ft
        Assert.Equal("C", leg.Exit!.Exit.TaxiwayName);
        Assert.Equal("U", leg.Exit.NextExit!.TaxiwayName);
    }

    [Fact]
    public void A_rescue_for_the_next_exit_never_replaces_a_comfortable_briefed_exit()
    {
        // Review M-1: C (6,562 ft) is comfortable with no next exit in the list, so the graph is asked from C on. It finds
        // U, a HIGH-SPEED exit 1,312 ft further — inside the preference window, where a fresh pick prefers it to C. The
        // rescue answers only "what follows C"; it never swaps the exit the pilot is briefed to take.
        var bundle = AirportWithUnmarkedExit(2000, unmarkedExitAngled: true);
        var rwy = TaxiBriefingPlanner.FindRunway(bundle.Runways, "09")!;
        var listed = bundle.Graph.GetLandingExits(rwy);
        Assert.DoesNotContain(listed, e => e.TaxiwayName == "U");                                   // preconditions: U is
        var c = listed.Single(e => e.TaxiwayName == "C");                                            // left out of the list,
        var rescued = bundle.Graph.FindDownfieldExits(rwy, c.DistanceFromThresholdFeet);
        var u = Assert.Single(rescued, e => e.TaxiwayName == "U");                                   // the graph finds it,
        Assert.Equal("High-speed", u.ExitType);                                                      // high-speed,
        Assert.InRange(u.DistanceFromThresholdFeet - c.DistanceFromThresholdFeet,
                       BriefingExitPicker.NextExitMinSeparationFeet, BriefingExitPicker.PreferenceWindowFeet); // in the window

        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738), bundle);
        Assert.Equal("C", leg.Exit!.Exit.TaxiwayName);
        Assert.True(leg.Exit.ComfortablyReachable);
        Assert.Equal("U", leg.Exit.NextExit!.TaxiwayName);
    }

    // ── notes and holds that say what is true ────────────────────────────────────────────────

    [Fact]
    public void A_refused_parking_service_gate_says_representative_only_when_one_is_used()
    {
        var far = new SayIntentionsGateHint("B3", new GeoPoint(Lat(20000), Lon(0)), SayIntentionsGateSource.ParkingService);
        var noStands = Airport() with { Spots = new List<ParkingSpot>() };
        var leg = TaxiBriefingPlanner.PlanTaxiIn(Request(B738, gate: far), noStands);
        Assert.Null(leg.Stand);   // precondition: no stand to stand in
        Assert.Contains("SayIntentions' parking service named B3, but its position is not at TEST", leg.Notes);
        Assert.DoesNotContain(leg.Notes, n => n.Contains("using a representative stand", StringComparison.Ordinal));
        Assert.Contains("SayIntentions' parking service named B3, but its position is not at TEST; using a representative stand instead",
            TaxiBriefingPlanner.PlanTaxiIn(Request(B738, gate: far), Airport()).Notes);
    }

    [Fact]
    public void The_no_exit_reason_names_what_is_really_missing()
    {
        Assert.Equal("this scenery names none of TEST's taxiways, so no exit off runway 09 can be named",
            TaxiBriefingPlanner.NoExitReason("TEST", "09", vacatingCount: 0, briefableCount: 0, airportNamesTaxiways: false));
        Assert.Equal("no exit taxiway is mapped clear of runway 09 in this scenery",
            TaxiBriefingPlanner.NoExitReason("TEST", "09", 0, 0, true));
        Assert.Equal("every exit clear of runway 09 in this scenery has its mapped route leave the runway on the other side from the one it turns toward, so none is briefed",
            TaxiBriefingPlanner.NoExitReason("TEST", "09", 3, 0, true));
    }

    [Fact]
    public void One_unheld_note_per_runway_pavement()
    {
        var notes = new List<string>(); var unheld = new List<string>();
        var events = new[]
        {
            new TaxiRouteRunwayEvent { Kind = RunwayEventKind.Crossing, Designator = "10L", Held = false },
            new TaxiRouteRunwayEvent { Kind = RunwayEventKind.Crossing, Designator = "28R", Held = false },
            new TaxiRouteRunwayEvent { Kind = RunwayEventKind.Crossing, Designator = "18", Held = false },
        };
        TaxiBriefingPlanner.CollectHoldShorts(RouteWithHold(null, null), events, notes, unheld);
        Assert.Equal(new[] { "10L", "18" }, unheld);
        Assert.Equal(2, notes.Count);
    }

    [Fact]
    public void Current_position_names_a_stand_only_within_forty_metres_of_it()
    {
        // C 1's node is at (2500, 250); 100 m east of it the nearest node is still that one, within the 150 m start reach.
        var beside = new OwnPosition(Lat(250), Lon(2600), OnGround: true);
        var bundle = Airport();
        var nearest = bundle.Graph.FindNearestNode(beside.Lat, beside.Lon,
            requiredComponentId: TaxiBriefingPlanner.NetworkComponentId(bundle.Graph), excludeBridgeOnlyStandStubs: true)!;
        Assert.Equal("C 1", nearest.ParkingName);   // precondition: the start node is the stand's own, 100 m away
        Assert.InRange(TaxiGraph.FastDistanceMeters(beside.Lat, beside.Lon, nearest.Latitude, nearest.Longitude),
            TaxiBriefingPlanner.OwnPositionStandMaxMetres + 1, TaxiBriefingPlanner.OwnPositionMaxNodeDistanceMetres);
        Assert.Equal("current position", TaxiBriefingPlanner.PlanTaxiOut(Request(B738, own: beside), bundle).EndpointDescription);
    }

    [Fact]
    public void Narrow_is_judged_in_whole_feet()
    {
        var route = RouteWithHold(null, null);
        route.Segments[0].TaxiwayName = "K"; route.Segments[0].PathWidth = 49.0;   // code C's 15 m in whole feet
        route.Segments[1].TaxiwayName = "L"; route.Segments[1].PathWidth = 48.0;
        var note = Assert.Single(TaxiBriefingPlanner.NarrowTaxiways(route, B738));
        Assert.Equal("L", note.Taxiway);
    }

    [Fact]
    public void A_taxi_out_from_the_aircraft_s_position_never_drops_the_crossing_it_starts_on()
    {
        var own = new OwnPosition(Lat(28), Lon(1000), OnGround: true);
        var bundle = AirportWithSouthRunway();
        // Precondition: the route's first node — the aircraft's nearest network node — is X's node on 09's centreline.
        var first = bundle.Graph.FindNearestNode(own.Lat, own.Lon,
            requiredComponentId: TaxiBriefingPlanner.NetworkComponentId(bundle.Graph), excludeBridgeOnlyStandStubs: true)!;
        Assert.Equal(Lat(0), first.Latitude, 6);
        Assert.Equal(Lon(1000), first.Longitude, 6);

        var leg = TaxiBriefingPlanner.PlanTaxiOut(Request(B738, own: own, originRunway: "07"), bundle);
        Assert.Null(leg.Unavailable);
        Assert.Equal("X", leg.Taxiways[0]);
        Assert.True(leg.HoldShorts.Any(h => h.Runway is "09" or "27") || leg.UnheldRunways.Any(r => r is "09" or "27"),
            "the crossing of 09/27 must be briefed, held or not");
    }
}
