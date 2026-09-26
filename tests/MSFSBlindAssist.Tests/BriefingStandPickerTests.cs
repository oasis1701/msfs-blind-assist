using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Briefing;
using MSFSBlindAssist.Services.SayIntentions;
using MSFSBlindAssist.Settings;

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
    private static readonly AircraftProfile B74F = AircraftSizeClass.Resolve("B74F", "Boeing 747-8F", 0);
    private static readonly AircraftProfile C172 = AircraftSizeClass.Resolve("C172", "Cessna 172", 4);
    private static readonly AircraftProfile Unknown = AircraftSizeClass.Resolve("ZZZZ", "", null);

    private static bool Always(ParkingSpot _) => true;

    /// <summary>Every stand connects to the taxiway network except the ones named.</summary>
    private static Func<ParkingSpot, bool> AllBut(params ParkingSpot[] unconnected) =>
        s => !unconnected.Any(u => ReferenceEquals(u, s));

    /// <summary>A SayIntentions gate whose published position is <paramref name="e"/> metres east of the base.</summary>
    private static SayIntentionsGateHint At(string label, double e, double n = 0) =>
        new(label, new GeoPoint(RunwayFixture.Lat(n), RunwayFixture.Lon(e)));

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
        Assert.Contains("SayIntentions assigned Gate 99, which this scenery lists as B 6", choice.Notes);
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

    // ── SayIntentions: which stand the assigned gate IS ─────────────────────────────────────
    // NormalizeParkingName strips GATE/SPOT/PARKING/…, so a letterless "Gate 9", "Spot 9" and
    // "Parking 9" all compare as "9", and navdata lists its unnamed spots (the GA ramps) first.

    [Fact]
    public void Say_intentions_gate_prefers_a_gate_over_a_same_numbered_ga_ramp_listed_first()
    {
        // CYYT: "Spot 9" (Ramp GA Medium) is listed ahead of "Gate 9" (Gate Small), 1.2 km apart.
        var spot9 = Spot("", 9, 4, 0, 0, radiusFt: 39);
        var gate9 = Spot("", 9, 9, 1200, 0, radiusFt: 52);
        var choice = BriefingStandPicker.Pick(new[] { spot9, gate9 }, B738, null, new SayIntentionsGateHint("Gate 9", null), Always)!;

        Assert.Same(gate9, choice.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
    }

    [Fact]
    public void Say_intentions_gate_duplicates_are_resolved_by_the_published_position()
    {
        var west = Spot("", 5, 10, 0, 0);
        var east = Spot("", 5, 10, 900, 0);
        var choice = BriefingStandPicker.Pick(new[] { west, east }, B738, null, At("Gate 5", 880), Always)!;

        Assert.Same(east, choice.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
    }

    [Fact]
    public void Say_intentions_published_position_outranks_the_gate_preference()
    {
        // The point SayIntentions published sits on the ramp, so the ramp is the stand it means.
        var spot9 = Spot("", 9, 4, 0, 0, radiusFt: 39);
        var gate9 = Spot("", 9, 9, 1200, 0, radiusFt: 52);
        Assert.Same(spot9, BriefingStandPicker.Pick(new[] { spot9, gate9 }, B738, null, At("Gate 9", 20), Always)!.Spot);
        Assert.Same(gate9, BriefingStandPicker.Pick(new[] { spot9, gate9 }, B738, null, At("Gate 9", 1180), Always)!.Spot);
    }

    [Fact]
    public void Say_intentions_gate_ignores_an_excluded_type_stand_of_the_same_name()
    {
        // KGCN: "Spot 105" is a FUEL spot listed ahead of "Parking 105".
        var fuel = Spot("", 105, 16, 0, 0, radiusFt: 26);
        var parking = Spot("Parking", 105, 3, 900, 0, radiusFt: 23);
        var choice = BriefingStandPicker.Pick(new[] { fuel, parking }, B738, null, new SayIntentionsGateHint("Parking 105", null), Always)!;

        Assert.Same(parking, choice.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
    }

    [Fact]
    public void Say_intentions_exact_name_beats_another_stand_s_alias_listed_first()
    {
        var a24a = Spot("A", 24, 10, 0, 0);
        a24a.Suffix = "A";
        a24a.Aliases.Add("A24");
        var a24 = Spot("A", 24, 10, 300, 0);
        var choice = BriefingStandPicker.Pick(new[] { a24a, a24 }, B738, null, new SayIntentionsGateHint("Gate A24", null), Always)!;

        Assert.Same(a24, choice.Spot);
        Assert.Empty(choice.Notes);
    }

    [Fact]
    public void Say_intentions_alias_match_says_the_scenery_uses_another_name()
    {
        var a24a = Spot("A", 24, 10, 0, 0);
        a24a.Suffix = "A";
        a24a.Aliases.Add("A24");
        var choice = BriefingStandPicker.Pick(new[] { a24a }, B738, null, new SayIntentionsGateHint("Gate A24", null), Always)!;

        Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Contains("SayIntentions assigned Gate A24, which this scenery lists as A 24A", choice.Notes);
    }

    [Fact]
    public void Say_intentions_position_match_prefers_an_ordinary_stand_to_a_nearer_excluded_one_and_names_the_label()
    {
        // The fuel spot and the de-ice pad are nearer the point, but a stand of an ordinary kind is in reach.
        var fuel = Spot("F", 1, 16, 0, 0);
        var deice = Spot("D", 1, 10, 5, 0);
        deice.IsDeiceArea = true;
        var gate = Spot("G", 1, 10, 40, 0);   // 30 m from the point, inside its 61 m acceptance
        var choice = BriefingStandPicker.Pick(new[] { fuel, deice, gate }, B738, null, At("Gate 99", 10), Always)!;

        Assert.Same(gate, choice.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Equal(new[] { "SayIntentions assigned Gate 99, which this scenery lists as G 1" }, choice.Notes);
    }

    [Theory]
    [InlineData(16, false, "fuel stand")]
    [InlineData(8, false, "military combat ramp")]
    [InlineData(17, false, "vehicle stand")]
    [InlineData(10, true, "de-icing pad")]
    public void A_position_match_takes_an_excluded_stand_when_no_ordinary_stand_is_in_reach(int type, bool deicePad, string kind)
    {
        // Nothing answers to "Gate 99", and only a stand of an excluded kind is in reach of the point, where Taxi
        // Assist's import would find it when its list carries one (a navdata-sourced list): it is briefed, and
        // its kind is said. "No stand at SayIntentions' position was found" would be untrue.
        var excluded = Spot("F", 1, type, 0, 0);
        excluded.IsDeiceArea = deicePad;
        var g1 = Spot("G", 1, 10, 800, 0);
        var choice = BriefingStandPicker.Pick(new[] { excluded, g1 }, B738, null, At("Gate 99", 10), Always)!;

        Assert.Same(excluded, choice.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Equal(new[]
        {
            "SayIntentions assigned Gate 99, which this scenery lists as F 1",
            $"this scenery marks that stand as a {kind}",
        }, choice.Notes);
    }

    [Fact]
    public void An_excluded_stand_at_the_position_that_does_not_connect_is_said_with_its_kind()
    {
        var fuel = Spot("F", 1, 16, 0, 0);
        var g1 = Spot("G", 1, 10, 800, 0);
        var choice = BriefingStandPicker.Pick(new[] { fuel, g1 }, B738, null, At("Gate 99", 10), AllBut(fuel))!;

        Assert.Same(g1, choice.Spot);
        Assert.NotEqual(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Equal(new[]
        {
            "SayIntentions assigned \"Gate 99\" was found by position as a fuel stand but does not connect to the " +
            "taxiway network; using a representative stand instead",
        }, choice.Notes);
    }

    [Fact]
    public void A_position_match_that_does_not_connect_gives_way_to_the_next_stand_in_reach_and_says_so()
    {
        // "B 6" is 20 m from the point and "B 7" 30 m, both inside their 61 m reach. B 6 does not connect, so
        // B 7 is briefed, and ONE sentence names both — never a second note calling B 7 SayIntentions' gate
        // right after naming B 6 as the stand at its position.
        var b6 = Spot("B", 6, 10, 0, 0);
        var b7 = Spot("B", 7, 10, 50, 0);
        var choice = BriefingStandPicker.Pick(new[] { b6, b7 }, B738, null, At("Gate 99", 20), AllBut(b6))!;

        Assert.Same(b7, choice.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Equal(new[]
        {
            "SayIntentions assigned Gate 99; the stand at its position, B 6 (20 m away), does not connect to the " +
            "taxiway network, so B 7 (30 m away) is used",
        }, choice.Notes);
    }

    [Theory]
    [InlineData(5, "Gate 5")]     // two listings this scenery calls the same
    [InlineData(0, "Parking")]    // two stands with no name or number
    public void The_position_fallback_sentence_tells_identically_named_stands_apart_by_distance(int number, string label)
    {
        var first = Spot("", number, 10, 0, 0);
        var second = Spot("", number, 10, 30, 0);
        var choice = BriefingStandPicker.Pick(new[] { first, second }, B738, null, At("Gate 99", 10), AllBut(first))!;

        Assert.Same(second, choice.Spot);
        Assert.Equal(new[]
        {
            $"SayIntentions assigned Gate 99; the stand at its position, {label} (10 m away), does not connect to the " +
            $"taxiway network, so {label} (20 m away) is used",
        }, choice.Notes);
    }

    [Fact]
    public void An_unconnected_ordinary_stand_at_the_position_never_hands_the_label_to_a_connected_excluded_one()
    {
        // Nothing answers to "Gate 99"; the gate "B 6" and the fuel stand "F 1" are both in reach of the point,
        // and B 6 does not connect. An ordinary stand is in reach, so the excluded one is never considered: B 6
        // is reported, and a representative stand is used.
        var b6 = Spot("B", 6, 10, 0, 0);
        var fuel = Spot("F", 1, 16, 10, 0);
        var g1 = Spot("G", 1, 10, 800, 0);
        var choice = BriefingStandPicker.Pick(new[] { b6, fuel, g1 }, B738, null, At("Gate 99", 5), AllBut(b6))!;

        Assert.Same(g1, choice.Spot);
        Assert.NotEqual(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Equal(new[]
        {
            "SayIntentions assigned \"Gate 99\" was found by position but does not connect to the taxiway network; " +
            "using a representative stand instead",
        }, choice.Notes);
    }

    // ── SayIntentions: a stand of an excluded kind ───────────────────────────────────────────
    // Military Combat, Fuel and Vehicles stands and de-ice pads are never a representative stand. But when
    // SayIntentions' own name for its gate, or an online alias, finds no stand of another kind — or, with
    // nothing answering to either, no stand of another kind is in reach of its published position — that
    // stand is briefed, as Taxi Assist's import would seat it where its list carries one (owner decision,
    // 2026-09-26), and a note says what this scenery marks it as. The import's list carries fuel and vehicle
    // stands only when it comes from navdata: GSX's gate list never does, and de-ice pads are a destination
    // type of their own there.

    [Theory]
    [InlineData(16, false, "fuel stand")]
    [InlineData(8, false, "military combat ramp")]
    [InlineData(17, false, "vehicle stand")]
    [InlineData(10, true, "de-icing pad")]
    public void A_name_found_only_on_a_stand_of_an_excluded_kind_briefs_that_stand_and_says_its_kind(int type, bool deicePad, string kind)
    {
        var skipped = Spot("", 105, type, 0, 0, radiusFt: 26);
        skipped.IsDeiceArea = deicePad;
        var gate6 = Spot("", 6, 10, 40, 0);
        var choice = BriefingStandPicker.Pick(new[] { skipped, gate6 }, B738, null, new SayIntentionsGateHint("Gate 105", null), Always)!;

        Assert.Same(skipped, choice.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Equal(new[] { $"this scenery marks that stand as a {kind}" }, choice.Notes);
    }

    [Theory]
    [InlineData(16, false, "fuel stand")]
    [InlineData(8, false, "military combat ramp")]
    [InlineData(17, false, "vehicle stand")]
    [InlineData(10, true, "de-icing pad")]
    public void An_excluded_kind_found_by_name_is_not_handed_to_the_gate_next_door_at_the_pin(int type, bool deicePad, string kind)
    {
        // The published point is 5 m from the stand the name found, and inside the reach of the gate next
        // door as well. The name has found SayIntentions' stand, so the point decides nothing; and it is
        // within that stand's own reach, so no distance is said.
        var skipped = Spot("", 105, type, 0, 0, radiusFt: 26);
        skipped.IsDeiceArea = deicePad;
        var gate6 = Spot("", 6, 10, 40, 0);
        var choice = BriefingStandPicker.Pick(new[] { skipped, gate6 }, B738, null, At("Gate 105", 5), Always)!;

        Assert.Same(skipped, choice.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Equal(new[] { $"this scenery marks that stand as a {kind}" }, choice.Notes);
    }

    [Fact]
    public void An_excluded_stand_found_only_by_its_alias_is_briefed()
    {
        // The fuel stand "B 5A" answers to the online alias "B5" (GateAliasResolver only adopts an alias that
        // carries the stand's own letter and number), and no stand is called B5.
        var fuel = Spot("B", 5, 16, 0, 0);
        fuel.Suffix = "A";
        fuel.Aliases.Add("B5");
        var gate6 = Spot("", 6, 10, 400, 0);
        var choice = BriefingStandPicker.Pick(new[] { fuel, gate6 }, B738, null, new SayIntentionsGateHint("Gate B5", null), Always)!;

        Assert.Same(fuel, choice.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Equal(new[]
        {
            "SayIntentions assigned Gate B5, which this scenery lists as B 5A",
            "this scenery marks that stand as a fuel stand",
        }, choice.Notes);
    }

    [Fact]
    public void An_ordinary_namesake_beats_an_excluded_one_even_at_the_pin()
    {
        // The fuel spot "Spot 7" is at the pin; "Parking 7" (595 m from it) and "Gate 7" (1,195 m) read
        // "7" as well. An ordinary namesake always beats an excluded one; among the ordinary ones the pin
        // decides, and with no pin the gate does.
        var fuel7 = Spot("", 7, 16, 0, 0, radiusFt: 26);
        var parking7 = Spot("Parking", 7, 3, 600, 0, radiusFt: 23);
        var gate7 = Spot("", 7, 10, 1200, 0);

        var pinned = BriefingStandPicker.Pick(new[] { fuel7, parking7, gate7 }, B738, null, At("Gate 7", 5), Always)!;
        Assert.Same(parking7, pinned.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, pinned.Source);
        Assert.Equal(new[] { "SayIntentions' position is 595 m from this stand" }, pinned.Notes);

        var unpinned = BriefingStandPicker.Pick(new[] { fuel7, parking7, gate7 }, B738, null, new SayIntentionsGateHint("Gate 7", null), Always)!;
        Assert.Same(gate7, unpinned.Spot);
        Assert.Empty(unpinned.Notes);
    }

    [Fact]
    public void An_ordinary_stand_s_alias_beats_an_excluded_stand_s_name()
    {
        // The fuel stand is called "B 5"; the gate "B 5A" answers to the online alias "B5". The ordinary stand
        // is looked for first, by its name and then by its aliases — where Taxi Assist's import would take the
        // name first, whatever kind of stand carries it.
        var fuel = Spot("B", 5, 16, 0, 0, radiusFt: 26);
        var b5a = Spot("B", 5, 10, 900, 0);
        b5a.Suffix = "A";
        b5a.Aliases.Add("B5");
        var choice = BriefingStandPicker.Pick(new[] { fuel, b5a }, B738, null, new SayIntentionsGateHint("Gate B5", null), Always)!;

        Assert.Same(b5a, choice.Spot);
        Assert.Equal(new[] { "SayIntentions assigned Gate B5, which this scenery lists as B 5A" }, choice.Notes);
    }

    [Fact]
    public void An_unconnected_ordinary_namesake_never_hands_the_label_to_a_connected_excluded_one()
    {
        // "Gate 7" does not connect; the fuel spot "Spot 7" beside it does. With no pin and with a pin inside
        // both stands' reach, the ordinary namesake is SayIntentions' stand: it is reported, and a
        // representative stand is used — never the fuel spot.
        var gate7 = Spot("", 7, 10, 0, 0);
        var fuel7 = Spot("", 7, 16, 20, 0);
        var g1 = Spot("G", 1, 10, 600, 0);

        foreach (var hint in new[] { new SayIntentionsGateHint("Gate 7", null), At("Gate 7", 10) })
        {
            var choice = BriefingStandPicker.Pick(new[] { gate7, fuel7, g1 }, B738, null, hint, AllBut(gate7))!;

            Assert.Same(g1, choice.Spot);
            Assert.NotEqual(StandChoiceSource.SayIntentions, choice.Source);
            Assert.Equal(new[]
            {
                "SayIntentions assigned \"Gate 7\" was found but does not connect to the taxiway network; " +
                "using a representative stand instead",
            }, choice.Notes);
        }
    }

    [Fact]
    public void An_excluded_namesake_that_does_not_connect_is_said_and_a_representative_stand_used()
    {
        // Like any stand the name finds, it must connect to the taxiway network; the pin is not then tried.
        var fuel = Spot("", 105, 16, 0, 0, radiusFt: 26);
        var b5 = Spot("B", 5, 10, 1210, 0);
        var choice = BriefingStandPicker.Pick(new[] { fuel, b5 }, B738, null, At("Spot 105", 1200), AllBut(fuel))!;

        Assert.Same(b5, choice.Spot);
        Assert.NotEqual(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Equal(new[]
        {
            "SayIntentions assigned \"Spot 105\" was found as a fuel stand but does not connect to the taxiway " +
            "network; using a representative stand instead",
        }, choice.Notes);
    }

    // ── SayIntentions: navdata's None type ───────────────────────────────────────────────────
    // Navdata reads UNKN as type 1, and UNKN is not only "no stand": LEBB's jetway gates 101-106 are
    // UNKN, and so is KGCN's helipad "Spot 101". SayIntentions may name one; it ranks after every
    // stand of another type that the evidence cannot tell it from.

    [Fact]
    public void Say_intentions_gate_on_a_None_type_stand_is_briefed()
    {
        // LEBB: SayIntentions' "Gate 101" is a jetway stand navdata types UNKN, and the name alone finds it.
        var unkn101 = Spot("", 101, 1, 0, 0, radiusFt: 33);
        unkn101.HasJetway = true;
        var gate6 = Spot("", 6, 10, 40, 0);
        var choice = BriefingStandPicker.Pick(new[] { unkn101, gate6 }, B738, null, new SayIntentionsGateHint("Gate 101", null), Always)!;

        Assert.Same(unkn101, choice.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Empty(choice.Notes);
    }

    [Fact]
    public void Say_intentions_gate_on_a_None_type_stand_is_not_handed_to_the_gate_next_door()
    {
        // The published point is inside the reach of the gate next door as well, but the name has found
        // the stand, so the point is never used to hand the label to a neighbour.
        var unkn101 = Spot("", 101, 1, 0, 0, radiusFt: 33);
        unkn101.HasJetway = true;
        var gate6 = Spot("", 6, 10, 40, 0);
        var choice = BriefingStandPicker.Pick(new[] { unkn101, gate6 }, B738, null, At("Gate 101", 5), Always)!;

        Assert.Same(unkn101, choice.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Empty(choice.Notes);
    }

    [Fact]
    public void Say_intentions_gate_prefers_a_known_type_stand_to_a_None_type_one_of_the_same_name()
    {
        // KGCN: "Spot 101" is a 13 ft UNKN helipad listed ahead of "Parking 101" (Ramp GA Small). Both
        // compare as "101"; with no published position, only the type tells them apart.
        var helipad = Spot("", 101, 1, 0, 0, radiusFt: 13);
        var parking = Spot("Parking", 101, 3, 900, 0, radiusFt: 23);
        var choice = BriefingStandPicker.Pick(new[] { helipad, parking }, B738, null, new SayIntentionsGateHint("Parking 101", null), Always)!;

        Assert.Same(parking, choice.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
    }

    [Fact]
    public void A_None_type_stand_never_stands_in_for_an_unconnected_known_type_match()
    {
        // KGCN with "Parking 101" off the network: the helipad sharing its number ranks below it, so it
        // is a different stand, not a fallback for SayIntentions' gate.
        var helipad = Spot("", 101, 1, 0, 0, radiusFt: 13);
        var parking101 = Spot("Parking", 101, 3, 900, 0, radiusFt: 23);
        var parking102 = Spot("Parking", 102, 3, 950, 0, radiusFt: 23);

        foreach (var hint in new[] { new SayIntentionsGateHint("Parking 101", null), At("Parking 101", 905) })
        {
            var choice = BriefingStandPicker.Pick(new[] { helipad, parking101, parking102 }, B738, null, hint, AllBut(parking101))!;

            Assert.Same(parking102, choice.Spot);
            Assert.NotEqual(StandChoiceSource.SayIntentions, choice.Source);
            Assert.Contains(choice.Notes, n => n.Contains("\"Parking 101\"", StringComparison.Ordinal)
                                            && n.Contains("does not connect to the taxiway network", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void A_published_position_tie_goes_to_the_known_type_stand()
    {
        // One stand listed twice, once as UNKN, 0.6 m apart: the published point cannot tell the two
        // listings apart, so the typed one is briefed, by name and by the position alone. Radius 33 ft
        // (10 m) answers to 20 m, so every stand here is within reach of the point.
        var unkn = Spot("", 101, 1, 0, 0, radiusFt: 33);
        var twin = Spot("", 101, 10, 0.6, 0, radiusFt: 33);
        var apart = Spot("", 101, 10, 1.5, 0, radiusFt: 33);

        foreach (var label in new[] { "Gate 101", "Gate 99" })
        {
            var tie = BriefingStandPicker.Pick(new[] { unkn, twin }, B738, null, At(label, -15), Always)!;
            Assert.Same(twin, tie.Spot);
            Assert.Equal(StandChoiceSource.SayIntentions, tie.Source);

            // More than a metre nearer, the UNKN stand is the one the point means.
            var nearer = BriefingStandPicker.Pick(new[] { unkn, apart }, B738, null, At(label, -15), Always)!;
            Assert.Same(unkn, nearer.Spot);
            Assert.Equal(StandChoiceSource.SayIntentions, nearer.Source);
        }
    }

    [Fact]
    public void Say_intentions_gate_found_but_unconnected_is_said_and_a_representative_stand_used()
    {
        var gate8 = Spot("", 8, 10, 0, 0);
        var g3 = Spot("G", 3, 10, 500, 0);
        var choice = BriefingStandPicker.Pick(new[] { gate8, g3 }, B738, null, new SayIntentionsGateHint("Gate 8", null), AllBut(gate8))!;

        Assert.Same(g3, choice.Spot);
        Assert.NotEqual(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Contains(choice.Notes, n => n.Contains("\"Gate 8\"", StringComparison.Ordinal) && n.Contains("does not connect to the taxiway network", StringComparison.Ordinal));
        Assert.DoesNotContain(choice.Notes, n => n.Contains("not found", StringComparison.Ordinal));
    }

    [Fact]
    public void A_same_numbered_ga_ramp_never_stands_in_for_an_unconnected_assigned_gate()
    {
        // With no pin, with the pin at the gate, and with the pin in the reach of neither (500 m from the gate,
        // 700 m from the ramp): a pin far from both must never let the ramp in where no pin would not.
        var gate9 = Spot("", 9, 9, 0, 0, radiusFt: 52);
        var spot9 = Spot("", 9, 4, 1200, 0, radiusFt: 39);
        var g1 = Spot("G", 1, 10, 600, 0);

        foreach (var hint in new[] { new SayIntentionsGateHint("Gate 9", null), At("Gate 9", 10), At("Gate 9", 500) })
        {
            var choice = BriefingStandPicker.Pick(new[] { gate9, spot9, g1 }, B738, null, hint, AllBut(gate9))!;
            Assert.Same(g1, choice.Spot);
            Assert.NotEqual(StandChoiceSource.SayIntentions, choice.Source);
            Assert.Contains(choice.Notes, n => n.Contains("does not connect to the taxiway network", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void An_unconnected_listing_falls_through_to_an_equally_matching_twin()
    {
        // The same gate listed twice, 30 m apart: the evidence cannot tell them apart, so the one that
        // connects is SayIntentions' gate — with no position (both gates) and with one (both in reach).
        var first = Spot("", 5, 10, 0, 0);
        var twin = Spot("", 5, 10, 30, 0);
        foreach (var hint in new[] { new SayIntentionsGateHint("Gate 5", null), At("Gate 5", 15) })
        {
            var choice = BriefingStandPicker.Pick(new[] { first, twin }, B738, null, hint, AllBut(first))!;
            Assert.Same(twin, choice.Spot);
            Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
        }
    }

    [Fact]
    public void A_twin_listing_stands_in_when_the_pin_reaches_neither()
    {
        // The same gate listed twice, 30 m apart, with SayIntentions' pin 500 m from the nearer listing and
        // in the reach of neither: the pin points at neither, so it cannot make the twin a different stand
        // where no pin would not. The twin is briefed, and the distance said.
        var first = Spot("", 5, 10, 0, 0);
        var twin = Spot("", 5, 10, 30, 0);
        var choice = BriefingStandPicker.Pick(new[] { first, twin }, B738, null, At("Gate 5", -500), AllBut(first))!;

        Assert.Same(twin, choice.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Equal(new[] { "SayIntentions' position is 530 m from this stand" }, choice.Notes);
    }

    [Fact]
    public void A_far_namesake_never_stands_in_for_an_unconnected_one_near_the_pin()
    {
        // Two "Gate 5" 3 km apart, the pin 70 m from the nearer one — just outside its 61 m reach — which does
        // not connect. The pin points at neither, but the far one is kilometres beyond anything the pin could
        // mean: it is never briefed as SayIntentions' gate, and a representative stand is used.
        var near5 = Spot("", 5, 10, 0, 0);
        var far5 = Spot("", 5, 10, 3000, 0);
        var g1 = Spot("G", 1, 10, 100, 0);
        var g2 = Spot("G", 2, 10, 200, 0);
        var choice = BriefingStandPicker.Pick(new[] { near5, far5, g1, g2 }, B738, null, At("Gate 5", 70), AllBut(near5))!;

        Assert.NotSame(far5, choice.Spot);
        Assert.NotEqual(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Equal(new[]
        {
            "SayIntentions assigned \"Gate 5\" was found but does not connect to the taxiway network; " +
            "using a representative stand instead",
        }, choice.Notes);
    }

    [Fact]
    public void With_the_pin_in_reach_of_neither_namesake_a_better_ranked_one_stands_in()
    {
        // The GA ramp "Spot 9" (answering to 24 m) is 46 m from the pin and does not connect; the gate "Gate 9"
        // (answering to 32 m) is 56 m from it. The pin points at neither, so the rule for no position decides,
        // and that rule puts the gate first: Gate 9 is briefed, and the distance said.
        var spot9 = Spot("", 9, 4, 0, 0, radiusFt: 39);
        var gate9 = Spot("", 9, 9, 102, 0, radiusFt: 52);
        var choice = BriefingStandPicker.Pick(new[] { spot9, gate9 }, B738, null, At("Gate 9", 46), AllBut(spot9))!;

        Assert.Same(gate9, choice.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Equal(new[] { "SayIntentions' position is 56 m from this stand" }, choice.Notes);
    }

    [Fact]
    public void A_namesake_just_refused_is_never_the_representative_stand()
    {
        // The nearer "Gate 5" does not connect; the other "Gate 5", 3 km away, is too far from the pin to stand
        // in for it. It sits at the centre of the airport's other gates, so the representative pick would
        // otherwise brief "representative stand Gate 5" right after "Gate 5 was found but does not connect".
        var near5 = Spot("", 5, 10, 0, 0);
        var far5 = Spot("", 5, 10, 3000, 0);
        var g1 = Spot("G", 1, 10, 2900, 0);
        var g2 = Spot("G", 2, 10, 3100, 0);
        var choice = BriefingStandPicker.Pick(new[] { near5, far5, g1, g2 }, B738, null, At("Gate 5", 70), AllBut(near5))!;

        Assert.NotSame(far5, choice.Spot);
        Assert.Same(g1, choice.Spot);
        Assert.NotEqual(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Equal(new[]
        {
            "SayIntentions assigned \"Gate 5\" was found but does not connect to the taxiway network; " +
            "using a representative stand instead",
        }, choice.Notes);
    }

    [Fact]
    public void A_nearer_namesake_out_of_reach_that_does_not_connect_gives_way_to_one_at_the_pin()
    {
        // The GA ramp "Spot 5" (13 ft, answering to 8 m) is 10 m from the pin and ranks first by distance, but
        // the pin is outside its reach; "Gate 5" is 20 m from the pin and inside its 61 m reach. The ramp does
        // not connect, and the gate — where the pin actually points — is at least as plausible, so it is used.
        var spot5 = Spot("", 5, 4, 0, 0, radiusFt: 13);
        var gate5 = Spot("", 5, 10, 30, 0);
        var choice = BriefingStandPicker.Pick(new[] { spot5, gate5 }, B738, null, At("Gate 5", 10), AllBut(spot5))!;

        Assert.Same(gate5, choice.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Empty(choice.Notes);
    }

    [Fact]
    public void Say_intentions_position_match_that_does_not_connect_is_said()
    {
        var b6 = Spot("B", 6, 10, 0, 0);
        var g1 = Spot("G", 1, 10, 800, 0);
        var choice = BriefingStandPicker.Pick(new[] { b6, g1 }, B738, null, At("Gate 99", 20), AllBut(b6))!;

        Assert.Same(g1, choice.Spot);
        Assert.NotEqual(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Contains(choice.Notes, n => n.Contains("\"Gate 99\"", StringComparison.Ordinal)
                                        && n.Contains("by position", StringComparison.Ordinal)
                                        && n.Contains("does not connect to the taxiway network", StringComparison.Ordinal));
    }

    // ── SayIntentions' pin: the name first, the published position only when no name matches ─
    // Taxi Assist's SayIntentions import resolves a gate by its name, then by the scenery's online
    // aliases, and uses the published position only when neither matches; the briefing does the same
    // (owner decision, 2026-09-26, reversing "the stand at the pin wins"). A stand the name or alias
    // found is SayIntentions' gate wherever it is; when the pin lies outside that stand's own reach, a
    // note says how far away it is.

    [Fact]
    public void Say_intentions_name_wins_over_the_stand_at_the_pin_and_the_distance_is_said()
    {
        // SayIntentions "Gate 7", pinned 10 m from this scenery's "Gate 7A"; the only stand that reads
        // "7" is the GA ramp "Spot 7", 1.21 km from the pin. The name decides, as in Taxi Assist's import.
        var gate7a = Spot("", 7, 10, 0, 0);
        gate7a.Suffix = "A";
        var spot7 = Spot("", 7, 4, 1220, 0, radiusFt: 39);
        var choice = BriefingStandPicker.Pick(new[] { spot7, gate7a }, B738, null, At("Gate 7", 10), Always)!;

        Assert.Same(spot7, choice.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Equal(new[] { "SayIntentions' position is 1.2 km from this stand" }, choice.Notes);

        // The same note in the pilot's feet setting.
        var inFeet = BriefingStandPicker.Pick(new[] { spot7, gate7a }, B738, null, At("Gate 7", 10), Always, DistanceUnit.Feet)!;
        Assert.Equal(new[] { "SayIntentions' position is 3,970 ft from this stand" }, inFeet.Notes);
    }

    [Fact]
    public void Among_namesakes_all_far_from_the_pin_the_nearest_to_it_is_used()
    {
        // "Spot 7" and "Parking 7" both read "7" and are both far from the pin; "B 7" is at it but reads
        // "B7". The name still decides, and the pin chooses between the namesakes.
        var spot7 = Spot("", 7, 4, 0, 0, radiusFt: 39);
        var parking7 = Spot("Parking", 7, 3, 600, 0, radiusFt: 23);
        var b7 = Spot("B", 7, 10, 1200, 0);
        var choice = BriefingStandPicker.Pick(new[] { spot7, parking7, b7 }, B738, null, At("Gate 7", 1205), Always)!;

        Assert.Same(parking7, choice.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Equal(new[] { "SayIntentions' position is 605 m from this stand" }, choice.Notes);
    }

    [Fact]
    public void An_excluded_namesake_far_from_the_pin_is_briefed_with_its_kind_and_the_distance()
    {
        // The fuel spot "Spot 105" is 1.2 km from the pin; the gate "B 5" is 10 m from it. The name has found
        // SayIntentions' stand, as it would in Taxi Assist's import, so the pin decides nothing: its distance
        // is said after the stand's kind.
        var fuel = Spot("", 105, 16, 0, 0, radiusFt: 26);
        var b5 = Spot("B", 5, 10, 1210, 0);
        foreach (var label in new[] { "Spot 105", "Parking 105" })
        {
            var choice = BriefingStandPicker.Pick(new[] { fuel, b5 }, B738, null, At(label, 1200), Always)!;

            Assert.Same(fuel, choice.Spot);
            Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
            Assert.Equal(new[]
            {
                "this scenery marks that stand as a fuel stand",
                "SayIntentions' position is 1.2 km from this stand",
            }, choice.Notes);
        }
    }

    [Fact]
    public void A_namesake_is_used_even_with_nothing_at_the_pin_and_the_distance_is_said()
    {
        // SayIntentions "Gate 8" is pinned where this scenery has no stand; the only "8" here is the GA
        // ramp "Spot 8", 1.2 km away. The name finds it, as Taxi Assist's import would: SayIntentions'
        // gate, not a representative stand.
        var spot8 = Spot("", 8, 4, 1200, 0, radiusFt: 39);
        var g1 = Spot("G", 1, 10, 600, 0);
        var choice = BriefingStandPicker.Pick(new[] { spot8, g1 }, B738, null, At("Gate 8", 0), Always)!;

        Assert.Same(spot8, choice.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Equal(new[] { "SayIntentions' position is 1.2 km from this stand" }, choice.Notes);
    }

    [Fact]
    public void With_a_pin_and_no_stand_of_that_name_the_note_speaks_of_the_position()
    {
        // No stand here is called 8 and none is at the pin. The note is about the position, the last
        // evidence tried, never "was not found at this airport".
        var g1 = Spot("G", 1, 10, 600, 0);
        var choice = BriefingStandPicker.Pick(new[] { g1 }, B738, null, At("Gate 8", 0), Always)!;

        Assert.Same(g1, choice.Spot);
        Assert.NotEqual(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Equal(new[]
        {
            "SayIntentions assigned \"Gate 8\", but no stand at SayIntentions' position was found in this scenery; " +
            "using a representative stand instead",
        }, choice.Notes);
    }

    [Fact]
    public void The_distance_is_said_only_outside_the_named_stand_s_own_reach()
    {
        // Navdata radius 100 ft = 30.5 m answers to 61 m: a pin 60 m away is the named stand's own, one
        // 62 m away is not, and the name decides either way.
        var b6 = Spot("B", 6, 10, 0, 0, radiusFt: 100);

        var inside = BriefingStandPicker.Pick(new[] { b6 }, B738, null, At("Gate B6", 60), Always)!;
        Assert.Same(b6, inside.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, inside.Source);
        Assert.Empty(inside.Notes);

        var outside = BriefingStandPicker.Pick(new[] { b6 }, B738, null, At("Gate B6", 62), Always)!;
        Assert.Same(b6, outside.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, outside.Source);
        Assert.Equal(new[] { "SayIntentions' position is 62 m from this stand" }, outside.Notes);
    }

    [Fact]
    public void The_distance_note_reads_a_gsx_radius_in_metres()
    {
        // A GSX radius is METRES: 30 m answers to 60 m. Read as feet (9.1 m) it would answer to 18 m, and a
        // pin 55 m from its own stand would be said to be far from it.
        var gsx = Spot("B", 6, 14, 0, 0, radiusFt: 30, source: GateSource.Gsx);

        var inside = BriefingStandPicker.Pick(new[] { gsx }, B738, null, At("Gate B6", 55), Always)!;
        Assert.Same(gsx, inside.Spot);
        Assert.Empty(inside.Notes);

        var outside = BriefingStandPicker.Pick(new[] { gsx }, B738, null, At("Gate B6", 65), Always)!;
        Assert.Same(gsx, outside.Spot);
        Assert.Equal(new[] { "SayIntentions' position is 65 m from this stand" }, outside.Notes);
    }

    [Fact]
    public void The_distance_note_gives_a_stand_of_unknown_size_60_m()
    {
        // No radius — every OpenStreetMap stand — answers to 60 m, as it does for the position match.
        var osm = Spot("B", 6, 0, 0, 0, radiusFt: 0);

        var inside = BriefingStandPicker.Pick(new[] { osm }, B738, null, At("Gate B6", 55), Always)!;
        Assert.Same(osm, inside.Spot);
        Assert.Empty(inside.Notes);

        var outside = BriefingStandPicker.Pick(new[] { osm }, B738, null, At("Gate B6", 65), Always)!;
        Assert.Same(osm, outside.Spot);
        Assert.Equal(new[] { "SayIntentions' position is 65 m from this stand" }, outside.Notes);
    }

    [Fact]
    public void Without_a_published_position_no_distance_is_said()
    {
        // No pin, nothing to measure: the name decides, as it always did, and adds no note.
        var gate7a = Spot("", 7, 10, 0, 0);
        gate7a.Suffix = "A";
        var spot7 = Spot("", 7, 4, 1220, 0, radiusFt: 39);
        var choice = BriefingStandPicker.Pick(new[] { spot7, gate7a }, B738, null, new SayIntentionsGateHint("Gate 7", null), Always)!;

        Assert.Same(spot7, choice.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Empty(choice.Notes);
    }

    [Fact]
    public void With_no_position_and_no_name_match_the_gate_is_not_found_at_this_airport()
    {
        var g1 = Spot("G", 1, 10, 0, 0);
        var choice = BriefingStandPicker.Pick(new[] { g1 }, B738, null, new SayIntentionsGateHint("Terminal 4 Gate B99", null), Always)!;

        Assert.Contains("SayIntentions assigned gate \"Terminal 4 Gate B99\" was not found at this airport; using a representative stand instead",
            choice.Notes);
    }

    [Fact]
    public void A_stand_found_by_position_for_a_label_with_no_stand_id_is_never_a_silent_substitution()
    {
        // SayIntentions' label normalises to nothing ("Gate") and so does the stand's own ("Parking"): compared as names
        // the two are "the same", and the stand was briefed as SayIntentions' gate with no word that it was found by
        // position alone.
        var unnamed = Spot("", 0, 10, 0, 0);
        var choice = BriefingStandPicker.Pick(new[] { unnamed }, B738, null, At("Gate", 10), Always)!;

        Assert.Same(unnamed, choice.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Contains("SayIntentions assigned Gate, which this scenery lists as Parking", choice.Notes);
    }

    [Fact]
    public void A_name_match_at_the_pin_beats_a_nearer_differently_named_stand()
    {
        // "Gate 7" is 30 m from the pin (inside its 61 m reach), "Gate 7A" only 5 m: the name decides.
        var gate7 = Spot("", 7, 10, 0, 0);
        var gate7a = Spot("", 7, 10, 35, 0);
        gate7a.Suffix = "A";
        var choice = BriefingStandPicker.Pick(new[] { gate7a, gate7 }, B738, null, At("Gate 7", 30), Always)!;

        Assert.Same(gate7, choice.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Empty(choice.Notes);
    }

    [Fact]
    public void An_alias_counts_wherever_the_stand_is_and_a_far_pin_is_said()
    {
        // "A 24A" answers to the online alias "A24". Pinned at "B 3", 905 m away, the alias still decides,
        // as it does in Taxi Assist's import, and the distance is said; pinned at "A 24A" there is nothing
        // more to say.
        var a24a = Spot("A", 24, 10, 0, 0);
        a24a.Suffix = "A";
        a24a.Aliases.Add("A24");
        var b3 = Spot("B", 3, 10, 900, 0);

        var atB3 = BriefingStandPicker.Pick(new[] { a24a, b3 }, B738, null, At("Gate A24", 905), Always)!;
        Assert.Same(a24a, atB3.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, atB3.Source);
        Assert.Equal(new[]
        {
            "SayIntentions assigned Gate A24, which this scenery lists as A 24A",
            "SayIntentions' position is 905 m from this stand",
        }, atB3.Notes);

        var atA24a = BriefingStandPicker.Pick(new[] { a24a, b3 }, B738, null, At("Gate A24", 5), Always)!;
        Assert.Same(a24a, atA24a.Spot);
        Assert.Equal(new[] { "SayIntentions assigned Gate A24, which this scenery lists as A 24A" }, atA24a.Notes);
    }

    // ── SayIntentions position acceptance ────────────────────────────────────────────────────

    [Fact]
    public void Position_match_reads_a_navdata_radius_in_feet()
    {
        // 100 ft = 30.5 m answers to 61 m; read as metres it would answer to 150 m.
        var b6 = Spot("B", 6, 10, 0, 0, radiusFt: 100);
        var choice = BriefingStandPicker.Pick(new[] { b6 }, B738, null, At("Gate 99", 70), Always)!;

        Assert.NotEqual(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Contains(choice.Notes, n => n.Contains("no stand at SayIntentions' position was found", StringComparison.Ordinal));
    }

    [Fact]
    public void Position_match_reads_a_gsx_radius_in_metres()
    {
        // A GSX radius is METRES (the helper's parameter name notwithstanding): 30 m answers to 60 m.
        // Read as feet (9.1 m) it would answer to 18 m and miss.
        var gsx = Spot("B", 6, 10, 0, 0, radiusFt: 30, source: GateSource.Gsx);
        var choice = BriefingStandPicker.Pick(new[] { gsx }, B738, null, At("Gate 99", 50), Always)!;

        Assert.Same(gsx, choice.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
    }

    [Fact]
    public void Position_match_accepts_a_stand_of_unknown_size_within_60_m()
    {
        var osm = Spot("B", 6, 0, 0, 0, radiusFt: 0);   // OpenStreetMap: no type, no size
        Assert.Equal(StandChoiceSource.SayIntentions,
            BriefingStandPicker.Pick(new[] { osm }, B738, null, At("Gate 99", 55), Always)!.Source);
        Assert.NotEqual(StandChoiceSource.SayIntentions,
            BriefingStandPicker.Pick(new[] { osm }, B738, null, At("Gate 99", 65), Always)!.Source);
    }

    [Fact]
    public void Position_match_never_reaches_past_150_m()
    {
        // 300 ft = 91.4 m, doubled 183 m: only the 150 m backstop stops a point at 160 m.
        var big = Spot("B", 6, 14, 0, 0, radiusFt: 300);
        Assert.Equal(StandChoiceSource.SayIntentions,
            BriefingStandPicker.Pick(new[] { big }, B738, null, At("Gate 99", 140), Always)!.Source);
        Assert.NotEqual(StandChoiceSource.SayIntentions,
            BriefingStandPicker.Pick(new[] { big }, B738, null, At("Gate 99", 160), Always)!.Source);
    }

    // ── Representative stand: honest notes, and never null while a stand connects ───────────

    [Fact]
    public void A_freighter_is_told_stand_types_are_unknown_not_that_there_are_no_cargo_stands()
    {
        var a1 = Spot("A", 1, 0, 0, 0, radiusFt: 0);
        var a2 = Spot("A", 2, 0, 100, 0, radiusFt: 0);
        var choice = BriefingStandPicker.Pick(new[] { a1, a2 }, Md11F, null, null, Always)!;

        Assert.Contains(choice.Notes, n => n.Contains("stand types unknown", StringComparison.Ordinal));
        Assert.DoesNotContain(choice.Notes, n => n.Contains("no cargo stands", StringComparison.Ordinal));
    }

    [Fact]
    public void An_airliner_is_told_stand_types_are_unknown()
    {
        var a1 = Spot("A", 1, 0, 0, 0, radiusFt: 0);
        var a2 = Spot("A", 2, 0, 100, 0, radiusFt: 0);
        var choice = BriefingStandPicker.Pick(new[] { a1, a2 }, B738, null, null, Always)!;

        Assert.Contains(choice.Notes, n => n.Contains("stand types unknown", StringComparison.Ordinal));
    }

    [Fact]
    public void An_airliner_with_only_cargo_stands_is_told_so()
    {
        var cargo = Spot("C", 1, 6, 0, 0);
        var choice = BriefingStandPicker.Pick(new[] { cargo }, B738, null, null, Always)!;

        Assert.Same(cargo, choice.Spot);
        Assert.Contains(choice.Notes, n => n.Contains("no gate or ramp stands at this airport; using a cargo stand", StringComparison.Ordinal));
    }

    [Fact]
    public void The_wingspan_note_names_the_stands_it_judged()
    {
        var small = Spot("G", 1, 13, 0, 0, radiusFt: 100);
        var gateChoice = BriefingStandPicker.Pick(new[] { small }, A388, null, null, Always)!;
        Assert.Contains(gateChoice.Notes, n => n.Contains("no gate at this airport is marked as fitting a 79.8 m wingspan", StringComparison.Ordinal));
        var inFeet = BriefingStandPicker.Pick(new[] { small }, A388, null, null, Always, DistanceUnit.Feet)!;
        Assert.Contains(inFeet.Notes, n => n.Contains("no gate at this airport is marked as fitting a 262 ft wingspan", StringComparison.Ordinal));

        // A freighter with no cargo stand judged every stand, and says "stand".
        var anyChoice = BriefingStandPicker.Pick(new[] { small }, B74F, null, null, Always)!;
        Assert.Contains(anyChoice.Notes, n => n.Contains("no stand at this airport is marked as fitting a 68.4 m wingspan", StringComparison.Ordinal));
    }

    [Fact]
    public void An_airline_stand_off_the_network_does_not_leave_the_leg_without_a_stand()
    {
        var ours = Spot("A", 1, 10, 0, 0, airlines: "DAL");
        var other = Spot("B", 2, 10, 500, 0);
        var choice = BriefingStandPicker.Pick(new[] { ours, other }, B738, "DAL", null, AllBut(ours));

        Assert.NotNull(choice);
        Assert.Same(other, choice.Spot);
        Assert.Equal(StandChoiceSource.Category, choice.Source);
    }

    [Fact]
    public void A_freighter_whose_cargo_stands_do_not_connect_uses_another_stand_and_says_why()
    {
        var cargo = Spot("C", 1, 6, 0, 0);
        var gate = Spot("G", 1, 10, 500, 0);
        var choice = BriefingStandPicker.Pick(new[] { cargo, gate }, Md11F, null, null, AllBut(cargo));

        Assert.NotNull(choice);
        Assert.Same(gate, choice.Spot);
        Assert.Contains(choice.Notes, n => n.Contains("no cargo stand at this airport connects to the taxiway network", StringComparison.Ordinal));
    }

    [Fact]
    public void A_fitting_stand_off_the_network_is_said_to_be_off_the_network()
    {
        var big = Spot("G", 1, 14, 0, 0, radiusFt: 150);
        var small = Spot("G", 2, 13, 500, 0, radiusFt: 100);
        var choice = BriefingStandPicker.Pick(new[] { big, small }, A388, null, null, AllBut(big));

        Assert.NotNull(choice);
        Assert.Same(small, choice.Spot);
        Assert.Contains(choice.Notes, n => n.Contains("no gate that fits a 79.8 m wingspan connects to the taxiway network", StringComparison.Ordinal));
    }

    [Fact]
    public void Wingspan_filter_can_choose_a_stand_of_unknown_size()
    {
        var small = Spot("G", 1, 13, 0, 0, radiusFt: 100);    // too small for an A380
        var unknown = Spot("G", 2, 14, 100, 0, radiusFt: 0);  // size unknown: kept, and the only one left
        var choice = BriefingStandPicker.Pick(new[] { small, unknown }, A388, null, null, Always)!;

        Assert.Same(unknown, choice.Spot);
        Assert.Empty(choice.Notes);
    }

    [Fact]
    public void Central_tie_is_broken_by_the_stand_description()
    {
        // Two listings at one point are equally central; the description decides, never list order.
        var second = Spot("G", 2, 10, 0, 0);
        var first = Spot("G", 1, 10, 0, 0);
        Assert.Same(first, BriefingStandPicker.Pick(new[] { second, first }, B738, null, null, Always)!.Spot);
    }

    [Fact]
    public void Airline_codes_may_be_separated_by_whitespace()
    {
        var other = Spot("A", 1, 10, 0, 0, airlines: "AAL");
        var ours = Spot("B", 7, 10, 900, 0, airlines: "UAL DAL\tSWA");
        var choice = BriefingStandPicker.Pick(new[] { other, ours }, B738, "DAL", null, Always)!;

        Assert.Same(ours, choice.Spot);
        Assert.Equal(StandChoiceSource.AirlineMatch, choice.Source);
    }

    [Fact]
    public void None_and_military_combat_stands_are_never_chosen()
    {
        var none = Spot("N", 1, 1, 0, 0);
        var combat = Spot("M", 1, 8, 10, 0);
        Assert.Null(BriefingStandPicker.Pick(new[] { none, combat }, B738, null, null, Always));
    }

    [Fact]
    public void A_representative_stand_is_never_of_the_None_type()
    {
        // An UNKN stand may be SayIntentions' gate when the name finds it, but it is no basis for a
        // "typical stand" guess. Here it is the only stand that connects, so whatever the SayIntentions
        // step concludes (no gate assigned, a gate not found, a gate found off the network) there is
        // no representative stand.
        var unkn = Spot("", 101, 1, 0, 0, radiusFt: 33);
        unkn.HasJetway = true;
        var gate7 = Spot("", 7, 10, 500, 0);

        foreach (var hint in new SayIntentionsGateHint?[]
                 {
                     null, new SayIntentionsGateHint("Gate 99", null), new SayIntentionsGateHint("Gate 7", null),
                 })
            Assert.Null(BriefingStandPicker.Pick(new[] { unkn, gate7 }, B738, null, hint, AllBut(gate7)));
    }

    [Fact]
    public void A_stand_of_unknown_type_is_kept()
    {
        var osm = Spot("A", 5, 0, 0, 0, radiusFt: 0);
        Assert.Same(osm, BriefingStandPicker.Pick(new[] { osm }, B738, null, null, Always)!.Spot);
    }

    [Fact]
    public void An_airline_with_no_stands_here_keeps_the_category_source()
    {
        var gate = Spot("A", 1, 10, 0, 0, airlines: "DAL");
        var choice = BriefingStandPicker.Pick(new[] { gate }, B738, "BAW", null, Always)!;

        Assert.Same(gate, choice.Spot);
        Assert.Equal(StandChoiceSource.Category, choice.Source);
    }

    [Fact]
    public void A_code_A_aircraft_prefers_a_ga_ramp()
    {
        var gate = Spot("G", 1, 10, 0, 0);
        var ramp = Spot("R", 1, 3, 500, 0, radiusFt: 30);
        var choice = BriefingStandPicker.Pick(new[] { gate, ramp }, C172, null, null, Always)!;

        Assert.Same(ramp, choice.Spot);
        Assert.Equal(StandChoiceSource.Category, choice.Source);
    }

    [Fact]
    public void A_code_A_aircraft_with_no_ramp_uses_a_gate_and_says_so()
    {
        var gate = Spot("G", 1, 10, 0, 0);
        var choice = BriefingStandPicker.Pick(new[] { gate }, C172, null, null, Always)!;

        Assert.Same(gate, choice.Spot);
        Assert.Contains(choice.Notes, n => n.Contains("no ramp stands at this airport; using a gate", StringComparison.Ordinal));
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

    [Fact]
    public void The_assigned_gate_label_is_trimmed()
        => Assert.Equal("Gate 6", SayIntentionsArrivalGate.From(Ctx(true, "EGLL", "KJFK", "  Gate 6 \t"), "EGLL", "KJFK")!.Label);

    // ── SayIntentionsArrivalGate: the parking-service fallback ─────────────────────────────

    private static SayIntentionsParking Parking(string? name, double? lat = null, double? lon = null) =>
        new() { Name = name, Latitude = lat, Longitude = lon };

    [Fact]
    public void The_flight_file_gate_wins_over_the_parking_service()
    {
        var ctx = Ctx(true, "KMEM", "KATL", "Terminal 1 Gate 17");
        ctx.AssignedGatePosition = new GeoPoint(33.64, -84.43);
        var hint = SayIntentionsArrivalGate.From(ctx, Parking("B 12", 35.04, -89.98), "KMEM", "KATL")!;

        Assert.Equal("Terminal 1 Gate 17", hint.Label);
        Assert.Equal(33.64, hint.Position!.Value.Latitude);
        Assert.Equal(SayIntentionsGateSource.FlightFile, hint.Source);
    }

    [Fact]
    public void With_no_gate_in_the_file_the_parking_service_gate_is_used_with_its_own_position()
    {
        // Live KMEM→KATL (2026-09-26): flight.json's assigned_gate was empty while SayIntentions showed a concourse B
        // arrival gate, served only by getParking — which MSFS Blind Assist's SayIntentions window already fell back to.
        var hint = SayIntentionsArrivalGate.From(Ctx(true, "KMEM", "KATL", ""), Parking(" B 12 ", 33.6407, -84.4277), "KMEM", "KATL")!;

        Assert.Equal("B 12", hint.Label);
        Assert.Equal(new GeoPoint(33.6407, -84.4277), hint.Position!.Value);
        Assert.Equal(SayIntentionsGateSource.ParkingService, hint.Source);
    }

    [Fact]
    public void A_parking_service_gate_never_takes_the_file_s_position()
    {
        var ctx = Ctx(true, "KMEM", "KATL", null);
        ctx.AssignedGatePosition = new GeoPoint(33.64, -84.43);   // a stray coordinate with no gate name beside it
        Assert.Null(SayIntentionsArrivalGate.From(ctx, Parking("B 12"), "KMEM", "KATL")!.Position);
    }

    [Fact]
    public void A_parking_service_position_at_zero_zero_is_no_position()
        => Assert.Null(SayIntentionsArrivalGate.From(Ctx(true, "KMEM", "KATL", null), Parking("B 12", 0, 0), "KMEM", "KATL")!.Position);

    [Fact]
    public void A_parking_service_position_missing_a_coordinate_is_no_position()
        => Assert.Null(SayIntentionsArrivalGate.From(Ctx(true, "KMEM", "KATL", null), Parking("B 12", 33.64, null), "KMEM", "KATL")!.Position);

    [Fact]
    public void No_gate_anywhere_means_no_hint()
    {
        Assert.Null(SayIntentionsArrivalGate.From(Ctx(true, "KMEM", "KATL", null), null, "KMEM", "KATL"));
        Assert.Null(SayIntentionsArrivalGate.From(Ctx(true, "KMEM", "KATL", " "), Parking("  "), "KMEM", "KATL"));
    }

    [Fact]
    public void Another_flight_s_parking_gate_is_ignored()
        => Assert.Null(SayIntentionsArrivalGate.From(Ctx(true, "KMEM", "KDFW", null), Parking("B 12"), "KMEM", "KATL"));

    [Fact]
    public void The_status_overload_reads_the_file_and_the_parking_service()
    {
        var status = new SayIntentionsStatusResult(Ctx(true, "KMEM", "KATL", null), Parking("B 12", 33.6407, -84.4277), null);
        Assert.Equal("B 12", SayIntentionsArrivalGate.FromStatus(status, "KMEM", "KATL")!.Label);
        Assert.Null(SayIntentionsArrivalGate.FromStatus(null, "KMEM", "KATL"));
    }

    [Fact]
    public void This_flight_means_the_file_exists_and_both_airports_match()
    {
        Assert.True(SayIntentionsArrivalGate.IsThisFlight(Ctx(true, "kmem ", "KATL", null), "KMEM", "KATL"));
        Assert.False(SayIntentionsArrivalGate.IsThisFlight(Ctx(false, "KMEM", "KATL", null), "KMEM", "KATL"));
        Assert.False(SayIntentionsArrivalGate.IsThisFlight(Ctx(true, "KMEM", "KDFW", null), "KMEM", "KATL"));
        Assert.False(SayIntentionsArrivalGate.IsThisFlight(Ctx(true, null, "KATL", null), "KMEM", "KATL"));
        Assert.False(SayIntentionsArrivalGate.IsThisFlight(null, "KMEM", "KATL"));
    }
}
