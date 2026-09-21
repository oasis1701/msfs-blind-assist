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
}
