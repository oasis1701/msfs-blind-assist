// Characterization tests for OsmTaxiSource.Parse's aeroway=holding_position
// handling — the data source behind the Taxi Assist "Depart from named holding
// point" option. Painted hold-line designators (LSZH "A2") exist in OSM only as
// holding_position NODES, not as named taxiway ways, so the parser must collect
// them separately: name from ref (name-tag fallback), position from the node's
// lat/lon, unnamed nodes dropped (they carry nothing selectable).

using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Tests;

public class OsmHoldingPositionParseTests
{
    private const string Json = """
    {
      "elements": [
        {
          "type": "node", "id": 1, "lat": 47.46, "lon": 8.55,
          "tags": { "aeroway": "holding_position", "ref": "A2", "holding_position:type": "ILS" }
        },
        {
          "type": "node", "id": 2, "lat": 47.47, "lon": 8.56,
          "tags": { "aeroway": "holding_position", "name": "CAT III B" }
        },
        {
          "type": "node", "id": 3, "lat": 47.48, "lon": 8.57,
          "tags": { "aeroway": "holding_position" }
        },
        {
          "type": "node", "id": 4, "lat": 47.49, "lon": 8.58,
          "tags": { "aeroway": "parking_position", "ref": "A10" }
        },
        {
          "type": "way", "id": 5,
          "tags": { "aeroway": "taxiway", "ref": "E" },
          "geometry": [ { "lat": 47.45, "lon": 8.54 }, { "lat": 47.451, "lon": 8.541 } ]
        }
      ]
    }
    """;

    [Fact]
    public void Collects_ref_named_holding_positions_with_coordinates()
    {
        var data = OsmTaxiSource.Parse(Json);
        var a2 = Assert.Single(data.HoldingPoints, p => p.Name == "A2");
        Assert.Equal(47.46, a2.Lat, 6);
        Assert.Equal(8.55, a2.Lon, 6);
    }

    [Fact]
    public void Collects_the_holding_position_type_kind_with_empty_fallback()
    {
        // Kind rides the OSM "holding_position:type" tag (runway/ILS/intermediate)
        // and is "" when the tag is absent — consumed by NamedHoldingPointResolver's
        // DisplayLabel ("N11 (ILS hold)").
        var data = OsmTaxiSource.Parse(Json);
        var a2 = Assert.Single(data.HoldingPoints, p => p.Name == "A2");
        Assert.Equal("ILS", a2.Kind);
        var cat3b = Assert.Single(data.HoldingPoints, p => p.Name == "CAT III B");
        Assert.Equal("", cat3b.Kind);
    }

    [Fact]
    public void Falls_back_to_the_name_tag_when_ref_is_absent()
    {
        var data = OsmTaxiSource.Parse(Json);
        Assert.Contains(data.HoldingPoints, p => p.Name == "CAT III B");
    }

    [Fact]
    public void Drops_unnamed_holding_positions()
    {
        var data = OsmTaxiSource.Parse(Json);
        Assert.Equal(2, data.HoldingPoints.Count);
    }

    [Fact]
    public void Does_not_disturb_taxiway_or_parking_parsing()
    {
        var data = OsmTaxiSource.Parse(Json);
        Assert.Single(data.Taxiways);
        Assert.Equal("E", data.Taxiways[0].Name);
        var park = Assert.Single(data.Parking);
        Assert.Equal("A10", park.Name);
    }
}
