using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Navigation.Briefing;
using MSFSBlindAssist.Services.SayIntentions;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The TAXI PLANNING block the route briefing hands the AI: only what the real-world taxi flow needs — the airports,
/// the runways (SayIntentions' when it assigned them), the stand the aircraft is parked at, SayIntentions' arrival gate
/// and the aircraft. No route is computed from the scenery any more (owner, 2026-09-26: "just the typical real world
/// flow"), which also takes the graph builds and the online-data wait out of the briefing's time.
/// </summary>
public class TaxiPlanningBlockTests
{
    private static readonly AircraftProfile B738 = AircraftSizeClass.Resolve("B738", "Boeing 737-800", 189);
    private static readonly Airport Kmem = new() { ICAO = "KMEM", Latitude = 35.0424, Longitude = -89.9767 };
    private static readonly Airport Katl = new() { ICAO = "KATL", Latitude = 33.6367, Longitude = -84.4281 };

    private static ParkingSpot Gate(string name, int number, double lat, double lon) =>
        new() { Name = name, Number = number, Type = 11, Latitude = lat, Longitude = lon, Suffix = "", AirlineCodes = "" };

    private static TaxiPlanningInputs Inputs(string? stand, ArrivalGateChoice gate, string? airline = "DAL",
                                             BriefingRunway? dep = null, BriefingRunway? arr = null) =>
        new(B738, airline,
            "KMEM", dep ?? new BriefingRunway("36L", "runway 36L is the runway SayIntentions assigned; the flight plan names 18R"),
            stand,
            "KATL", arr ?? new BriefingRunway("26R", null),
            gate);

    [Fact]
    public void Block_names_runways_stand_gate_and_aircraft_and_no_computed_route()
    {
        string block = TaxiPlanningBlock.Render(
            Inputs("Gate 17", new ArrivalGateChoice("B3", null)), DistanceUnit.Feet);

        Assert.Equal(
            "TAXI PLANNING (no taxi route is computed; these are the facts for the typical real-world taxi flow)\n" +
            "Distance unit: feet (the pilot's setting); give every taxi distance in feet\n" +
            "Aircraft: Boeing 737-800 (SimBrief type B738), size class C, wingspan 117 ft, passenger\n" +
            "Airline: DAL\n" +
            "TAXI OUT at KMEM, runway 36L\n" +
            "  Start: the aircraft is parked at stand Gate 17\n" +
            "  Note: runway 36L is the runway SayIntentions assigned; the flight plan names 18R\n" +
            "TAXI IN at KATL, landing runway 26R\n" +
            "  Gate: B3 (assigned by SayIntentions)",
            block);
    }

    [Fact]
    public void Without_a_stand_gate_airline_or_runway_the_block_says_so()
    {
        string block = TaxiPlanningBlock.Render(
            Inputs(null, new ArrivalGateChoice(null, null), airline: null,
                   dep: new BriefingRunway("", null), arr: new BriefingRunway("", null)),
            DistanceUnit.Metres);

        Assert.Equal(
            "TAXI PLANNING (no taxi route is computed; these are the facts for the typical real-world taxi flow)\n" +
            "Distance unit: metres (the pilot's setting); give every taxi distance in metres\n" +
            "Aircraft: Boeing 737-800 (SimBrief type B738), size class C, wingspan 35.8 m, passenger\n" +
            "TAXI OUT at KMEM, runway not given in the flight plan\n" +
            "  Start: no stand known\n" +
            "TAXI IN at KATL, landing runway not given in the flight plan\n" +
            "  Gate: none assigned",
            block);
    }

    [Fact]
    public void A_refused_gate_keeps_its_note()
    {
        string block = TaxiPlanningBlock.Render(
            Inputs(null, new ArrivalGateChoice(null, "SayIntentions' parking service named B3, but its position is not at KATL, so it is not used")),
            DistanceUnit.Feet);

        Assert.EndsWith(
            "  Gate: none assigned\n" +
            "  Note: SayIntentions' parking service named B3, but its position is not at KATL, so it is not used",
            block);
    }

    // ── Where the taxi out starts ──────────────────────────────────────────────────────────

    [Fact]
    public void Parked_at_the_origin_names_the_nearest_stand()
    {
        var spots = new[]
        {
            Gate("", 16, 35.0500, -89.9800),
            Gate("", 17, 35.0501, -89.9801),
        };
        var own = new OwnPosition(35.05011, -89.98012, OnGround: true);

        Assert.Equal("Gate 17", TaxiPlanningBlock.ResolveDepartureStand(own, Kmem, () => spots));
    }

    [Fact]
    public void Not_at_the_origin_names_no_stand_and_never_reads_the_stands()
    {
        var own = new OwnPosition(Katl.Latitude, Katl.Longitude, OnGround: true);
        Assert.Null(TaxiPlanningBlock.ResolveDepartureStand(own, Kmem,
            () => throw new InvalidOperationException("stands must not be read away from the origin")));
    }

    [Fact]
    public void Airborne_or_unknown_position_names_no_stand()
    {
        var spots = new[] { Gate("", 17, 35.0501, -89.9801) };
        Assert.Null(TaxiPlanningBlock.ResolveDepartureStand(new OwnPosition(35.0501, -89.9801, OnGround: false), Kmem, () => spots));
        Assert.Null(TaxiPlanningBlock.ResolveDepartureStand(null, Kmem, () => spots));
        Assert.Null(TaxiPlanningBlock.ResolveDepartureStand(new OwnPosition(35.0501, -89.9801, true), null, () => spots));
    }

    [Fact]
    public void On_the_ground_at_the_origin_but_away_from_every_stand_names_no_stand()
    {
        var spots = new[] { Gate("", 17, 35.0501, -89.9801) };
        // About 1.1 km north of the stand: taxiing, or on a runway — not parked.
        var own = new OwnPosition(35.0601, -89.9801, OnGround: true);
        Assert.Null(TaxiPlanningBlock.ResolveDepartureStand(own, Kmem, () => spots));
    }

    // ── SayIntentions' arrival gate ────────────────────────────────────────────────────────

    [Fact]
    public void A_flight_file_gate_is_used_as_published()
    {
        var gate = TaxiPlanningBlock.ResolveArrivalGate(
            new SayIntentionsGateHint("Gate B3", new GeoPoint(Kmem.Latitude, Kmem.Longitude)), "KATL", Katl);
        Assert.Equal(new ArrivalGateChoice("Gate B3", null), gate);
    }

    [Fact]
    public void A_parking_service_gate_with_no_position_is_used_by_name()
    {
        // Live KMEM→KATL: getParking answered "B3" with no position, and it WAS the arrival gate.
        var gate = TaxiPlanningBlock.ResolveArrivalGate(
            new SayIntentionsGateHint("B3", null, SayIntentionsGateSource.ParkingService), "KATL", Katl);
        Assert.Equal(new ArrivalGateChoice("B3", null), gate);
    }

    [Fact]
    public void A_parking_service_gate_positioned_away_from_the_arrival_airport_is_refused_with_a_note()
    {
        var gate = TaxiPlanningBlock.ResolveArrivalGate(
            new SayIntentionsGateHint("17", new GeoPoint(Kmem.Latitude, Kmem.Longitude), SayIntentionsGateSource.ParkingService),
            "KATL", Katl);
        Assert.Equal(new ArrivalGateChoice(null,
            "SayIntentions' parking service named 17, but its position is not at KATL, so it is not used"), gate);
    }

    [Fact]
    public void A_parking_service_gate_at_the_arrival_airport_is_used()
    {
        var gate = TaxiPlanningBlock.ResolveArrivalGate(
            new SayIntentionsGateHint("B3", new GeoPoint(33.6400, -84.4250), SayIntentionsGateSource.ParkingService),
            "KATL", Katl);
        Assert.Equal(new ArrivalGateChoice("B3", null), gate);
    }

    [Fact]
    public void No_hint_is_no_gate()
        => Assert.Equal(new ArrivalGateChoice(null, null), TaxiPlanningBlock.ResolveArrivalGate(null, "KATL", Katl));
}
