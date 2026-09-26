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
        Assert.Contains(choice.Notes, n => n.Contains("\"Gate A24\"", StringComparison.Ordinal) && n.Contains("another name", StringComparison.Ordinal));
    }

    [Fact]
    public void Say_intentions_position_match_never_picks_an_excluded_type_or_deice_pad_and_names_the_label()
    {
        var fuel = Spot("F", 1, 16, 0, 0);
        var deice = Spot("D", 1, 10, 5, 0);
        deice.IsDeiceArea = true;
        var gate = Spot("G", 1, 10, 40, 0);   // 30 m from the point, inside its 61 m acceptance
        var choice = BriefingStandPicker.Pick(new[] { fuel, deice, gate }, B738, null, At("Gate 99", 10), Always)!;

        Assert.Same(gate, choice.Spot);
        Assert.Equal(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Contains(choice.Notes, n => n.Contains("\"Gate 99\"", StringComparison.Ordinal) && n.Contains("matched by position", StringComparison.Ordinal));
    }

    [Fact]
    public void Say_intentions_gate_listed_only_as_a_stand_the_briefing_skips_is_said_not_denied()
    {
        // LEBB: jetway stands 101-106 are navdata UNKN, which reads as type 1 and is never briefed. The
        // stand exists, so "not found" would be untrue, and the published position must not hand the
        // label to the gate next door (inside that gate's reach here).
        var unkn101 = Spot("", 101, 1, 0, 0, radiusFt: 33);
        unkn101.HasJetway = true;
        var gate6 = Spot("", 6, 10, 40, 0);
        var choice = BriefingStandPicker.Pick(new[] { unkn101, gate6 }, B738, null, At("Gate 101", 5), Always)!;

        Assert.Same(gate6, choice.Spot);
        Assert.NotEqual(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Contains(choice.Notes, n => n.Contains("\"Gate 101\" was found, but only as a stand the briefing does not route to", StringComparison.Ordinal));
        Assert.DoesNotContain(choice.Notes, n => n.Contains("not found", StringComparison.Ordinal) || n.Contains("matched by position", StringComparison.Ordinal));
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
        var gate9 = Spot("", 9, 9, 0, 0, radiusFt: 52);
        var spot9 = Spot("", 9, 4, 1200, 0, radiusFt: 39);
        var g1 = Spot("G", 1, 10, 600, 0);

        foreach (var hint in new[] { new SayIntentionsGateHint("Gate 9", null), At("Gate 9", 10) })
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

    // ── SayIntentions position acceptance ────────────────────────────────────────────────────

    [Fact]
    public void Position_match_reads_a_navdata_radius_in_feet()
    {
        // 100 ft = 30.5 m answers to 61 m; read as metres it would answer to 150 m.
        var b6 = Spot("B", 6, 10, 0, 0, radiusFt: 100);
        var choice = BriefingStandPicker.Pick(new[] { b6 }, B738, null, At("Gate 99", 70), Always)!;

        Assert.NotEqual(StandChoiceSource.SayIntentions, choice.Source);
        Assert.Contains(choice.Notes, n => n.Contains("not found", StringComparison.Ordinal));
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
}
