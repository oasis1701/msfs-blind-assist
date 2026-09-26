// tests/MSFSBlindAssist.Tests/TaxiBriefingPlannerTests.cs
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Navigation.Briefing;
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
    public void Only_a_start_hold_for_the_runway_just_landed_on_is_discarded_including_its_reciprocal()
    {
        var notes = new List<string>();
        var noEvents = Array.Empty<TaxiRouteRunwayEvent>();

        // A START hold naming the runway just landed on (either end) sits where the aircraft has just vacated it.
        Assert.Empty(TaxiBriefingPlanner.CollectHoldShorts(RouteWithHold(null, startHold: "runway 27"), noEvents, landedRunway: "09", notes));
        Assert.Empty(TaxiBriefingPlanner.CollectHoldShorts(RouteWithHold(null, startHold: "runway 09"), noEvents, landedRunway: "09", notes));
        Assert.Single(TaxiBriefingPlanner.CollectHoldShorts(RouteWithHold(null, startHold: "runway 27"), noEvents, landedRunway: "18", notes));

        // A hold further along the route naming it is the route crossing that runway AGAIN: briefed, as a crossing.
        var reCrossing = Assert.Single(TaxiBriefingPlanner.CollectHoldShorts(RouteWithHold("runway 27 at A", startHold: null), noEvents, landedRunway: "09", notes));
        Assert.Equal(new HoldShortNote("27", "A", BeforeEntering: false), reCrossing);
    }

    [Fact]
    public void A_start_hold_is_a_hold_note_on_the_first_taxiway()
    {
        var notes = new List<string>();
        var route = RouteWithHold(null, startHold: "runway 18");
        var hold = Assert.Single(TaxiBriefingPlanner.CollectHoldShorts(route, Array.Empty<TaxiRouteRunwayEvent>(), landedRunway: null, notes));
        Assert.Equal("18", hold.Runway);
        Assert.Equal("A", hold.Taxiway);
        Assert.False(hold.BeforeEntering);
    }

    [Fact]
    public void An_unheld_crossing_becomes_a_note()
    {
        var notes = new List<string>();
        var events = new[] { new TaxiRouteRunwayEvent { Kind = RunwayEventKind.Crossing, Designator = "06L", Held = false } };
        TaxiBriefingPlanner.CollectHoldShorts(RouteWithHold(null, null), events, landedRunway: null, notes);
        Assert.Contains(notes, n => n.Contains("06L", StringComparison.Ordinal) && n.Contains("no hold short point", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unheld_re_crossing_of_the_runway_just_landed_on_still_becomes_a_note()
    {
        var notes = new List<string>();
        var events = new[] { new TaxiRouteRunwayEvent { Kind = RunwayEventKind.Crossing, Designator = "27", Held = false } };
        TaxiBriefingPlanner.CollectHoldShorts(RouteWithHold(null, null), events, landedRunway: "09", notes);
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
        var crossed = Assert.Single(TaxiBriefingPlanner.CollectHoldShorts(crossing, Array.Empty<TaxiRouteRunwayEvent>(), landedRunway: null, notes));
        Assert.Equal("27", crossed.Runway);
        Assert.False(crossed.BeforeEntering);

        var startHeld = RouteWithHold(null, startHold: "runway 09");
        var held = Assert.Single(TaxiBriefingPlanner.CollectHoldShorts(startHeld, Array.Empty<TaxiRouteRunwayEvent>(), landedRunway: null, notes));
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
}
