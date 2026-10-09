using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins <see cref="GsxNavdataGeometryFiller"/>: the heading and size GSX's Remote API leaves out
/// for a stand no GSX profile section covers, borrowed from the same navdata stand within 10 m
/// (same number and, when one exists, same suffix; an unnumbered stand from the lone unnumbered
/// row). The end-to-end case runs the REAL KSAN captures through the REAL reader, because that is
/// the defect: 75 of KSAN's 79 stands arrived with no heading and were dropped after touchdown.
/// </summary>
public class GsxNavdataGeometryFillerTests
{
    private const string Ksan = GsxKsanFixtures.Ksan;
    private const double Lat = 32.73188, Lon = -117.19246;

    private static ParkingSpot Api(string id, int number, double lat, double lon,
                                   double heading = double.NaN, double? maxWingspan = null,
                                   bool unconfigured = false, bool hasJetway = false, string airlineCodes = "",
                                   string suffix = "") => new()
    {
        AirportICAO = Ksan, GsxIdentifier = id, Number = number, Suffix = suffix, Latitude = lat, Longitude = lon,
        Heading = heading, MaxWingspanMeters = maxWingspan,
        Radius = maxWingspan.HasValue ? maxWingspan.Value / 2.0 : 100.0,   // the reader's own placeholder
        Source = GateSource.Gsx,
        GsxUnconfigured = unconfigured, HasJetway = hasJetway, AirlineCodes = airlineCodes,
    };

    private static ParkingSpot Nav(int number, double lat, double lon, double heading, double radiusFeet = 66.0,
                                   bool hasJetway = false, string airlineCodes = "", string suffix = "",
                                   GateSource source = GateSource.Navdata) => new()
    {
        AirportICAO = Ksan, Number = number, Suffix = suffix, Latitude = lat, Longitude = lon, Heading = heading,
        Radius = radiusFeet, Source = source, HasJetway = hasJetway, AirlineCodes = airlineCodes,
    };

    /// <summary>Offsets a coordinate due north by <paramref name="metres"/>.</summary>
    private static double LatPlusMetres(double lat, double metres) => lat + metres / 111_320.0;

    private static Func<IReadOnlyList<ParkingSpot>?> Navdata(params ParkingSpot[] spots) => () => spots;

    // ── Heading ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_heading_GSX_left_out_is_taken_from_the_same_numbered_navdata_stand()
    {
        var spot = Api("Ramp 115", 115, Lat, Lon, maxWingspan: 40.0);
        GsxNavdataGeometryFiller.Fill(new[] { spot }, Navdata(Nav(115, LatPlusMetres(Lat, 2.0), Lon, 196.1)));
        Assert.Equal(196.1, spot.Heading, 6);
    }

    [Fact]
    public void A_heading_GSX_published_is_never_replaced_and_navdata_is_never_read()
    {
        int reads = 0;
        var spot = Api("Gate N 1", 1, Lat, Lon, heading: 325.8, maxWingspan: 58.0);
        GsxNavdataGeometryFiller.Fill(new[] { spot }, () => { reads++; return new[] { Nav(1, Lat, Lon, 10.0) }; });
        Assert.Equal(325.8, spot.Heading);
        Assert.Equal(0, reads);
    }

    [Fact]
    public void A_differently_numbered_navdata_stand_never_donates()
    {
        var spot = Api("Ramp 115", 115, Lat, Lon, maxWingspan: 40.0);
        GsxNavdataGeometryFiller.Fill(new[] { spot }, Navdata(Nav(116, Lat, Lon, 196.1)));
        Assert.True(double.IsNaN(spot.Heading));
    }

    [Theory]
    [InlineData(9.5, true)]
    [InlineData(10.5, false)]
    public void The_match_radius_is_the_shared_10_metres(double metres, bool fills)
    {
        Assert.Equal(10.0, GsxNavdataGeometryFiller.MatchRadiusMetres);
        var spot = Api("Ramp 115", 115, Lat, Lon, maxWingspan: 40.0);
        GsxNavdataGeometryFiller.Fill(new[] { spot }, Navdata(Nav(115, LatPlusMetres(Lat, metres), Lon, 196.1)));
        Assert.Equal(fills, GsxRemoteParkingReader.HasUsableHeading(spot));
    }

    [Fact]
    public void In_range_navdata_stands_that_disagree_are_refused_not_arbitrated()
    {
        var spot = Api("Ramp 115", 115, Lat, Lon);
        GsxNavdataGeometryFiller.Fill(new[] { spot }, Navdata(
            Nav(115, LatPlusMetres(Lat, 2.0), Lon, 196.0),
            Nav(115, LatPlusMetres(Lat, 4.0), Lon, 16.0)));
        Assert.True(double.IsNaN(spot.Heading));
        Assert.Null(spot.MaxWingspanMeters);   // nothing at all is taken from a refused match
    }

    [Fact]
    public void In_range_navdata_stands_that_agree_donate_the_nearest_ones_heading()
    {
        var spot = Api("Ramp 115", 115, Lat, Lon, maxWingspan: 40.0);
        GsxNavdataGeometryFiller.Fill(new[] { spot }, Navdata(
            Nav(115, LatPlusMetres(Lat, 4.0), Lon, 197.0),
            Nav(115, LatPlusMetres(Lat, 2.0), Lon, 196.0)));
        Assert.Equal(196.0, spot.Heading, 6);
    }

    [Fact]
    public void In_range_navdata_stands_either_side_of_north_agree_across_the_wrap()
    {
        // 359 and 1 degrees are 2 degrees apart, not 358: pins the TaxiGeo.WrapDeltaDeg wrap. Refused, the
        // stand would stay NaN and be dropped after touchdown.
        var spot = Api("Ramp 115", 115, Lat, Lon, maxWingspan: 40.0);
        GsxNavdataGeometryFiller.Fill(new[] { spot }, Navdata(
            Nav(115, LatPlusMetres(Lat, 2.0), Lon, 359.0),
            Nav(115, LatPlusMetres(Lat, 4.0), Lon, 1.0)));
        Assert.Equal(359.0, spot.Heading, 6);   // the nearest one's, never an average
    }

    // ── Which in-range row is the SAME stand: the suffix, and unnumbered stands [DCK-42] ─────

    [Fact]
    public void A_MARS_child_takes_its_own_suffix_row_not_the_nearer_parent()
    {
        // The parent row "20" is nearer, but it is a different (wider) stand: the child must take
        // its own 59 ft radius, or the fitting-stands filter offers it to a wider aircraft.
        var spot = Api("Gate 20A", 20, Lat, Lon, suffix: "A");
        GsxNavdataGeometryFiller.Fill(new[] { spot }, Navdata(
            Nav(20, LatPlusMetres(Lat, 2.0), Lon, 196.0, radiusFeet: 89.0),
            Nav(20, LatPlusMetres(Lat, 4.0), Lon, 196.0, radiusFeet: 59.0, suffix: "A")));
        Assert.Equal(59.0 * 0.3048, spot.Radius, 6);
    }

    [Fact]
    public void MARS_siblings_that_point_different_ways_no_longer_refuse_the_exact_suffix_row()
    {
        // EGSS 15 and 15R sit 9.4 m apart and differ by 19.7 degrees. Comparing every in-range row
        // refused stand 15 outright, and DropUnusableHeadings then dropped it (53 such stands
        // worldwide in the fs2024 navdata).
        var spot = Api("Gate 15", 15, Lat, Lon);
        GsxNavdataGeometryFiller.Fill(new[] { spot }, Navdata(
            Nav(15, LatPlusMetres(Lat, 2.0), Lon, 100.0),
            Nav(15, LatPlusMetres(Lat, 6.0), Lon, 120.0, suffix: "R")));
        Assert.Equal(100.0, spot.Heading, 6);
    }

    [Fact]
    public void With_no_row_of_its_own_suffix_the_nearest_same_numbered_row_still_donates()
    {
        var spot = Api("Gate 7B", 7, Lat, Lon, suffix: "B");
        GsxNavdataGeometryFiller.Fill(new[] { spot }, Navdata(Nav(7, LatPlusMetres(Lat, 2.0), Lon, 50.0)));
        Assert.Equal(50.0, spot.Heading, 6);
    }

    [Fact]
    public void Two_rows_of_the_spots_own_suffix_that_disagree_are_still_refused()
    {
        var spot = Api("Gate 15", 15, Lat, Lon);
        GsxNavdataGeometryFiller.Fill(new[] { spot }, Navdata(
            Nav(15, LatPlusMetres(Lat, 2.0), Lon, 100.0),
            Nav(15, LatPlusMetres(Lat, 4.0), Lon, 200.0)));
        Assert.True(double.IsNaN(spot.Heading));
        Assert.Null(spot.MaxWingspanMeters);
    }

    [Fact]
    public void An_unnumbered_stand_borrows_from_the_one_unnumbered_navdata_row_in_range()
    {
        // KSFO's Northwest parking carries number 0. The navdata fallback listed it; GSX's list
        // must not lose it. Unnumbered stands sit a median 59.5 m apart, none within 14.5 m.
        var spot = Api("NW Parking", 0, Lat, Lon);
        GsxNavdataGeometryFiller.Fill(new[] { spot }, Navdata(Nav(0, LatPlusMetres(Lat, 3.0), Lon, 236.5, radiusFeet: 59.0)));
        Assert.Equal(236.5, spot.Heading, 6);
        Assert.Equal(2 * 59.0 * 0.3048, spot.MaxWingspanMeters!.Value, 6);
    }

    [Fact]
    public void An_unnumbered_stand_with_two_unnumbered_rows_in_range_is_refused()
    {
        // With no number to agree on, two candidates in range is ambiguity even when they agree.
        var spot = Api("NW Parking", 0, Lat, Lon);
        GsxNavdataGeometryFiller.Fill(new[] { spot }, Navdata(
            Nav(0, LatPlusMetres(Lat, 2.0), Lon, 236.5),
            Nav(0, LatPlusMetres(Lat, 6.0), Lon, 236.5)));
        Assert.True(double.IsNaN(spot.Heading));
    }

    [Fact]
    public void An_unnumbered_stand_never_borrows_from_a_numbered_row()
    {
        var spot = Api("NW Parking", 0, Lat, Lon);
        GsxNavdataGeometryFiller.Fill(new[] { spot }, Navdata(Nav(5, LatPlusMetres(Lat, 1.0), Lon, 90.0)));
        Assert.True(double.IsNaN(spot.Heading));
    }

    [Fact]
    public void A_row_that_is_not_navdata_sourced_never_donates()
    {
        // Radius is FEET on navdata and METRES on GSX (DCK-7): a GSX row would be shrunk 3.28 times.
        var spot = Api("Ramp 115", 115, Lat, Lon);
        GsxNavdataGeometryFiller.Fill(new[] { spot }, Navdata(
            Nav(115, LatPlusMetres(Lat, 2.0), Lon, 196.1, source: GateSource.Gsx)));
        Assert.True(double.IsNaN(spot.Heading));
        Assert.Equal(100.0, spot.Radius);
    }

    // ── Jet-bridge flag and airline codes: unconfigured stands only [DCK-44] ─────────────────

    [Fact]
    public void An_unconfigured_stand_takes_the_jet_bridge_flag_from_the_donor()
    {
        var spot = Api("Ramp 115", 115, Lat, Lon, unconfigured: true);
        GsxNavdataGeometryFiller.Fill(new[] { spot }, Navdata(Nav(115, LatPlusMetres(Lat, 2.0), Lon, 196.1, hasJetway: true)));
        Assert.True(spot.HasJetway);
        Assert.Contains("(Jetway)", spot.Describe());
    }

    [Fact]
    public void An_unconfigured_stand_whose_donor_has_no_jet_bridge_stays_without_one()
    {
        var spot = Api("N Parking 10", 10, Lat, Lon, unconfigured: true);
        GsxNavdataGeometryFiller.Fill(new[] { spot }, Navdata(Nav(10, Lat, Lon, 90.0, hasJetway: false)));
        Assert.False(spot.HasJetway);
        Assert.DoesNotContain("(Jetway)", spot.Describe());
    }

    [Fact]
    public void A_stand_GSX_configured_never_has_its_jet_bridge_flag_or_airline_codes_replaced()
    {
        // KJFK Gate 1A: GSX published hasJetway (false) and airlineCodes but not the heading. Only
        // the heading is borrowed; the donor's jet bridge and airlines are another source's opinion.
        var spot = Api("Gate 1A", 1, Lat, Lon, unconfigured: false, hasJetway: false, airlineCodes: "DAL");
        GsxNavdataGeometryFiller.Fill(new[] { spot }, Navdata(
            Nav(1, LatPlusMetres(Lat, 2.86), Lon, 196.1, hasJetway: true, airlineCodes: "UAL, AAL")));
        Assert.Equal(196.1, spot.Heading, 6);   // the gap is filled...
        Assert.False(spot.HasJetway);           // ...and nothing else is
        Assert.Equal("DAL", spot.AirlineCodes);
    }

    [Fact]
    public void An_unconfigured_stand_takes_the_donors_airline_codes_but_an_empty_donor_value_leaves_what_it_had()
    {
        var withCodes = Api("Ramp 115", 115, Lat, Lon, unconfigured: true);
        // Starts with codes of its own, so an unconditional assignment would blank them: the stand
        // already defaults to "", which could never show the "only when the donor has any" guard.
        var withoutCodes = Api("Ramp 116", 116, LatPlusMetres(Lat, 50.0), Lon, unconfigured: true, airlineCodes: "UAL");
        GsxNavdataGeometryFiller.Fill(new[] { withCodes, withoutCodes }, Navdata(
            Nav(115, Lat, Lon, 196.1, airlineCodes: "UAL, AAL"),
            Nav(116, LatPlusMetres(Lat, 50.0), Lon, 16.1, airlineCodes: "")));
        Assert.Equal("UAL, AAL", withCodes.AirlineCodes);
        Assert.Equal("UAL", withoutCodes.AirlineCodes);
    }

    [Fact]
    public void A_refused_match_gives_an_unconfigured_stand_no_jet_bridge_flag_either()
    {
        var spot = Api("Ramp 115", 115, Lat, Lon, unconfigured: true);
        GsxNavdataGeometryFiller.Fill(new[] { spot }, Navdata(
            Nav(115, LatPlusMetres(Lat, 2.0), Lon, 196.0, hasJetway: true),
            Nav(115, LatPlusMetres(Lat, 4.0), Lon, 16.0, hasJetway: true)));
        Assert.False(spot.HasJetway);
    }

    // ── Size ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_size_GSX_left_out_is_navdatas_radius_in_metres_and_fits_exactly_as_navdata_does()
    {
        var nav = Nav(115, LatPlusMetres(Lat, 2.0), Lon, 196.1, radiusFeet: 66.0);
        var spot = Api("Ramp 115", 115, Lat, Lon);   // no heading, no size
        GsxNavdataGeometryFiller.Fill(new[] { spot }, Navdata(nav));

        Assert.Equal(66.0 * 0.3048, spot.Radius, 6);                   // METRES on a GSX spot (DCK-7)
        Assert.Equal(2 * 66.0 * 0.3048, spot.MaxWingspanMeters!.Value, 6);
        // A320 (111.9 ft) fits, 135 ft does not (the stand holds a 132 ft span), nor a 777-300ER.
        foreach (double wingspanFeet in new[] { 111.9, 135.0, 212.6 })
            Assert.Equal(nav.FitsAircraft(wingspanFeet), spot.FitsAircraft(wingspanFeet));
    }

    [Fact]
    public void A_size_GSX_published_is_kept_while_the_heading_gap_is_still_filled()
    {
        var spot = Api("Ramp 115", 115, Lat, Lon, maxWingspan: 58.0);
        GsxNavdataGeometryFiller.Fill(new[] { spot }, Navdata(Nav(115, Lat, Lon, 196.1, radiusFeet: 66.0)));
        Assert.Equal(58.0, spot.MaxWingspanMeters);
        Assert.Equal(29.0, spot.Radius);
        Assert.Equal(196.1, spot.Heading, 6);
    }

    [Fact]
    public void A_navdata_stand_with_no_radius_donates_its_heading_but_no_size()
    {
        var spot = Api("Ramp 115", 115, Lat, Lon);
        GsxNavdataGeometryFiller.Fill(new[] { spot }, Navdata(Nav(115, Lat, Lon, 196.1, radiusFeet: 0.0)));
        Assert.Equal(196.1, spot.Heading, 6);
        Assert.Null(spot.MaxWingspanMeters);
        Assert.Equal(100.0, spot.Radius);
    }

    // ── The navdata read ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Navdata_is_read_once_however_many_stands_need_it()
    {
        int reads = 0;
        var a = Api("Ramp 115", 115, Lat, Lon);
        var b = Api("Ramp 116", 116, LatPlusMetres(Lat, 50.0), Lon);
        GsxNavdataGeometryFiller.Fill(new[] { a, b }, () =>
        {
            reads++;
            return new[] { Nav(115, Lat, Lon, 196.1), Nav(116, LatPlusMetres(Lat, 50.0), Lon, 16.1) };
        });
        Assert.Equal(1, reads);
        Assert.Equal(196.1, a.Heading, 6);
        Assert.Equal(16.1, b.Heading, 6);
    }

    [Fact]
    public void A_navdata_read_that_throws_leaves_every_stand_as_it_was()
    {
        var spot = Api("Ramp 115", 115, Lat, Lon);
        var result = GsxNavdataGeometryFiller.Fill(new[] { spot }, () => throw new InvalidOperationException("db locked"));
        Assert.Same(spot, Assert.Single(result));
        Assert.True(double.IsNaN(spot.Heading));
    }

    [Fact]
    public void Null_inputs_degrade_to_an_empty_or_unchanged_list()
    {
        Assert.Empty(GsxNavdataGeometryFiller.Fill(null, Navdata()));
        var spot = Api("Ramp 115", 115, Lat, Lon);
        Assert.Same(spot, Assert.Single(GsxNavdataGeometryFiller.Fill(new[] { spot, null! }, null)));
        Assert.True(double.IsNaN(spot.Heading));
    }

    // ── The real KSAN data ──────────────────────────────────────────────────────────────────

    [Fact]
    public void The_KSAN_navdata_fixture_names_its_stands_as_the_app_does()
    {
        // The concourse-letter filler reads navdata Name (single letters donate), so the fixture
        // must carry LittleNavMapProvider's mapped names ("GN" -> "N", "EP" -> "East"), not the raw
        // column, or the end-to-end tests never see the letter borrow production performs.
        var navdata = GsxKsanFixtures.Navdata();
        Assert.Contains(navdata, s => s.Number == 1 && s.Name == "N");
        Assert.Contains(navdata, s => s.Number == 1 && s.Name == "East");
        Assert.DoesNotContain(navdata, s => s.Name == "GN" || s.Name == "EP");
    }

    [Fact]
    public void Every_KSAN_stand_GSX_left_unconfigured_gets_its_heading_and_size_from_navdata()
    {
        var spots = GsxNavdataGeometryFiller.Fill(
            GsxRemoteParkingReader.Read(GsxKsanFixtures.GsxAirport(), Ksan),
            () => GsxKsanFixtures.Navdata());

        Assert.Equal(79, spots.Count);
        Assert.All(spots, s => Assert.True(GsxRemoteParkingReader.HasUsableHeading(s), s.GsxIdentifier));
        Assert.All(spots, s => Assert.NotNull(s.MaxWingspanMeters));

        var ramp115 = spots.Single(s => s.GsxIdentifier == "Ramp 115");   // SayIntentions' "Gate 115"
        Assert.Equal(196.08, ramp115.Heading, 2);
        Assert.Equal(66.0 * 0.3048, ramp115.Radius, 6);

        // The 4 stands the installed profile covers keep GSX's own values (normalized 0-360).
        var gateN1 = spots.Single(s => s.GsxIdentifier == "Gate N 1");
        Assert.Equal(325.79, gateN1.Heading, 2);
        Assert.Equal(58.0, gateN1.MaxWingspanMeters);
    }

    [Fact]
    public void KSAN_gates_with_a_jet_bridge_in_navdata_keep_it_after_the_fill_and_ramps_do_not_gain_one()
    {
        // Without this, 53 of KSAN's 75 recovered stands read "no jetway": the label loses
        // "(Jetway)" and docking says "Door on your left" for a jet bridge. At a profile-less
        // airport (KSFO) that would be a regression against the navdata list this replaces.
        var spots = GsxNavdataGeometryFiller.Fill(
            GsxRemoteParkingReader.Read(GsxKsanFixtures.GsxAirport(), Ksan),
            () => GsxKsanFixtures.Navdata());

        var ramp115 = spots.Single(s => s.GsxIdentifier == "Ramp 115");   // SayIntentions' "Gate 115"
        Assert.True(ramp115.GsxUnconfigured);
        Assert.True(ramp115.HasJetway);
        Assert.Contains("(Jetway)", ramp115.ToString());

        var parking10 = spots.Single(s => s.GsxIdentifier == "N Parking 10");   // a GA ramp: no jet bridge in navdata
        Assert.True(parking10.GsxUnconfigured);
        Assert.False(parking10.HasJetway);
        Assert.DoesNotContain("(Jetway)", parking10.ToString());

        // A sanity check only: Gate N 1 has a heading, so the filler never reads a donor for it. The
        // synthetic Gate 1A test above is the real guard that a configured stand keeps GSX's values.
        var gateN1 = spots.Single(s => s.GsxIdentifier == "Gate N 1");
        Assert.False(gateN1.GsxUnconfigured);
        Assert.False(gateN1.HasJetway);
    }
}
