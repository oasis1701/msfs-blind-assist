// Characterization tests for how TaxiGraph.Build pairs `start` rows into
// RunwayCenterlines, and for the runway-designator helpers the fallback pass uses.
//
// Why the fallback exists: the original pass pairs two start rows when their stored
// headings are reciprocal (±15°). Some navdata builds get `start.heading` wrong —
// EGKK (fs2020) stores 08L and 26R BOTH at 257.6° and 08R and 26L both at ~168°, so
// nothing paired and the airport built ZERO centerlines. That is silent but expensive:
// no "on runway X" in Where-Am-I, hold-short names degrade to the nearest-threshold
// fallback format, and the auto runway-crossing hold-shorts never fire. Designators
// are reciprocal by definition, so the fallback pairs on the NAME and sanity-checks
// with the bearing between the two start points instead of the stored headings.
//
// Fixture idiom (shared with HoldingPointEntryTests / BacktrackEntryTests): a synthetic
// east-west runway on the equator, where the code's equirectangular constant
// (111132 m/deg, cos(0)=1) makes distances exactly metres = degrees × 111132. A runway
// laid from (0,0) to (0,0.027) is 3000 m long on a true bearing of 090°, so "09"/"27"
// are the honest designators for its two ends.
//
// Characterization, not spec: if a literal ever disagrees with real output, fix the
// test to match the output, not the other way around.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RunwayCenterlinePairingTests
{
    private const double FarLon = 0.027;   // 3000 m east of the origin

    // A minimal taxiway so the graph has nodes; centerline pairing reads only the
    // start rows, but Build's earlier passes walk the node set.
    private static List<TaxiPath> Paths() => new()
    {
        new TaxiPath { StartLat = 0.0006, StartLon = 0.006, EndLat = 0, EndLon = 0.006 },
    };

    private static StartPosition Start(string name, double heading, double lat, double lon) =>
        new() { RunwayName = name, Type = "R", Heading = heading, Latitude = lat, Longitude = lon };

    private static TaxiGraph Build(params StartPosition[] starts) =>
        TaxiGraph.Build(Paths(), new List<ParkingSpot>(), starts.ToList());

    // ---------------------------------------------------------------- designators

    [Theory]
    [InlineData("26L", "08R")]
    [InlineData("08R", "26L")]
    [InlineData("08", "26")]
    [InlineData("26", "08")]
    [InlineData("18C", "36C")]
    [InlineData("36", "18")]
    [InlineData("01", "19")]
    [InlineData("19L", "01R")]
    [InlineData("9", "27")]        // unpadded input still yields a padded designator
    [InlineData(" 26L ", "08R")]   // navdata names arrive padded often enough to matter
    public void Reciprocal_designator_wraps_the_number_and_swaps_the_side(string name, string expected)
    {
        Assert.Equal(expected, TaxiGraph.ReciprocalRunwayName(name));
    }

    [Theory]
    [InlineData("H1")]      // helipad
    [InlineData("W2")]      // water start
    [InlineData("")]
    [InlineData(null)]
    [InlineData("26LR")]    // two side letters is not a designator
    [InlineData("37")]      // out of the 1-36 space
    [InlineData("00")]
    [InlineData("123")]
    public void Non_runway_names_have_no_reciprocal(string? name)
    {
        Assert.Null(TaxiGraph.ReciprocalRunwayName(name));
    }

    [Fact]
    public void Designator_heading_is_the_number_times_ten_and_zero_when_unparseable()
    {
        Assert.Equal(260.0, TaxiGraph.RunwayDesignatorHeading("26L"));
        Assert.Equal(80.0, TaxiGraph.RunwayDesignatorHeading("08"));
        Assert.Equal(360.0, TaxiGraph.RunwayDesignatorHeading("36"));
        Assert.Equal(0.0, TaxiGraph.RunwayDesignatorHeading("H1"));
    }

    [Theory]
    [InlineData("26L", true)]
    [InlineData("08L/26R", true)]
    [InlineData("09/27", true)]
    [InlineData("36C-18C", true)]   // EHAM maps its runway hold lines dash-separated
    [InlineData("09-27", true)]
    [InlineData("A2", false)]
    [InlineData("N4", false)]
    [InlineData("VIKAS", false)]
    [InlineData("NO ENTRY", false)]
    [InlineData("", false)]
    [InlineData("A2/A3", false)]
    [InlineData("A2-A3", false)]
    public void Runway_designator_labels_are_told_apart_from_taxiway_holding_points(string label, bool expected)
    {
        Assert.Equal(expected, TaxiGraph.IsRunwayDesignatorLabel(label));
    }

    // ------------------------------------------------------------------- pairing

    [Fact]
    public void Sound_data_pairs_on_the_designator_pass_and_publishes_the_measured_heading()
    {
        var g = Build(
            Start("09R", 90.0, 0, 0),
            Start("27L", 270.0, 0, FarLon));

        var line = Assert.Single(g.RunwayCenterlines);
        Assert.Equal("09R", line.Name1);
        Assert.Equal("27L", line.Name2);
        // Designator-first, so the line carries the bearing between the two points rather
        // than the stored heading — the same number here, since this fixture is sound.
        Assert.Equal(90.0, line.HeadingDeg1, 1);
    }

    [Fact]
    public void Headings_that_mis_pair_across_runways_lose_to_the_designator()
    {
        // The LEMD shape: four ends whose stored headings read 0°/180° on runways that
        // point 322°/142°. On headings alone, 32R reads as the reciprocal of 18L and 32L of
        // 18R — two lines drawn diagonally across the airfield, and no correct line at all.
        // Pairing on designators first gets all four right.
        var g = Build(
            Start("14L", 140.0, 0.0, 0.0),
            Start("32R", 0.0, -0.019, 0.019),
            Start("18L", 180.0, 0.027, -0.004),
            Start("36R", 0.0, 0.0, -0.004));

        Assert.Equal(2, g.RunwayCenterlines.Count);
        foreach (var line in g.RunwayCenterlines)
            Assert.Equal(TaxiGraph.ReciprocalRunwayName(line.Name1), line.Name2);
    }

    [Fact]
    public void Wrong_start_headings_still_pair_by_reciprocal_designator()
    {
        // The EGKK shape: both rows carry the SAME bogus heading, so the ±15°
        // reciprocal test rejects every combination and the heading pass builds nothing.
        var g = Build(
            Start("09R", 168.0, 0, 0),
            Start("27L", 168.0, 0, FarLon));

        var line = Assert.Single(g.RunwayCenterlines);
        Assert.Equal("09R", line.Name1);
        Assert.Equal("27L", line.Name2);
        // The fallback distrusts the stored heading, so it publishes the MEASURED one.
        Assert.Equal(90.0, line.HeadingDeg1, 1);
    }

    [Fact]
    public void Parallel_ends_never_cross_pair_because_the_side_letter_disambiguates()
    {
        // Two parallels with useless headings. 09R's only valid partner is 27L and
        // 09L's is 27R — a name-blind pass could have crossed them.
        var g = Build(
            Start("09R", 168.0, 0, 0),
            Start("09L", 168.0, 0.0004, 0),
            Start("27R", 168.0, 0.0004, FarLon),
            Start("27L", 168.0, 0, FarLon));

        Assert.Equal(2, g.RunwayCenterlines.Count);
        foreach (var line in g.RunwayCenterlines)
        {
            Assert.Equal(TaxiGraph.ReciprocalRunwayName(line.Name1), line.Name2);
            // Each line stays on its own parallel: both ends share a latitude.
            Assert.Equal(line.Lat1, line.Lat2, 6);
        }
    }

    [Fact]
    public void Non_reciprocal_designators_do_not_pair()
    {
        // 09R's reciprocal is 27L, not 27R.
        var g = Build(
            Start("09R", 168.0, 0, 0),
            Start("27R", 168.0, 0, FarLon));

        Assert.Empty(g.RunwayCenterlines);
    }

    [Fact]
    public void Reciprocal_names_whose_geometry_disagrees_do_not_pair()
    {
        // Named 09/27 but laid out NORTH-south: the a→b bearing is 000°, 90° away from
        // what "09" claims. Pairing these would invent a centerline across open ground.
        var g = Build(
            Start("09R", 168.0, 0, 0),
            Start("27L", 168.0, 0.027, 0));

        Assert.Empty(g.RunwayCenterlines);
    }

    [Fact]
    public void Magnetic_variation_sized_skew_is_tolerated()
    {
        // The designator is magnetic while the measured bearing is true, so a pair whose
        // geometry runs ~20° off the designator must still bind.
        var g = Build(
            Start("09R", 168.0, 0, 0),
            Start("27L", 168.0, 0.0098, 0.0252));   // ~069° true, 21° off "09"

        var line = Assert.Single(g.RunwayCenterlines);
        Assert.Equal("09R", line.Name1);
    }

    [Fact]
    public void Separation_outside_the_sanity_band_does_not_pair()
    {
        // Same end (111 m apart) — below the 200 m floor.
        Assert.Empty(Build(
            Start("09R", 168.0, 0, 0),
            Start("27L", 168.0, 0, 0.001)).RunwayCenterlines);

        // 7780 m apart — above the 6000 m ceiling, so these are different runways.
        Assert.Empty(Build(
            Start("09R", 168.0, 0, 0),
            Start("27L", 168.0, 0, 0.07)).RunwayCenterlines);
    }

    [Fact]
    public void Helipad_and_water_starts_are_left_unpaired()
    {
        var g = Build(
            Start("H1", 168.0, 0, 0),
            Start("H2", 168.0, 0, FarLon));

        Assert.Empty(g.RunwayCenterlines);
    }

    // -------------------------------------------------- snapping bogus start rows
    //
    // EGKK (fs2020) stores three of its four start rows 99-122 m to the SIDE of their own
    // runway. That row feeds the route destination, the LINEUP TARGET a blind pilot steers
    // by, and the centerlines — at 26L it aimed the lineup ~109 m north of the pavement.
    // Every other airport probed (EGLL, EGCC, EGSS, EGGW, EHAM, LFPG, KJFK, KBOS, LEBL)
    // sits within 5 m, so the repair has to be a no-op on sound data.

    private const double North111M = 0.001;      // 111 m north of the equator centerline

    [Fact]
    public void A_start_row_on_the_centerline_is_returned_untouched()
    {
        var (lat, lon) = TaxiGraph.SnapStartToRunwayCenterline(
            0, 0.0009, 0, 0, 0, FarLon);

        Assert.Equal(0, lat, 9);
        Assert.Equal(0.0009, lon, 9);
    }

    [Fact]
    public void A_laterally_offset_row_keeps_its_along_track_position()
    {
        // The EGKK 26L shape: 111 m to the side, and 400 m BEHIND the landing threshold —
        // the starter extension, which is the whole reason the start table is consulted.
        var (lat, lon) = TaxiGraph.SnapStartToRunwayCenterline(
            North111M, -0.0036, 0, 0, 0, FarLon);

        Assert.Equal(0, lat, 6);           // pulled onto the line
        Assert.Equal(-0.0036, lon, 6);     // along-track preserved, still behind the threshold
    }

    [Fact]
    public void A_row_further_out_than_the_ceiling_is_left_alone()
    {
        // 333 m off is not an offset row, it is describing somewhere else — and we have no
        // basis to relocate it, so today's behavior stands.
        var (lat, lon) = TaxiGraph.SnapStartToRunwayCenterline(
            0.003, 0.001, 0, 0, 0, FarLon);

        Assert.Equal(0.003, lat, 9);
        Assert.Equal(0.001, lon, 9);
    }

    [Fact]
    public void A_row_past_midfield_is_left_alone_rather_than_dragged_back()
    {
        // 2222 m along a 3000 m runway. A departure never begins past midfield, so this row
        // is not this runway's start — it is the NAME-SWAPPED shape AYCH and URWW carry,
        // where the row labelled for one end physically sits at the other. Projecting it
        // put both of an airport's rows at midfield on top of each other, which then failed
        // the 200 m separation test and destroyed a centerline that used to exist.
        var (lat, lon) = TaxiGraph.SnapStartToRunwayCenterline(
            North111M, 0.02, 0, 0, 0, FarLon);

        Assert.Equal(North111M, lat, 9);
        Assert.Equal(0.02, lon, 9);
    }

    [Fact]
    public void Name_swapped_rows_keep_the_centerline_they_already_had()
    {
        // AYCH in miniature: each row sits at the OTHER end and carries that end's heading,
        // so the heading pass pairs them correctly today. The repair must not break that.
        var runways = new List<Runway>
        {
            new() { RunwayID = "09R", StartLat = 0, StartLon = 0, EndLat = 0, EndLon = FarLon },
            new() { RunwayID = "27L", StartLat = 0, StartLon = FarLon, EndLat = 0, EndLon = 0 },
        };
        var g = TaxiGraph.Build(
            Paths(), new List<ParkingSpot>(),
            new List<StartPosition>
            {
                Start("09R", 270.0, 0, FarLon - 0.0005),   // labelled 09R, sitting at the 27L end
                Start("27L", 90.0, 0, 0.0005),             // labelled 27L, sitting at the 09R end
            },
            runways);

        Assert.Single(g.RunwayCenterlines);
    }

    [Fact]
    public void A_degenerate_runway_line_leaves_the_row_alone()
    {
        var (lat, lon) = TaxiGraph.SnapStartToRunwayCenterline(
            North111M, 0.001, 0, 0, 0, 0);

        Assert.Equal(North111M, lat, 9);
        Assert.Equal(0.001, lon, 9);
    }

    // ------------------------------------------- choosing among duplicate start rows

    [Fact]
    public void The_full_length_row_is_the_one_furthest_back_not_the_first_listed()
    {
        // The EGLL 09R shape: the DB returns the 342 m row first, but a 67 m row exists.
        var rows = new List<StartPosition>
        {
            Start("09R", 90.0, 0, 0.00308),   // ~342 m in — listed first
            Start("09R", 90.0, 0, 0.0006),    // ~67 m in  — the real full-length point
        };

        var pick = TaxiGraph.PickFullLengthStart(rows, 0, 0, 0, FarLon);

        Assert.NotNull(pick);
        Assert.Equal(0.0006, pick!.Longitude, 6);
    }

    [Fact]
    public void A_row_behind_the_threshold_beats_one_on_it()
    {
        // Starter-extension rows project negative; those are further back still.
        var rows = new List<StartPosition>
        {
            Start("09R", 90.0, 0, 0.0005),
            Start("09R", 90.0, 0, -0.002),
        };

        Assert.Equal(-0.002, TaxiGraph.PickFullLengthStart(rows, 0, 0, 0, FarLon)!.Longitude, 6);
    }

    [Fact]
    public void A_single_row_is_returned_unchanged_and_an_empty_set_yields_null()
    {
        var only = Start("09R", 90.0, 0, 0.004);
        Assert.Same(only, TaxiGraph.PickFullLengthStart(new[] { only }, 0, 0, 0, FarLon));
        Assert.Null(TaxiGraph.PickFullLengthStart(Array.Empty<StartPosition>(), 0, 0, 0, FarLon));
    }

    [Fact]
    public void A_row_parked_at_the_runway_midpoint_is_rejected_outright()
    {
        // LatinVFR's LEMD parks 32R/32L/18L/18R at 47-48 % of runway length — the midpoint,
        // a placeholder never dragged to the threshold, with no taxiway within 220-660 m and
        // (on the 32s) a heading of 0° on a runway pointing 322°. Null sends the caller to the
        // pavement edge, which beats aiming the lineup mid-field.
        var rows = new List<StartPosition> { Start("09R", 90.0, 0, FarLon * 0.48) };

        Assert.Null(TaxiGraph.PickFullLengthStart(rows, 0, 0, 0, FarLon));
    }

    [Fact]
    public void A_row_inside_the_bar_is_still_accepted()
    {
        // 93.5 % of the 97,488 rows in the live navdata sit within the first 10 %; the bar has
        // to leave the long legitimate tail alone. 35 % is unusual but not broken.
        var rows = new List<StartPosition> { Start("09R", 90.0, 0, FarLon * 0.35) };

        Assert.NotNull(TaxiGraph.PickFullLengthStart(rows, 0, 0, 0, FarLon));
    }

    [Fact]
    public void A_good_row_rescues_a_runway_whose_other_row_is_parked_midfield()
    {
        // The bar applies to the BEST row, not each row: a usable one still wins.
        var rows = new List<StartPosition>
        {
            Start("09R", 90.0, 0, FarLon * 0.48),
            Start("09R", 90.0, 0, 0.0006),
        };

        var pick = TaxiGraph.PickFullLengthStart(rows, 0, 0, 0, FarLon);
        Assert.NotNull(pick);
        Assert.Equal(0.0006, pick!.Longitude, 6);
    }

    [Fact]
    public void The_fraction_bar_is_not_applied_to_a_tiny_strip()
    {
        // On a runway under 100 m the fraction is meaningless — keep whatever row exists.
        var rows = new List<StartPosition> { Start("09R", 90.0, 0, 0.0006) };

        Assert.NotNull(TaxiGraph.PickFullLengthStart(rows, 0, 0, 0, 0.0008));
    }

    [Fact]
    public void The_pick_ignores_lateral_offset_and_ranks_on_along_track_only()
    {
        // Lateral error is SnapStartToRunwayCenterline's business; a badly-offset row that is
        // further back is still the full-length one.
        var rows = new List<StartPosition>
        {
            Start("09R", 90.0, 0, 0.0009),          // on the centerline, 100 m in
            Start("09R", 90.0, North111M, 0.0002),  // 111 m to the side, but only 22 m in
        };

        Assert.Equal(0.0002, TaxiGraph.PickFullLengthStart(rows, 0, 0, 0, FarLon)!.Longitude, 6);
    }

    // ------------------------------------- the holding-point measuring window
    //
    // Navdata disagrees with itself about where a runway begins, in BOTH directions:
    // EGKK 26L departs 406 m BEYOND its runway_end on a starter extension, while EGLL 09R
    // and EHAM 36C put the start row 45 m and 480 m INTO the runway. Anchoring on either
    // source alone loses holding points at the airports the other describes — measured
    // against live navdata + OSM, the lineup anchor cost EGLL 09R its N1/NB1, EGLL 27L its
    // N8/NB8, and EHAM 36C its CAT III hold. The outer envelope loses nothing by
    // construction.

    private const double Back400M = -0.0036;   // 400 m behind the threshold
    private const double In500M = 0.0045;      // 500 m into the runway

    [Fact]
    public void A_departure_point_behind_the_pavement_edge_pulls_the_window_back()
    {
        // The EGKK 26L shape.
        var w = TaxiGraph.ChooseHoldingPointExtent(
            0, 0, 0, FarLon, 0, Back400M, 0, FarLon);

        Assert.Equal(Back400M, w.ThrLon, 6);   // window starts at the departure point
        Assert.Equal(FarLon, w.FarLon, 6);
        Assert.Equal(0.0, w.LineupAlong, 1);   // ...which IS the lineup point
    }

    [Fact]
    public void A_departure_point_inside_the_runway_leaves_the_window_at_the_edge()
    {
        // The EGLL 09R / EHAM 36C shape: keep the full-length holds that sit behind it.
        var w = TaxiGraph.ChooseHoldingPointExtent(
            0, 0, 0, FarLon, 0, In500M, 0, FarLon);

        Assert.Equal(0.0, w.ThrLon, 6);        // window still starts at the pavement edge
        Assert.Equal(500.0, w.LineupAlong, 0); // but the departure point is 500 m in
    }

    [Fact]
    public void The_far_end_extends_to_whichever_candidate_reaches_further()
    {
        double beyond = FarLon + 0.003;        // reciprocal departs 333 m past the far edge
        var w = TaxiGraph.ChooseHoldingPointExtent(
            0, 0, 0, FarLon, 0, 0.0005, 0, beyond);
        Assert.Equal(beyond, w.FarLon, 6);

        // ...and never contracts when the reciprocal's point is short of the edge.
        var w2 = TaxiGraph.ChooseHoldingPointExtent(
            0, 0, 0, FarLon, 0, 0.0005, 0, FarLon - 0.004);
        Assert.Equal(FarLon, w2.FarLon, 6);
    }

    [Fact]
    public void With_no_reciprocal_point_the_window_degrades_to_the_pavement_edges()
    {
        // Callers pass the far edge for both far arguments when the opposite end isn't listed.
        var w = TaxiGraph.ChooseHoldingPointExtent(
            0, 0, 0, FarLon, 0, 0.0005, 0, FarLon);

        Assert.Equal(0.0, w.ThrLon, 6);
        Assert.Equal(FarLon, w.FarLon, 6);
    }

    [Fact]
    public void The_window_never_reports_a_negative_lineup_position()
    {
        // When the window has been pulled back to the lineup point, "along" is zero, never
        // a small negative from floating-point drift — the caller adds a margin to it.
        var w = TaxiGraph.ChooseHoldingPointExtent(
            0, 0, 0, FarLon, 0, Back400M, 0, FarLon);
        Assert.True(w.LineupAlong >= 0);
    }

    [Fact]
    public void Both_ends_can_extend_at_once()
    {
        double beyond = FarLon + 0.003;
        var w = TaxiGraph.ChooseHoldingPointExtent(
            0, 0, 0, FarLon, 0, Back400M, 0, beyond);

        Assert.Equal(Back400M, w.ThrLon, 6);
        Assert.Equal(beyond, w.FarLon, 6);
    }

    [Fact]
    public void Build_snaps_offset_start_rows_when_a_runway_table_is_supplied()
    {
        var runways = new List<Runway>
        {
            new() { RunwayID = "09R", StartLat = 0, StartLon = 0, EndLat = 0, EndLon = FarLon },
            new() { RunwayID = "27L", StartLat = 0, StartLon = FarLon, EndLat = 0, EndLon = 0 },
        };
        var g = TaxiGraph.Build(
            Paths(), new List<ParkingSpot>(),
            new List<StartPosition>
            {
                Start("09R", 90.0, North111M, 0.0005),
                Start("27L", 270.0, North111M, FarLon - 0.0005),
            },
            runways);

        var line = Assert.Single(g.RunwayCenterlines);
        Assert.Equal(0, line.Lat1, 6);
        Assert.Equal(0, line.Lat2, 6);
        Assert.Equal(0.0005, line.Lon1, 6);
    }

    [Fact]
    public void Build_without_a_runway_table_leaves_start_rows_exactly_as_given()
    {
        var g = TaxiGraph.Build(
            Paths(), new List<ParkingSpot>(),
            new List<StartPosition>
            {
                Start("09R", 90.0, North111M, 0.0005),
                Start("27L", 270.0, North111M, FarLon - 0.0005),
            });

        var line = Assert.Single(g.RunwayCenterlines);
        Assert.Equal(North111M, line.Lat1, 9);
        Assert.Equal(North111M, line.Lat2, 9);
    }

    [Fact]
    public void A_start_row_with_no_matching_runway_is_passed_through()
    {
        // A runway table that doesn't cover this designator must not drop the row.
        var g = TaxiGraph.Build(
            Paths(), new List<ParkingSpot>(),
            new List<StartPosition>
            {
                Start("09R", 90.0, North111M, 0.0005),
                Start("27L", 270.0, North111M, FarLon - 0.0005),
            },
            new List<Runway> { new() { RunwayID = "18", StartLat = 0, StartLon = 0, EndLat = 0.01, EndLon = 0 } });

        var line = Assert.Single(g.RunwayCenterlines);
        Assert.Equal(North111M, line.Lat1, 9);
    }

    [Fact]
    public void A_start_row_is_used_by_at_most_one_centerline()
    {
        // Three rows where two are valid partners for the same end; the pairing must not
        // emit the shared row twice.
        var g = Build(
            Start("09R", 168.0, 0, 0),
            Start("27L", 168.0, 0, FarLon),
            Start("27L", 168.0, 0, FarLon + 0.001));

        Assert.Single(g.RunwayCenterlines);
    }
}
