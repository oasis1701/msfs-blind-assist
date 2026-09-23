using System.Text.Json;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.Surroundings;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Tests;

public class OsmFeatureClassifierTests
{
    private static AirportFeature? One(string elementJson)
    {
        using var doc = JsonDocument.Parse(elementJson);
        return OsmFeatureClassifier.Classify(doc.RootElement);
    }
    // Minimal elements: tag sets are REAL (copied from live EGLL/EDDF/KJAC responses); only the
    // position is reduced to a node, because these tests pin TAG rules, not geometry.
    private static string Node(string tags) => "{\"type\":\"node\",\"id\":1,\"lat\":51.47,\"lon\":-0.45,\"tags\":{" + tags + "}}";

    [Theory]
    [InlineData("\"building\":\"yes\",\"name\":\"Genesis Car Park\"")]
    [InlineData("\"building\":\"yes\",\"name\":\"Heathrow Terminal 5 short stay car park\"")]
    [InlineData("\"building\":\"yes\",\"name\":\"Heathrow Central Bus station\"")]
    [InlineData("\"building\":\"yes\",\"name\":\"North Escape Shaft\"")]
    [InlineData("\"building\":\"yes\",\"name\":\"Hapeville City Hall\"")]
    [InlineData("\"building\":\"yes\",\"name\":\"Executive Car Park\"")]
    [InlineData("\"office\":\"company\",\"name\":\"Premia\"")]
    public void A_named_building_that_is_not_aviation_is_not_a_feature(string tags) => Assert.Null(One(Node(tags)));

    [Fact]
    public void Road_fuel_is_not_aircraft_fuel_but_aeroway_fuel_is()
    {
        Assert.Null(One(Node("\"amenity\":\"fuel\",\"name\":\"Chevron\",\"brand\":\"Chevron\"")));
        Assert.Null(One(Node("\"amenity\":\"fuel\",\"building\":\"roof\",\"name\":\"120 Betriebstankstelle\"")));
        Assert.Equal(FeatureKind.Fuel, One(Node("\"aeroway\":\"fuel\""))!.Kind);
    }

    [Fact]
    public void A_named_building_matching_the_fbo_or_cargo_lexicon_is_kept()
    {
        var fbo = One(Node("\"building\":\"yes\",\"name\":\"Signature Flight Support\""))!;
        Assert.Equal(FeatureKind.Fbo, fbo.Kind);
        Assert.Equal("Signature Flight Support", fbo.Name);
        Assert.Equal(FeatureKind.Cargo, One(Node("\"office\":\"company\",\"name\":\"IAG Cargo Head Office\""))!.Kind);
    }

    [Fact]
    public void A_bare_reference_number_is_never_a_name()
    {
        var hangar = One(Node("\"building\":\"hangar\",\"ref\":\"117\""))!;
        Assert.Equal(FeatureKind.Hangar, hangar.Kind);
        Assert.False(hangar.HasName);
        Assert.False(One(Node("\"aeroway\":\"terminal\",\"ref\":\"222;223\""))!.HasName);   // a list is not a name either
    }

    [Fact]
    public void A_designator_borrowed_from_ref_is_spoken_with_the_word_for_what_it_is()
    {
        // A ref is a REFERENCE, not a name: spoken bare it came out as "On the A." and
        // "A, to the left, 100 metres", which names nothing a pilot can look for.
        Assert.Equal("Apron A", One(Node("\"aeroway\":\"apron\",\"ref\":\"A\""))!.Name);
        Assert.Equal("Terminal T2", One(Node("\"aeroway\":\"terminal\",\"ref\":\"T2\""))!.Name);
        // It stays a PROPER name — OSM gave this apron that designator, and an unnamed neighbour
        // must not outrank it in the catalog merge.
        Assert.True(One(Node("\"aeroway\":\"apron\",\"ref\":\"A\""))!.HasProperName);
        // A real `name` is never touched, and neither is a ref that is already prose (the de-icing
        // pad below) or that already says the word.
        Assert.Equal("West Apron", One(Node("\"aeroway\":\"apron\",\"name\":\"West Apron\""))!.Name);
        Assert.Equal("Apron", One(Node("\"aeroway\":\"apron\",\"ref\":\"Apron\""))!.Name);
    }

    [Fact]
    public void Concourse_wording_is_not_english_only()
    {
        Assert.Equal(FeatureKind.Concourse, One(Node("\"aeroway\":\"terminal\",\"name\":\"Terminal 1 Flugsteig A\""))!.Kind);
        Assert.Equal(FeatureKind.Concourse, One(Node("\"aeroway\":\"terminal\",\"name\":\"Concourse B\""))!.Kind);
        Assert.Equal(FeatureKind.Terminal, One(Node("\"aeroway\":\"terminal\",\"name\":\"Baggage Claim\""))!.Kind);
        Assert.Equal(FeatureKind.Fbo, One(Node("\"aeroway\":\"terminal\",\"name\":\"General Aviation Terminal\",\"operator\":\"Jackson Hole Aviation LLC\""))!.Kind);
        Assert.Equal(FeatureKind.Cargo, One(Node("\"aeroway\":\"terminal\",\"name\":\"FedEx\""))!.Kind);
    }

    [Fact]
    public void An_apron_named_only_by_ref_can_be_a_deice_pad()
    {
        var pad = One(Node("\"aeroway\":\"apron\",\"ref\":\"De-icing pad\""))!;
        Assert.Equal(FeatureKind.DeicePad, pad.Kind);
        Assert.Equal("De-icing pad", pad.Name);      // already words: nothing is prefixed to it
    }

    [Theory]
    [InlineData("taxiway")] [InlineData("parking_position")] [InlineData("gate")] [InlineData("holding_position")] [InlineData("runway")]
    public void Pavement_elements_are_never_features(string aeroway)
        => Assert.Null(One(Node("\"aeroway\":\"" + aeroway + "\",\"ref\":\"A\",\"building\":\"yes\",\"name\":\"Signature Flight Support\"")));

    [Fact]
    public void A_real_overpass_response_classifies_with_footprints_and_no_center_member()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "osm-features-area-ktiw.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var features = doc.RootElement.GetProperty("elements").EnumerateArray()
            .Select(OsmFeatureClassifier.Classify).Where(f => f != null).Select(f => f!).ToList();

        Assert.Equal(25, features.Count);
        Assert.Equal(20, features.Count(f => f.Kind == FeatureKind.Hangar));
        Assert.Equal(1, features.Count(f => f.Kind == FeatureKind.Tower));
        var aprons = features.Where(f => f.Kind == FeatureKind.Apron).ToList();
        Assert.Equal(4, aprons.Count);
        Assert.All(aprons, a => Assert.True(a.Footprint != null && a.Footprint.Count >= 3));   // `out tags geom;` really carries geometry
        Assert.All(features, f => { Assert.InRange(f.Lat, 47.25, 47.29); Assert.InRange(f.Lon, -122.60, -122.55); });
        Assert.All(features, f => Assert.Equal(FeatureSource.Osm, f.Source));
    }

    // ---- Multipolygon relations (review OV-3) ------------------------------------------------
    //
    // Under `out tags geom;` a relation arrived as type, id, bounds and tags — no members, no
    // geometry (measured live 2026-09-22, KATL relation 10189710 "Domestic Terminal"). Under
    // `out body geom;` it carries `members`, each way member with its own `geometry`.

    [Fact]
    public void A_relation_outline_is_joined_from_its_outer_ways_one_of_them_reversed()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "osm-features-relation-terminal.json"));

        var f = Assert.Single(OsmFeatureSource.Parse(json));

        Assert.Equal(FeatureKind.Terminal, f.Kind);
        Assert.Equal("Terminal 3", f.Name);
        Assert.Equal(new[]
        {
            new LatLon(50.0460, 8.5930), new LatLon(50.0460, 8.5950), new LatLon(50.0450, 8.5960),
            new LatLon(50.0440, 8.5950), new LatLon(50.0440, 8.5930), new LatLon(50.0450, 8.5920),
        }, f.Footprint);                                                    // the inner way is not in it
        Assert.True(SurroundingsGeometry.Contains(f.Footprint!, f.Lat, f.Lon));
    }

    [Fact]
    public void A_relation_with_no_outer_way_to_join_keeps_the_centre_of_its_bounds()
    {
        // KATL relation 10189710's real bounds — first in the shape `out tags geom;` delivered (no
        // members at all), then with its only outer member a sub-relation, which has no geometry of
        // its own. Either way there is nothing to join: no outline, and the bounds centre as before.
        const string bounds = @"""bounds"":{""minlat"":33.6404431,""minlon"":-84.4463257,""maxlat"":33.6411475,""maxlon"":-84.4430752}";
        const string tags = @"""tags"":{""aeroway"":""terminal"",""name"":""Domestic Terminal"",""type"":""multipolygon""}";
        string noMembers = @"{""type"":""relation"",""id"":10189710," + bounds + "," + tags + "}";
        string subRelationOnly = @"{""type"":""relation"",""id"":10189710," + bounds +
            @",""members"":[{""type"":""relation"",""ref"":1,""role"":""outer""}]," + tags + "}";

        foreach (string json in new[] { noMembers, subRelationOnly })
        {
            var f = One(json)!;
            Assert.Equal(FeatureKind.Terminal, f.Kind);
            Assert.Null(f.Footprint);
            Assert.Equal((33.6404431 + 33.6411475) / 2.0, f.Lat, 9);
            Assert.Equal((-84.4463257 + -84.4430752) / 2.0, f.Lon, 9);
        }
    }

    [Fact]
    public void An_outer_way_with_a_gap_in_its_geometry_is_not_joined_across_it()
    {
        // Overpass prints a null vertex for a node outside an `out ... (bbox)` clip. These queries
        // never clip, but a way with a gap cannot be joined honestly, so it is left out — and with
        // it gone the other half of this ring cannot close, so there is no outline at all.
        const string json =
            @"{""type"":""relation"",""id"":900000002,
               ""bounds"":{""minlat"":50.0440,""minlon"":8.5920,""maxlat"":50.0460,""maxlon"":8.5960},
               ""members"":[
                 {""type"":""way"",""ref"":1,""role"":""outer"",""geometry"":[{""lat"":50.0460,""lon"":8.5930},{""lat"":50.0460,""lon"":8.5950},{""lat"":50.0450,""lon"":8.5960},{""lat"":50.0440,""lon"":8.5950}]},
                 {""type"":""way"",""ref"":2,""role"":""outer"",""geometry"":[{""lat"":50.0460,""lon"":8.5930},null,{""lat"":50.0440,""lon"":8.5930},{""lat"":50.0440,""lon"":8.5950}]}],
               ""tags"":{""aeroway"":""apron"",""name"":""North Apron"",""type"":""multipolygon""}}";

        var f = One(json)!;

        Assert.Equal(FeatureKind.Apron, f.Kind);
        Assert.Null(f.Footprint);
        Assert.Equal(50.0450, f.Lat, 9);
        Assert.Equal(8.5940, f.Lon, 9);
    }

    [Fact]
    public void A_way_carrying_the_node_id_array_of_out_body_classifies_exactly_as_before()
    {
        // `out body geom;` adds ONE thing to a way: `nodes`, its node ids. KTIW way 300483486 —
        // bounds, geometry and tags verbatim from the osm-features-area-ktiw.json capture (taken
        // under `out tags geom;`), node ids as Overpass returned them live 2026-09-22.
        string shared =
            @"""bounds"":{""minlat"":47.2637172,""minlon"":-122.5764924,""maxlat"":47.2649167,""maxlon"":-122.5743362},
              ""geometry"":[{""lat"":47.2649167,""lon"":-122.5762837},{""lat"":47.2648889,""lon"":-122.5759751},
                            {""lat"":47.2647437,""lon"":-122.5759984},{""lat"":47.2646155,""lon"":-122.5744337},
                            {""lat"":47.2646075,""lon"":-122.5743362},{""lat"":47.2637172,""lon"":-122.5744979},
                            {""lat"":47.2638791,""lon"":-122.5764924},{""lat"":47.2645456,""lon"":-122.576353},
                            {""lat"":47.2649167,""lon"":-122.5762837}],
              ""tags"":{""aeroway"":""apron"",""source"":""local knowledge""}";

        var before = One(@"{""type"":""way"",""id"":300483486," + shared + "}")!;
        var after = One(@"{""type"":""way"",""id"":300483486," +
            @"""nodes"":[3045593405,3045626448,6770738780,6770738783,3045593407,3045593408,3045593409,3045621091,3045593405]," +
            shared + "}")!;

        Assert.Equal(before.Kind, after.Kind);
        Assert.Equal(before.Footprint, after.Footprint);
        Assert.Equal(before.Lat, after.Lat);
        Assert.Equal(before.Lon, after.Lon);
        Assert.Equal(8, after.Footprint!.Count);   // nine vertices, closing duplicate dropped
    }

    [Fact]
    public void A_way_with_a_gap_in_its_geometry_has_no_outline_and_keeps_its_bounds_centre()
    {
        // A way and a relation member read their vertices through ONE reader, and a gap (the null
        // vertex Overpass prints outside an `out ... (bbox)` clip) means no outline for either. The
        // way's own loop used to THROW here — it asked a null vertex for its lat — and the throw
        // failed the whole airport's buildings fetch.
        const string json =
            @"{""type"":""way"",""id"":900000003,
               ""bounds"":{""minlat"":50.0440,""minlon"":8.5920,""maxlat"":50.0460,""maxlon"":8.5960},
               ""geometry"":[{""lat"":50.0460,""lon"":8.5930},{""lat"":50.0460,""lon"":8.5950},null,
                             {""lat"":50.0440,""lon"":8.5930},{""lat"":50.0460,""lon"":8.5930}],
               ""tags"":{""aeroway"":""apron"",""name"":""East Apron""}}";

        var f = One(json)!;

        Assert.Equal(FeatureKind.Apron, f.Kind);
        Assert.Null(f.Footprint);
        Assert.Equal(50.0450, f.Lat, 9);
        Assert.Equal(8.5940, f.Lon, 9);
    }

    [Fact]
    public void An_inner_way_never_becomes_the_footprint_even_when_the_outer_ring_cannot_close()
    {
        // A single OUTER member that does not close on its own and has no partner to join with —
        // LargestRing drops an incomplete chain like this one regardless (see
        // OsmRingAssemblerTests.A_chain_that_never_closes_is_dropped_and_a_closed_way_still_counts)
        // — plus an INNER member (a courtyard) that IS closed. Without the `role == "outer"` filter
        // in RelationOutline, this courtyard is the only thing that closes at all, so it would
        // become the footprint; pinning this catches that regression even though the real fixture's
        // own courtyard is too small to ever win on area against its outer ring.
        const string json =
            @"{""type"":""relation"",""id"":900000005,
               ""bounds"":{""minlat"":50.0440,""minlon"":8.5920,""maxlat"":50.0460,""maxlon"":8.5960},
               ""members"":[
                 {""type"":""way"",""ref"":1,""role"":""outer"",""geometry"":[{""lat"":50.0460,""lon"":8.5930},{""lat"":50.0460,""lon"":8.5950},{""lat"":50.0450,""lon"":8.5960}]},
                 {""type"":""way"",""ref"":2,""role"":""inner"",""geometry"":[{""lat"":50.0457,""lon"":8.5934},{""lat"":50.0457,""lon"":8.5938},{""lat"":50.0455,""lon"":8.5938},{""lat"":50.0455,""lon"":8.5934},{""lat"":50.0457,""lon"":8.5934}]}],
               ""tags"":{""aeroway"":""terminal"",""name"":""Broken Terminal"",""type"":""multipolygon""}}";

        var f = One(json)!;

        Assert.Equal(FeatureKind.Terminal, f.Kind);
        Assert.Null(f.Footprint);
    }
}
