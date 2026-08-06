// Characterization tests for OsmTaxiSource.Parse's holding_position handling
// (named-holding-point augmentation). Taxiway/parking parsing predates these
// tests and is exercised implicitly by the mixed-payload case.

using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Tests;

public class OsmTaxiSourceParseTests
{
    private static string Element(string body) =>
        $"{{\"elements\":[{body}]}}";

    [Fact]
    public void Parse_collects_named_holding_positions_with_ref_and_type()
    {
        var data = OsmTaxiSource.Parse(Element(
            "{\"type\":\"node\",\"lat\":51.46807,\"lon\":-0.48354," +
            "\"tags\":{\"aeroway\":\"holding_position\",\"ref\":\"VIKAS\"}}," +
            "{\"type\":\"node\",\"lat\":51.46606,\"lon\":-0.48765," +
            "\"tags\":{\"aeroway\":\"holding_position\",\"ref\":\"N11\",\"holding_position:type\":\"ILS\"}}"));

        Assert.Equal(2, data.HoldingPoints.Count);
        Assert.Equal(("VIKAS", 51.46807, -0.48354, ""), data.HoldingPoints[0]);
        Assert.Equal(("N11", 51.46606, -0.48765, "ILS"), data.HoldingPoints[1]);
    }

    [Fact]
    public void Parse_falls_back_to_name_when_ref_is_absent()
    {
        var data = OsmTaxiSource.Parse(Element(
            "{\"type\":\"node\",\"lat\":51.0,\"lon\":-0.4," +
            "\"tags\":{\"aeroway\":\"holding_position\",\"name\":\"HANLI\",\"holding_position:type\":\"intermediate\"}}"));

        var hp = Assert.Single(data.HoldingPoints);
        Assert.Equal(("HANLI", 51.0, -0.4, "intermediate"), hp);
    }

    [Fact]
    public void Parse_skips_unnamed_holding_positions()
    {
        // The vast majority of holding_position nodes are unnamed painted hold
        // lines — they must never surface as selectable points.
        var data = OsmTaxiSource.Parse(Element(
            "{\"type\":\"node\",\"lat\":51.0,\"lon\":-0.4," +
            "\"tags\":{\"aeroway\":\"holding_position\",\"holding_position:type\":\"runway\"}}"));

        Assert.Empty(data.HoldingPoints);
    }

    [Fact]
    public void Parse_holding_positions_do_not_leak_into_taxiways_or_parking()
    {
        var data = OsmTaxiSource.Parse(Element(
            "{\"type\":\"way\",\"tags\":{\"aeroway\":\"taxiway\",\"ref\":\"A\"}," +
            "\"geometry\":[{\"lat\":51.0,\"lon\":-0.4},{\"lat\":51.001,\"lon\":-0.4}]}," +
            "{\"type\":\"node\",\"lat\":51.002,\"lon\":-0.4," +
            "\"tags\":{\"aeroway\":\"parking_position\",\"ref\":\"A51\"}}," +
            "{\"type\":\"node\",\"lat\":51.003,\"lon\":-0.4," +
            "\"tags\":{\"aeroway\":\"holding_position\",\"ref\":\"DASSO\"}}"));

        Assert.Single(data.Taxiways);
        Assert.Single(data.Parking);
        var hp = Assert.Single(data.HoldingPoints);
        Assert.Equal("DASSO", hp.Name);
    }
}
