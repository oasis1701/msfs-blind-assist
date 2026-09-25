using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Briefing;
using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Tests;

public class BriefingStandPickerTests
{
    // Spots on a 0.001°-ish grid; e/n are metres east/north of RunwayFixture's base.
    private static ParkingSpot Spot(string name, int number, int type, double e, double n, double radiusFt = 100,
                                    string airlines = "", GateSource source = GateSource.Navdata) => new()
    {
        AirportICAO = "TEST", Name = name, Number = number, Type = type, Radius = radiusFt,
        Latitude = RunwayFixture.Lat(n), Longitude = RunwayFixture.Lon(e), AirlineCodes = airlines, Source = source,
    };

    private static readonly AircraftProfile Md11F = AircraftSizeClass.Resolve("MD1F", "MD-11F", 0);
    private static readonly AircraftProfile B738 = AircraftSizeClass.Resolve("B738", "Boeing 737-800", 189);
    private static readonly AircraftProfile A388 = AircraftSizeClass.Resolve("A388", "Airbus A380-800", 500);
    private static readonly AircraftProfile Unknown = AircraftSizeClass.Resolve("ZZZZ", "", null);

    private static bool Always(ParkingSpot _) => true;

    [Fact]
    public void A_freighter_prefers_a_cargo_ramp()
    {
        var gate = Spot("G", 1, 10, 0, 0);
        var cargo = Spot("C", 1, 6, 500, 0);
        var choice = BriefingStandPicker.Pick(new[] { gate, cargo }, Md11F, null, null, Always)!;

        Assert.Same(cargo, choice.Spot);
        Assert.Equal(StandChoiceSource.Category, choice.Source);
    }

    [Fact]
    public void A_freighter_with_no_cargo_ramps_falls_back_with_a_note()
    {
        var gate = Spot("G", 1, 10, 0, 0);
        var choice = BriefingStandPicker.Pick(new[] { gate }, Md11F, null, null, Always)!;

        Assert.Same(gate, choice.Spot);
        Assert.Contains(choice.Notes, n => n.Contains("no cargo stands", StringComparison.Ordinal));
    }

    [Fact]
    public void An_airliner_prefers_gates_over_ramps_and_cargo()
    {
        var cargo = Spot("C", 1, 6, 0, 0);
        var ramp = Spot("R", 1, 4, 100, 0);
        var gate = Spot("G", 1, 11, 200, 0);
        Assert.Same(gate, BriefingStandPicker.Pick(new[] { cargo, ramp, gate }, B738, null, null, Always)!.Spot);
    }

    [Fact]
    public void An_airliner_with_no_gates_uses_a_ramp_and_says_so()
    {
        var ramp = Spot("R", 1, 5, 100, 0);
        var cargo = Spot("C", 1, 6, 0, 0);
        var choice = BriefingStandPicker.Pick(new[] { cargo, ramp }, B738, null, null, Always)!;

        Assert.Same(ramp, choice.Spot);
        Assert.Contains(choice.Notes, n => n.Contains("using a ramp", StringComparison.Ordinal));
    }

    [Fact]
    public void Wingspan_drops_stands_too_small_but_keeps_stands_of_unknown_size()
    {
        // A380: 79.75 m = 261.6 ft, navdata needs Radius >= 130.8 ft.
        var small = Spot("G", 1, 13, 0, 0, radiusFt: 100);
        var big = Spot("G", 2, 14, 300, 0, radiusFt: 150);
        var unknown = Spot("G", 3, 14, 600, 0, radiusFt: 0);
        var choice = BriefingStandPicker.Pick(new[] { small, big, unknown }, A388, null, null, Always)!;

        Assert.NotSame(small, choice.Spot);
        Assert.Empty(choice.Notes);
    }

    [Fact]
    public void Nothing_fits_falls_back_to_all_with_a_note()
    {
        var small = Spot("G", 1, 13, 0, 0, radiusFt: 100);
        var choice = BriefingStandPicker.Pick(new[] { small }, A388, null, null, Always)!;

        Assert.Same(small, choice.Spot);
        Assert.Contains(choice.Notes, n => n.Contains("79.8 m wingspan", StringComparison.Ordinal));
    }

    [Fact]
    public void Gsx_max_wingspan_is_honoured_in_metres()
    {
        var gsxSmall = Spot("G", 1, 14, 0, 0, radiusFt: 30, source: GateSource.Gsx);
        gsxSmall.MaxWingspanMeters = 65.0;
        var gsxBig = Spot("G", 2, 14, 300, 0, radiusFt: 40, source: GateSource.Gsx);
        gsxBig.MaxWingspanMeters = 80.0;
        Assert.Same(gsxBig, BriefingStandPicker.Pick(new[] { gsxSmall, gsxBig }, A388, null, null, Always)!.Spot);
    }

    [Fact]
    public void The_airline_s_own_gates_win()
    {
        var other = Spot("A", 1, 10, 0, 0, airlines: "DAL, AAL");
        var ours = Spot("B", 7, 10, 900, 0, airlines: "UAL;DAL");
        var choice = BriefingStandPicker.Pick(new[] { other, ours }, B738, "ual", null, Always)!;

        Assert.Same(ours, choice.Spot);
        Assert.Equal(StandChoiceSource.AirlineMatch, choice.Source);
    }

    [Fact]
    public void Central_stand_is_the_one_nearest_the_centroid()
    {
        var west = Spot("G", 1, 10, 0, 0);
        var middle = Spot("G", 2, 10, 400, 0);
        var east = Spot("G", 3, 10, 1000, 0);   // centroid at 466 m → G 2
        Assert.Same(middle, BriefingStandPicker.Pick(new[] { west, middle, east }, B738, null, null, Always)!.Spot);
    }

    [Fact]
    public void Excluded_types_and_deice_pads_are_never_chosen()
    {
        var fuel = Spot("F", 1, 16, 0, 0);
        var vehicles = Spot("V", 1, 17, 10, 0);
        var deice = Spot("D", 1, 10, 20, 0);
        deice.IsDeiceArea = true;
        Assert.Null(BriefingStandPicker.Pick(new[] { fuel, vehicles, deice }, B738, null, null, Always));
    }

    [Fact]
    public void Stands_with_no_graph_node_are_skipped()
    {
        var far = Spot("G", 1, 10, 0, 0);
        var near = Spot("G", 2, 10, 100, 0);
        Assert.Same(near, BriefingStandPicker.Pick(new[] { far, near }, B738, null, null, s => s.Number == 2)!.Spot);
        Assert.Null(BriefingStandPicker.Pick(new[] { far }, B738, null, null, _ => false));
    }

    [Fact]
    public void Say_intentions_gate_matches_by_normalised_name()
    {
        var j1 = Spot("J", 1, 10, 0, 0);
        var j2 = Spot("J", 2, 10, 100, 0);
        var hint = new SayIntentionsGateHint("Terminal 3 Gate J1", null);
        var choice = BriefingStandPicker.Pick(new[] { j2, j1 }, B738, "DAL", hint, Always)!;

        Assert.Same(j1, choice.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
    }

    [Fact]
    public void Say_intentions_gate_matches_an_online_alias()
    {
        var a24a = Spot("A", 24, 10, 0, 0);
        a24a.Suffix = "A";
        a24a.Aliases.Add("A24");
        var hint = new SayIntentionsGateHint("Gate A24", null);
        Assert.Same(a24a, BriefingStandPicker.Pick(new[] { a24a }, B738, null, hint, Always)!.Spot);
    }

    [Fact]
    public void Say_intentions_gate_matches_by_position_within_twice_the_radius()
    {
        // Navdata radius 100 ft = 30.5 m → 61 m acceptance; the nose-stop sits 40 m off the datum.
        var b6 = Spot("B", 6, 10, 0, 0, radiusFt: 100);
        var b7 = Spot("B", 7, 10, 200, 0, radiusFt: 100);
        var hint = new SayIntentionsGateHint("Gate 99", new GeoPoint(RunwayFixture.Lat(0), RunwayFixture.Lon(40)));
        var choice = BriefingStandPicker.Pick(new[] { b6, b7 }, B738, null, hint, Always)!;

        Assert.Same(b6, choice.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Contains(choice.Notes, n => n.Contains("matched by position", StringComparison.Ordinal));
    }

    [Fact]
    public void Say_intentions_gate_not_found_falls_back_and_says_so()
    {
        var g1 = Spot("G", 1, 10, 0, 0);
        var hint = new SayIntentionsGateHint("Gate Z9", null);
        var choice = BriefingStandPicker.Pick(new[] { g1 }, B738, null, hint, Always)!;

        Assert.Same(g1, choice.Spot);
        Assert.NotEqual(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Contains(choice.Notes, n => n.Contains("Gate Z9", StringComparison.Ordinal) && n.Contains("not found", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_aircraft_applies_no_fit_filter()
    {
        var tiny = Spot("G", 1, 9, 0, 0, radiusFt: 10);
        Assert.Same(tiny, BriefingStandPicker.Pick(new[] { tiny }, Unknown, null, null, Always)!.Spot);
    }

    [Fact]
    public void Identity_label_is_the_part_before_the_type()
    {
        Assert.Equal("A 24A", BriefingStandPicker.IdentityLabel(new ParkingSpot { Name = "A", Number = 24, Suffix = "A", Type = 10 }));
        Assert.Equal("Gate 5", BriefingStandPicker.IdentityLabel(new ParkingSpot { Number = 5, Type = 10 }));
    }

    // ── SayIntentionsArrivalGate.From ──────────────────────────────────────────

    private static SayIntentionsFlightContext Ctx(bool exists, string? origin, string? dest, string? gate) => new()
    {
        FlightJsonExists = exists, Origin = origin, Destination = dest, AssignedGate = gate,
    };

    [Fact]
    public void No_flight_json_means_no_hint()
        => Assert.Null(SayIntentionsArrivalGate.From(Ctx(false, "EGLL", "KJFK", "Gate 6"), "EGLL", "KJFK"));

    [Fact]
    public void Null_context_means_no_hint()
        => Assert.Null(SayIntentionsArrivalGate.From(null, "EGLL", "KJFK"));

    [Fact]
    public void Another_flight_s_gate_is_ignored()
    {
        Assert.Null(SayIntentionsArrivalGate.From(Ctx(true, "EGLL", "KLAX", "Gate 6"), "EGLL", "KJFK"));
        Assert.Null(SayIntentionsArrivalGate.From(Ctx(true, "LMML", "KJFK", "Gate 6"), "EGLL", "KJFK"));
    }

    [Fact]
    public void Matching_flight_yields_the_hint()
    {
        var ctx = Ctx(true, "egll", "kjfk ", "Terminal 1 Gate 6");
        ctx.AssignedGatePosition = new GeoPoint(40.64, -73.78);
        var hint = SayIntentionsArrivalGate.From(ctx, "EGLL", "KJFK")!;

        Assert.Equal("Terminal 1 Gate 6", hint.Label);
        Assert.Equal(40.64, hint.Position!.Value.Latitude);
    }

    [Fact]
    public void Blank_gate_means_no_hint()
        => Assert.Null(SayIntentionsArrivalGate.From(Ctx(true, "EGLL", "KJFK", " "), "EGLL", "KJFK"));
}
