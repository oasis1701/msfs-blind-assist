using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Tests;

public class SurroundingsReportTests
{
    // Own ship at the origin of a flat local frame: 0.0009° lat ≈ 100 m, 0.0009° lon ≈ 100 m at lat 0.
    private const double Lat = 0.0, Lon = 0.0;
    private static string Metres(double m) => $"{Math.Round(m / 10) * 10} metres";

    private static AirportFeature F(FeatureKind k, string name, double dLatMetres, double dLonMetres, FeatureSource src = FeatureSource.Osm, IReadOnlyList<LatLon>? fp = null)
        => new() { Kind = k, Name = name, Lat = Lat + dLatMetres / 111_320.0, Lon = Lon + dLonMetres / 111_320.0, Source = src, Footprint = fp };

    private static AirportFeatureCatalog Cat(params AirportFeature[] fs) => AirportFeatureCatalog.Build("KTIW", "v", fs);

    [Fact]
    public void Compose_leads_with_where_am_i_then_nearest_features_with_direction_and_distance()
    {
        var cat = Cat(
            F(FeatureKind.Tower, "Control Tower", 200, 0),          // north, heading north → ahead
            F(FeatureKind.Fbo, "Narrows Aviation", 0, 100),         // east → to the right
            F(FeatureKind.Fuel, "Fuel", -150, -150));               // south-west → behind and to the left
        string s = SurroundingsReport.Compose("Taxiway A at KTIW.", "KTIW", cat, Lat, Lon, 0.0, Metres);
        Assert.Equal("Taxiway A at KTIW. Narrows Aviation, to the right, 100 metres. Control Tower, ahead, 200 metres. Fuel, behind and to the left, 210 metres.", s);
    }

    [Fact]
    public void Compose_names_the_zone_and_excludes_it_from_the_list()
    {
        var square = new[] { new LatLon(-0.0005, -0.0005), new LatLon(-0.0005, 0.0005), new LatLon(0.0005, 0.0005), new LatLon(0.0005, -0.0005) };
        var cat = Cat(F(FeatureKind.Apron, "Commercial Ramp", 0, 0, fp: square), F(FeatureKind.Terminal, "General Aviation Terminal", 0, 80));
        string s = SurroundingsReport.Compose("Not on a known taxiway or ramp at KJAC.", "KJAC", cat, Lat, Lon, 0.0, Metres);
        Assert.Equal("Not on a known taxiway or ramp at KJAC. On the Commercial Ramp. General Aviation Terminal, to the right, 80 metres.", s);
    }

    [Fact]
    public void Zone_falls_back_to_a_concourse_within_120m()
    {
        var cat = Cat(F(FeatureKind.Concourse, "Concourse B", 100, 0));
        Assert.Equal("Concourse B", SurroundingsReport.Zone(cat, Lat, Lon)?.Name);
        Assert.Null(SurroundingsReport.Zone(Cat(F(FeatureKind.Concourse, "Concourse B", 130, 0)), Lat, Lon));
        string s = SurroundingsReport.Compose("Gate B2 at KATL.", "KATL", cat, Lat, Lon, 0.0, Metres);
        Assert.StartsWith("Gate B2 at KATL. At Concourse B.", s);
    }

    [Fact]
    public void Compose_caps_at_four_and_one_per_kind_except_hangars_and_fbos()
    {
        var cat = Cat(
            F(FeatureKind.Fuel, "Fuel", 50, 0), F(FeatureKind.Fuel, "Fuel", -60, 0),          // 110 m apart: survives the merge, dropped by one-per-kind
            F(FeatureKind.Hangar, "Hangar 1", 70, 0), F(FeatureKind.Hangar, "Hangar 2", 0, 80), // 106 m apart: both survive
            F(FeatureKind.Tower, "Control Tower", 90, 0), F(FeatureKind.Cargo, "Cargo ramp", 95, 0));
        string s = SurroundingsReport.Compose("X.", "X", cat, Lat, Lon, 0.0, Metres);
        Assert.Equal("X. Fuel, ahead, 50 metres. Hangar 1, ahead, 70 metres. Hangar 2, to the right, 80 metres. Control Tower, ahead, 90 metres.", s);
    }

    [Fact]
    public void Two_unnamed_hangars_collapse_to_hangars_at_the_nearer_distance()
    {
        var cat = Cat(F(FeatureKind.Hangar, "", 0, -80), F(FeatureKind.Hangar, "", 0, -140));
        string s = SurroundingsReport.Compose("X.", "X", cat, Lat, Lon, 0.0, Metres);
        Assert.Equal("X. Hangars, to the left, 80 metres.", s);
    }

    [Fact]
    public void Compose_reports_empty_range_and_missing_catalog()
    {
        Assert.Equal("X. Nothing within 600 metres.", SurroundingsReport.Compose("X.", "X", Cat(F(FeatureKind.Tower, "T", 700, 0)), Lat, Lon, 0.0, Metres));
        Assert.Equal("X. No surroundings data for KXYZ.", SurroundingsReport.Compose("X.", "KXYZ", null, Lat, Lon, 0.0, Metres));
        Assert.Equal("X. No surroundings data for KXYZ.", SurroundingsReport.Compose("X.", "KXYZ", AirportFeatureCatalog.Build("KXYZ", "", Array.Empty<AirportFeature>()), Lat, Lon, 0.0, Metres));
    }

    [Fact]
    public void Sections_carry_facts_first_then_everything_within_1km_nearest_first_with_detail()
    {
        var cat = Cat(
            new AirportFeature { Kind = FeatureKind.Concourse, Name = "Concourse B", Lat = Lat + 300 / 111_320.0, Lon = Lon, Source = FeatureSource.Navdata, Detail = "Delta gates" },
            F(FeatureKind.Tower, "Control Tower", 0, 150), F(FeatureKind.Hangar, "Far Hangar", 1200, 0));
        var sections = SurroundingsReport.BuildSections("KATL", cat, "Avgas. Tower 118.5.", Lat, Lon, 0.0, Metres);
        Assert.Equal("Airport", sections[0].Heading);
        Assert.Equal(new[] { "Avgas. Tower 118.5." }, sections[0].Items);
        Assert.Equal("Nearby, 2 items", sections[1].Heading);
        Assert.Equal(new[] { "Control Tower, to the right, 150 metres", "Concourse B, Delta gates, ahead, 300 metres" }, sections[1].Items);
    }

    // NOTE: the file's existing Metres(double) helper above (rounds to the nearest 10) is reused
    // here instead of redeclaring one that rounds to whole metres — the two new tests below never
    // assert an exact formatted distance other than 1000 (a multiple of 10, where both round the
    // same), so a second same-signature Metres would only be a compile-time duplicate.
    private static AirportFeature Pt(FeatureKind k, string name, double lat, double lon)
        => new() { Kind = k, Name = name, Lat = lat, Lon = lon, Source = FeatureSource.Navdata };

    [Fact]
    public void Several_unnamed_hangars_read_as_Hangars_even_when_the_cap_is_reached_first()
    {
        var cat = AirportFeatureCatalog.Build("X", "v", new[]
        {
            Pt(FeatureKind.Tower, "Control Tower", 0.0005, 0), Pt(FeatureKind.Fuel, "Avfuel", 0.0010, 0),
            Pt(FeatureKind.Cargo, "FedEx", 0.0015, 0), Pt(FeatureKind.Hangar, "", 0.0020, 0), Pt(FeatureKind.Hangar, "", 0.0030, 0),
        });
        string said = SurroundingsReport.Compose("On taxiway A at X.", "X", cat, 0, 0, 0, Metres);
        Assert.Contains("Hangars,", said);          // two in range: plural, although only one slot was left
        Assert.DoesNotContain("Hangar,", said);
    }

    // ---- Final review follow-up: the ramp you are ON is the zone, and zero range has no side ----

    [Fact]
    public void A_named_apron_you_are_inside_outranks_a_stand_cluster_you_are_standing_at()
    {
        // Both rungs are live at once: the aircraft is inside the named ring AND on one of the
        // cluster's stands. The NAME is what a pilot can act on, so the ring wins. (The pair does
        // not merge: the cluster's far stand is 200 m out, well past Apron's same-name radius, so
        // the cluster does not describe the ring — see SameFeature.)
        var ring = new[] { new LatLon(-0.0005, -0.0005), new LatLon(-0.0005, 0.0005), new LatLon(0.0005, 0.0005), new LatLon(0.0005, -0.0005) };
        var cat = Cat(F(FeatureKind.Apron, "South Apron", 0, 0, fp: ring),
                      new AirportFeature { Kind = FeatureKind.Apron, Name = "GA ramp", NameIsGeneric = true, Source = FeatureSource.Navdata,
                                           Lat = Lat + 100 / 111_320.0, Lon = Lon,
                                           Members = new[] { new LatLon(Lat, Lon), new LatLon(Lat + 200 / 111_320.0, Lon) } });
        Assert.Equal(2, cat.Features.Count);
        Assert.Equal("South Apron", SurroundingsReport.Zone(cat, Lat, Lon)?.Name);
        // …and the ramp underfoot is never ALSO offered as somewhere nearby.
        string s = SurroundingsReport.Compose("X.", "X", cat, Lat, Lon, 0.0, Metres);
        Assert.Equal("X. On the South Apron. Nothing within 600 metres.", s);
    }

    [Fact]
    public void A_stand_cluster_you_are_at_is_the_zone_when_no_named_apron_contains_you()
    {
        var cluster = new AirportFeature { Kind = FeatureKind.Apron, Name = "GA ramp", NameIsGeneric = true, Source = FeatureSource.Navdata,
                                           Lat = Lat, Lon = Lon, Members = new[] { new LatLon(Lat, Lon) } };
        Assert.Equal("GA ramp", SurroundingsReport.Zone(Cat(cluster), Lat, Lon)?.Name);
        // …but only while the aircraft is AT it: one stand spacing out, there is nothing to be on.
        var far = new AirportFeature { Kind = FeatureKind.Apron, Name = "GA ramp", NameIsGeneric = true, Source = FeatureSource.Navdata,
                                       Lat = Lat, Lon = Lon,
                                       Members = new[] { new LatLon(Lat + (SurroundingsReport.ZoneMemberMetres + 5) / 111_320.0, Lon) } };
        Assert.Null(SurroundingsReport.Zone(Cat(far), Lat, Lon));
    }

    /// <summary>A ramp of `count` stands running north from `dLatMetres`, 30 m apart.</summary>
    private static AirportFeature Ramp(string name, double dLatMetres, int count = 2)
        => new() { Kind = FeatureKind.Apron, Name = name, NameIsGeneric = name == "GA ramp", Source = FeatureSource.Navdata,
                   Lat = Lat + dLatMetres / 111_320.0, Lon = Lon,
                   Members = Enumerable.Range(0, count).Select(i => new LatLon(Lat + (dLatMetres + i * 30) / 111_320.0, Lon)).ToList() };

    [Fact]
    public void A_differently_named_ramp_nearby_is_still_named_under_an_anonymous_zone()
    {
        // The zone is the unnamed OSM polygon the aircraft is inside ("On the Apron."), which says
        // nothing about a REAL ramp 300 m away. Spending the whole Apron kind on an anonymous zone
        // silenced the one feature in range a pilot could actually have asked for by name.
        var ring = new[] { new LatLon(-0.0005, -0.0005), new LatLon(-0.0005, 0.0005), new LatLon(0.0005, 0.0005), new LatLon(0.0005, -0.0005) };
        var cat = Cat(F(FeatureKind.Apron, "", 0, 0, fp: ring), Ramp("North Apron", 300));
        string s = SurroundingsReport.Compose("X.", "X", cat, Lat, Lon, 0.0, Metres);
        Assert.Contains("On the Apron.", s);
        Assert.Contains("North Apron, ahead, 300 metres.", s);
    }

    [Fact]
    public void A_ramp_sharing_the_zones_name_is_the_one_that_is_skipped()
    {
        // KTIW in miniature: two navdata ramps 600 m apart, both called "GA ramp". The zone names
        // the one underfoot, so naming the other one — same words, different place — is the
        // one-name-two-places confusion. A neighbour with a name of its own is unaffected.
        var cat = Cat(Ramp("GA ramp", 0), Ramp("GA ramp", 400), Ramp("South Apron", 250));
        string s = SurroundingsReport.Compose("X.", "X", cat, Lat, Lon, 0.0, Metres);
        Assert.Equal("X. On the GA ramp. South Apron, ahead, 250 metres.", s);
    }

    [Fact]
    public void Beside_the_ramp_you_are_on_only_a_NAMED_apron_is_worth_a_slot()
    {
        // The anonymous polygon 12 m away is the pavement the ramp belongs to: it names nothing a
        // pilot can act on and it spends one of the four slots a building should have. A place with
        // a name of its own is different, however far off.
        var ring = new[] { new LatLon(10 / 111_320.0, -0.0003), new LatLon(10 / 111_320.0, 0.0003),
                           new LatLon(40 / 111_320.0, 0.0003), new LatLon(40 / 111_320.0, -0.0003) };
        var cat = Cat(Ramp("GA ramp", 0), F(FeatureKind.Apron, "", 25, 0, fp: ring), Ramp("North Apron", 300));
        string s = SurroundingsReport.Compose("X.", "X", cat, Lat, Lon, 0.0, Metres);
        Assert.Equal("X. On the GA ramp. North Apron, ahead, 300 metres.", s);
    }

    [Fact]
    public void A_synthesized_name_that_still_DISTINGUISHES_the_ramp_is_spoken_beside_the_zone()
    {
        // NavdataFeatureSource labels a directional ramp "North ramp" and marks it NameIsGeneric,
        // so it has no PROPER name — but it is what a controller calls that pavement, and a pilot
        // can act on it. The no-name half of the rule is about ANONYMOUS apron pavement, not about
        // a synthesized label that names one ramp rather than all of them.
        var north = new AirportFeature { Kind = FeatureKind.Apron, Name = "North ramp", NameIsGeneric = true, Source = FeatureSource.Navdata,
                                         Lat = Lat + 300 / 111_320.0, Lon = Lon, Members = new[] { new LatLon(Lat + 300 / 111_320.0, Lon) } };
        Assert.True(north.HasName);
        Assert.False(north.HasProperName);
        var cat = Cat(Ramp("GA ramp", 0), north);
        Assert.Equal("X. On the GA ramp. North ramp, ahead, 300 metres.",
                     SurroundingsReport.Compose("X.", "X", cat, Lat, Lon, 0.0, Metres));
    }

    [Fact]
    public void An_unnamed_de_ice_pad_beside_the_ramp_is_spoken_because_its_KIND_is_the_information()
    {
        // "De-ice pad, ahead, 200 metres" is worth a slot with no name at all — the kind word is
        // what a pilot wanted. "Apron" is not: beside the ramp you are parked on it is the pavement
        // that ramp belongs to.
        var cat = Cat(Ramp("GA ramp", 0), F(FeatureKind.DeicePad, "", 200, 0));
        Assert.Equal("X. On the GA ramp. De-ice pad, ahead, 200 metres.",
                     SurroundingsReport.Compose("X.", "X", cat, Lat, Lon, 0.0, Metres));
    }

    [Fact]
    public void Away_from_any_ramp_an_unnamed_apron_is_real_information_and_is_spoken()
    {
        // Out on a taxiway there is no ground zone, so nothing has been said about the pavement
        // and "Apron, ahead, 200 metres" is the readout doing its job.
        var cat = Cat(F(FeatureKind.Apron, "", 200, 0));
        Assert.Null(SurroundingsReport.Zone(cat, Lat, Lon));
        Assert.Equal("X. Apron, ahead, 200 metres.", SurroundingsReport.Compose("X.", "X", cat, Lat, Lon, 0.0, Metres));
    }

    [Fact]
    public void Nothing_at_zero_range_is_ever_given_a_direction()
    {
        // A hangar the aircraft is inside: the bearing to it is degenerate and the distance rounds
        // to zero in both units, so "Cessna Hangar, ahead, 0 metres" was a side that meant nothing
        // and a number that said nothing.
        var ring = new[] { new LatLon(-0.0003, -0.0003), new LatLon(-0.0003, 0.0003), new LatLon(0.0003, 0.0003), new LatLon(0.0003, -0.0003) };
        var cat = Cat(F(FeatureKind.Hangar, "Cessna Hangar", 0, 0, fp: ring));
        Assert.Equal("X. Cessna Hangar, here.", SurroundingsReport.Compose("X.", "X", cat, Lat, Lon, 0.0, Metres));

        var sections = SurroundingsReport.BuildSections("X", cat, "", Lat, Lon, 0.0, Metres);
        Assert.Equal(new[] { "Cessna Hangar, here" }, Assert.Single(sections).Items);
    }

    [Fact]
    public void A_feature_just_past_the_zero_range_floor_still_gets_its_direction_and_distance()
    {
        var cat = Cat(F(FeatureKind.Hangar, "Cessna Hangar", SurroundingsReport.ZeroRangeMetres + 1.0, 0));
        Assert.Contains("Cessna Hangar, ahead,", SurroundingsReport.Compose("X.", "X", cat, Lat, Lon, 0.0, Metres));
    }

    [Fact]
    public void The_window_never_has_an_empty_list_and_never_hard_codes_a_unit()
    {
        var far = AirportFeatureCatalog.Build("X", "v", new[] { Pt(FeatureKind.Tower, "Control Tower", 0.5, 0.5) });   // ~78 km away
        Assert.Empty(SurroundingsReport.BuildSections("X", far, "", 0, 0, 0, Metres));                                  // nothing to show: the caller SPEAKS instead

        var withFacts = SurroundingsReport.BuildSections("X", far, "Tower 118.5.", 0, 0, 0, Metres);
        Assert.Equal(2, withFacts.Count);
        Assert.All(withFacts, s => Assert.NotEmpty(s.Items));
        Assert.Equal("Nothing within 1000 metres.", Assert.Single(withFacts[1].Items));
        Assert.DoesNotContain("kilometre", string.Join(" ", withFacts.SelectMany(s => s.Items).Concat(withFacts.Select(s => s.Heading))));
    }
}
