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

    // ---- Stand / gate coverage (added 2026-08-25) ----------------------------------
    //
    // Element shapes are real Overpass "out tags geom" output, sampled live from
    // overpass-api.de around EGLL (51.4700,-0.4543) and KDTW (42.2124,-83.3534):
    //   EGLL   70 parking_position nodes vs 304 ways;  148 aeroway=gate nodes
    //   KDTW    0 parking_position nodes vs 176 ways;  133 aeroway=gate nodes
    // i.e. the previous node-only query saw NOTHING at KDTW. That is the regression
    // these tests pin.

    // A stand mapped as a WAY (the painted guidance line) — the dominant shape at hubs.
    private const string StandWay =
        @"{""elements"":[
            {""type"":""way"",""id"":1,""tags"":{""aeroway"":""parking_position"",""ref"":""A46A""},
             ""geometry"":[{""lat"":42.2100,""lon"":-83.3500},{""lat"":42.2110,""lon"":-83.3500}]}
          ]}";

    [Fact]
    public void Parking_position_mapped_as_a_way_is_collected()
    {
        var data = OsmTaxiSource.Parse(StandWay);

        var stand = Assert.Single(data.Parking);
        Assert.Equal("A46A", stand.Name);
    }

    [Fact]
    public void A_stand_way_reports_the_midpoint_of_its_line_not_an_endpoint()
    {
        var data = OsmTaxiSource.Parse(StandWay);

        var stand = Assert.Single(data.Parking);
        Assert.Equal(42.2105, stand.Lat, 6);
        Assert.Equal(-83.3500, stand.Lon, 6);
    }

    [Fact]
    public void Parking_position_mapped_as_a_node_still_uses_its_own_position()
    {
        string json =
            @"{""elements"":[
                {""type"":""node"",""id"":2,""lat"":51.4700,""lon"":-0.4543,
                 ""tags"":{""aeroway"":""parking_position"",""ref"":""531""}}
              ]}";

        var data = OsmTaxiSource.Parse(json);

        var stand = Assert.Single(data.Parking);
        Assert.Equal("531", stand.Name);
        Assert.Equal(51.4700, stand.Lat, 6);
        Assert.Equal(-0.4543, stand.Lon, 6);
    }

    [Fact]
    public void Aeroway_gate_contributes_a_stand_designator()
    {
        // KDTW's gate nodes carry exactly the terminal-side numbering a controller says ("A24").
        string json =
            @"{""elements"":[
                {""type"":""node"",""id"":3,""lat"":42.2124,""lon"":-83.3534,
                 ""tags"":{""aeroway"":""gate"",""ref"":""A24""}}
              ]}";

        var data = OsmTaxiSource.Parse(json);

        var gate = Assert.Single(data.Parking);
        Assert.Equal("A24", gate.Name);
    }

    [Fact]
    public void A_stand_or_gate_without_a_ref_is_dropped_even_when_it_has_a_name()
    {
        // "name" is free prose on aeroway features ("Terminal 3"), which StandId would parse as
        // stand number 3 and alias onto an unrelated gate. Every gate/stand in the live sample
        // carried a ref, so ref-only costs no coverage.
        string json =
            @"{""elements"":[
                {""type"":""node"",""id"":4,""lat"":42.2124,""lon"":-83.3534,
                 ""tags"":{""aeroway"":""gate"",""name"":""Terminal 3""}},
                {""type"":""way"",""id"":5,""tags"":{""aeroway"":""parking_position""},
                 ""geometry"":[{""lat"":42.2100,""lon"":-83.3500},{""lat"":42.2110,""lon"":-83.3500}]}
              ]}";

        var data = OsmTaxiSource.Parse(json);

        Assert.Empty(data.Parking);
    }

    [Fact]
    public void A_stand_way_with_no_geometry_is_skipped_rather_than_placed_at_null_island()
    {
        string json =
            @"{""elements"":[
                {""type"":""way"",""id"":6,""tags"":{""aeroway"":""parking_position"",""ref"":""B12""}}
              ]}";

        var data = OsmTaxiSource.Parse(json);

        Assert.Empty(data.Parking);
    }

    [Fact]
    public void Taxiway_ways_and_holding_position_nodes_are_unaffected_by_the_stand_changes()
    {
        string json =
            @"{""elements"":[
                {""type"":""way"",""id"":7,""tags"":{""aeroway"":""taxiway"",""ref"":""A""},
                 ""geometry"":[{""lat"":51.4700,""lon"":-0.4543},{""lat"":51.4710,""lon"":-0.4543},
                               {""lat"":51.4720,""lon"":-0.4543}]},
                {""type"":""node"",""id"":8,""lat"":51.4705,""lon"":-0.4540,
                 ""tags"":{""aeroway"":""holding_position"",""ref"":""A2"",
                           ""holding_position:type"":""runway""}}
              ]}";

        var data = OsmTaxiSource.Parse(json);

        Assert.Equal(2, data.Taxiways.Count);          // 3 vertices -> 2 segments
        Assert.All(data.Taxiways, s => Assert.Equal("A", s.Name));

        var hold = Assert.Single(data.HoldingPoints);
        Assert.Equal("A2", hold.Name);
        Assert.Equal("runway", hold.Kind);
        Assert.Empty(data.Parking);
    }

    [Fact]
    public void A_gate_way_uses_its_geometry_midpoint_like_a_stand_way()
    {
        string json =
            @"{""elements"":[
                {""type"":""way"",""id"":9,""tags"":{""aeroway"":""gate"",""ref"":""19""},
                 ""geometry"":[{""lat"":51.4700,""lon"":-0.4560},{""lat"":51.4700,""lon"":-0.4540}]}
              ]}";

        var data = OsmTaxiSource.Parse(json);

        var gate = Assert.Single(data.Parking);
        Assert.Equal("19", gate.Name);
        Assert.Equal(51.4700, gate.Lat, 6);
        Assert.Equal(-0.4550, gate.Lon, 6);
    }

    [Fact]
    public void The_midpoint_is_by_ARC_LENGTH_so_a_densely_noded_end_does_not_drag_it()
    {
        // Four vertices, three of them bunched at the start: a vertex AVERAGE would sit at
        // ~lat 42.20025, the arc-length midpoint sits at the true middle of the line.
        string json =
            @"{""elements"":[
                {""type"":""way"",""id"":10,""tags"":{""aeroway"":""parking_position"",""ref"":""C7""},
                 ""geometry"":[{""lat"":42.2000,""lon"":-83.3500},{""lat"":42.2001,""lon"":-83.3500},
                               {""lat"":42.2002,""lon"":-83.3500},{""lat"":42.2100,""lon"":-83.3500}]}
              ]}";

        var data = OsmTaxiSource.Parse(json);

        var stand = Assert.Single(data.Parking);
        Assert.Equal(42.2050, stand.Lat, 6);
    }

    [Fact]
    public void A_stand_way_across_the_antimeridian_stays_on_the_line()
    {
        // NZ/Fiji-side aprons: a raw (lon1+lon2)/2 would land on the far side of the planet.
        string json =
            @"{""elements"":[
                {""type"":""way"",""id"":11,""tags"":{""aeroway"":""parking_position"",""ref"":""7""},
                 ""geometry"":[{""lat"":-17.7550,""lon"":179.9990},{""lat"":-17.7550,""lon"":-179.9990}]}
              ]}";

        var data = OsmTaxiSource.Parse(json);

        var stand = Assert.Single(data.Parking);
        Assert.Equal(180.0, Math.Abs(stand.Lon), 4);
    }

    // ---- The Overpass query itself -----------------------------------------------------
    //
    // The query embeds the airport's coordinates as bare decimals inside an `around:`
    // clause. Interpolating them under the CURRENT culture emits a comma decimal separator
    // on de-DE / fr-FR / pt-BR / tr-TR, which turns `around:5000,51.4706,-0.4614` into a
    // five-token clause Overpass rejects with a 400 — silently killing the whole online
    // taxiway/holding-point/stand layer for every user on such a machine, and (since the
    // per-mirror cooldown landed) blacklisting all seven mirrors while it does so.

    [Fact]
    public void The_overpass_query_uses_invariant_decimal_separators()
    {
        var prev = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");

            string q = OsmTaxiSource.BuildQuery(51.4706, -0.4614);

            Assert.Contains("51.4706", q);
            Assert.Contains("-0.4614", q);
            Assert.DoesNotContain("51,4706", q);
            Assert.DoesNotContain("0,4614", q);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = prev; }
    }

    [Fact]
    public void The_overpass_query_asks_for_stands_and_gates_as_node_and_way()
    {
        // At hubs a stand is mapped as the painted guidance LINE, not a point (KDTW: zero
        // stand nodes against 176 ways), so dropping either spelling silently empties the
        // gate-alias layer at exactly the airports that need it.
        string q = OsmTaxiSource.BuildQuery(42.2124, -83.3534);

        Assert.Contains("node[\"aeroway\"=\"parking_position\"]", q);
        Assert.Contains("way[\"aeroway\"=\"parking_position\"]", q);
        Assert.Contains("node[\"aeroway\"=\"gate\"]", q);
        Assert.Contains("way[\"aeroway\"=\"gate\"]", q);
        Assert.Contains("way[\"aeroway\"=\"taxiway\"]", q);
        Assert.Contains("node[\"aeroway\"=\"holding_position\"]", q);
    }
}
