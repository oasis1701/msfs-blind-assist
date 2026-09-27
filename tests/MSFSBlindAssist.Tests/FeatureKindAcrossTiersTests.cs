using System.Text.Json;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.SceneryIndex;
using MSFSBlindAssist.Services.Surroundings;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// One name, one kind, from every tier (PR #230 review PC-5). AirportFeatureCatalog never merges
/// across kinds, so a building two sources name alike but classify differently is listed twice —
/// GSX read Cargo first, OSM and the scenery Concourse first, OSM's plain-building branch FBO
/// first. Each tier gets the name in its own natural form: an OSM element's `name` tag, a GSX
/// section header over gate-typed stands (so the stand types cannot decide), a scenery model name.
/// </summary>
public class FeatureKindAcrossTiersTests
{
    private static FeatureKind? Osm(string tags)
    {
        using var doc = JsonDocument.Parse("{\"type\":\"node\",\"id\":1,\"lat\":51.47,\"lon\":-0.45,\"tags\":{" + tags + "}}");
        return OsmFeatureClassifier.Classify(doc.RootElement)?.Kind;
    }

    private static FeatureKind? OsmTerminal(string name) => Osm("\"aeroway\":\"terminal\",\"name\":" + JsonSerializer.Serialize(name));
    private static FeatureKind? OsmBuilding(string name) => Osm("\"building\":\"yes\",\"name\":" + JsonSerializer.Serialize(name));

    private static FeatureKind Gsx(string header)
    {
        var spots = new[]
        {
            new ParkingSpot { Source = GateSource.Gsx, TerminalName = header, Number = 1, Type = 11, Latitude = 40.6410, Longitude = -73.7800 },
            new ParkingSpot { Source = GateSource.Gsx, TerminalName = header, Number = 2, Type = 11, Latitude = 40.6414, Longitude = -73.7800 },
        };
        return Assert.Single(GsxTerminalFeatureSource.Read(spots)).Kind;
    }

    private static FeatureKind? Scenery(string name)
        => SceneryModelNameClassifier.Classify("KXYZ_" + name.Replace(' ', '_'), "KXYZ")?.Kind;

    [Theory]
    [InlineData("DHL Aviation", FeatureKind.Cargo)]
    [InlineData("Menzies Aviation Cargo", FeatureKind.Cargo)]
    [InlineData("Virgin Atlantic Cargo", FeatureKind.Cargo)]
    [InlineData("Cargo Satellite", FeatureKind.Cargo)]
    [InlineData("Signature Flight Support", FeatureKind.Fbo)]
    [InlineData("Jet Aviation", FeatureKind.Fbo)]
    [InlineData("Concourse B", FeatureKind.Concourse)]
    public void A_name_is_the_same_kind_in_every_tier(string name, FeatureKind kind)
    {
        Assert.Equal(kind, FeatureLexicon.NamedKind(name));
        Assert.Equal(kind, OsmTerminal(name));
        Assert.Equal(kind, Gsx(name));
        Assert.Equal(kind, Scenery(name));
        // A plain named building stays STRICT: its name may make it Cargo or an FBO, never a concourse.
        Assert.Equal(kind == FeatureKind.Concourse ? (FeatureKind?)null : kind, OsmBuilding(name));
    }

    [Theory]
    [InlineData("Civil Aviation Authority")]
    [InlineData("Virgin Atlantic Upper Class")]
    public void An_office_or_an_airline_facility_is_an_fbo_in_no_tier(string name)
    {
        Assert.Null(OsmBuilding(name));                            // its name says no aviation kind at all
        Assert.Equal(FeatureKind.Terminal, OsmTerminal(name));
        Assert.Equal(FeatureKind.Terminal, Gsx(name));
        Assert.NotEqual(FeatureKind.Fbo, Scenery(name));
    }

    [Fact]
    public void A_terminal_run_by_a_department_of_aviation_is_a_terminal_and_one_run_by_an_fbo_is_an_fbo()
    {
        Assert.Equal(FeatureKind.Terminal, Osm("\"aeroway\":\"terminal\",\"name\":\"Terminal\",\"operator\":\"City of Atlanta Department of Aviation\""));
        Assert.Equal(FeatureKind.Fbo, Osm("\"aeroway\":\"terminal\",\"operator\":\"Signature Flight Support\""));
    }

    [Theory]
    [InlineData("Deicing Pad")] [InlineData("De Ice Pad")] [InlineData("De-Icing Apron")]
    public void Every_deice_spelling_is_a_deice_pad_from_osm_and_from_the_scenery(string name)
    {
        Assert.Equal(FeatureKind.DeicePad, Osm("\"aeroway\":\"apron\",\"name\":" + JsonSerializer.Serialize(name)));
        Assert.Equal(FeatureKind.DeicePad, Scenery(name));
    }
}
