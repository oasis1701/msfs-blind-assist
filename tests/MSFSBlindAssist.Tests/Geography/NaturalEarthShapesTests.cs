using System.Text;
using MSFSBlindAssist.Services.Geography;

namespace MSFSBlindAssist.Tests.Geography;

public class NaturalEarthShapesTests
{
    private static IEnumerable<ShapeRecord> Read(NaturalEarthLayer layer, string json) =>
        NaturalEarthShapes.Read(layer, new MemoryStream(Encoding.UTF8.GetBytes(json))).ToList();

    private static string Feature(string properties, string geometry) =>
        "{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\",\"properties\":" + properties + ",\"geometry\":" + geometry + "}]}";

    private const string Square = "[[[-1,-1],[1,-1],[1,1],[-1,1],[-1,-1]]]";

    [Fact]
    public void A_multipolygon_becomes_one_record_per_part_with_its_own_box()
    {
        string geometry = "{\"type\":\"MultiPolygon\",\"coordinates\":[[[[178,-18],[180,-18],[180,-16],[178,-16],[178,-18]]],[[[-180,-18],[-178,-18],[-178,-16],[-180,-16],[-180,-18]]]]}";
        var parts = Read(NaturalEarthLayer.Marine, Feature("{\"name\":\"Koro Sea\"}", geometry)).ToList();
        Assert.Equal(2, parts.Count);
        Assert.All(parts, p => Assert.True(p.MaxLon - p.MinLon <= 2.0)); // never a globe-wide box
    }

    [Fact]
    public void Holes_are_kept_after_the_outer_ring()
    {
        string geometry = "{\"type\":\"Polygon\",\"coordinates\":[[[-2,-2],[2,-2],[2,2],[-2,2],[-2,-2]],[[-1,-1],[1,-1],[1,1],[-1,1],[-1,-1]]]}";
        var lake = Assert.Single(Read(NaturalEarthLayer.Lake, Feature("{\"name\":\"Ring Lake\"}", geometry)));
        Assert.Equal(2, lake.Rings.Count);
    }

    [Fact]
    public void Lines_are_cut_into_chunks_that_share_their_joining_vertex()
    {
        var coords = string.Join(",", Enumerable.Range(0, 150).Select(i => $"[{i * 0.01},0]"));
        var chunks = Read(NaturalEarthLayer.Coastline, Feature("{}", "{\"type\":\"LineString\",\"coordinates\":[" + coords + "]}")).ToList();
        Assert.Equal(3, chunks.Count); // 64 + 64 + 24 vertices, overlapping by one
        Assert.All(chunks, c => Assert.True(c.IsLine));
        Assert.All(chunks, c => Assert.InRange(c.Rings[0].Length / 2, 2, NaturalEarthShapes.LineChunkVertices));
        Assert.Equal(chunks[0].Rings[0][^2], chunks[1].Rings[0][0]); // last lon of chunk 1 = first lon of chunk 2
    }

    [Fact]
    public void Unnamed_seas_and_lakes_are_skipped()
    {
        Assert.Empty(Read(NaturalEarthLayer.Marine, Feature("{\"name\":null}", "{\"type\":\"Polygon\",\"coordinates\":" + Square + "}")));
        Assert.Empty(Read(NaturalEarthLayer.Lake, Feature("{}", "{\"type\":\"Polygon\",\"coordinates\":" + Square + "}")));
    }

    [Fact]
    public void Regions_use_upper_case_keys_skip_continents_and_tidy_names()
    {
        string poly = "{\"type\":\"Polygon\",\"coordinates\":" + Square + "}";
        Assert.Empty(Read(NaturalEarthLayer.Region, Feature("{\"NAME\":\"NORTH AMERICA\",\"FEATURECLA\":\"Continent\"}", poly)));
        var alps = Assert.Single(Read(NaturalEarthLayer.Region, Feature("{\"NAME\":\"ALPS\",\"FEATURECLA\":\"Range/mtn\"}", poly)));
        Assert.Equal("Alps", alps.Name);
        Assert.Equal("mountain range", alps.Extra);
    }

    [Fact]
    public void States_store_the_geonames_code_iso_code_and_country()
    {
        string poly = "{\"type\":\"Polygon\",\"coordinates\":" + Square + "}";
        var gr = Assert.Single(Read(NaturalEarthLayer.State, Feature(
            "{\"name\":\"Graubünden\",\"admin\":\"Switzerland\",\"iso_a2\":\"CH\",\"gn_a1_code\":\"CH.GR\"}", poly)));
        Assert.Equal("Graubünden", gr.Name);
        Assert.Equal("CH.GR|CH|Switzerland", gr.Extra);

        var noCode = Assert.Single(Read(NaturalEarthLayer.State, Feature("{\"name\":\"Somewhere\",\"admin\":\"Nowhere\"}", poly)));
        Assert.Equal("||Nowhere", noCode.Extra);
    }

    [Fact]
    public void Encode_and_decode_round_trip_to_float_precision()
    {
        var rings = new List<double[]> { new[] { -76.48357, 38.55179, 179.99999, -89.5 }, new[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 } };
        var back = NaturalEarthShapes.Decode(NaturalEarthShapes.Encode(rings));
        Assert.Equal(2, back.Count);
        for (int r = 0; r < rings.Count; r++)
            for (int i = 0; i < rings[r].Length; i++)
                Assert.Equal(rings[r][i], back[r][i], 4);
    }

    [Fact]
    public void A_truncated_blob_decodes_what_it_can_without_throwing()
    {
        var blob = NaturalEarthShapes.Encode(new List<double[]> { new[] { 1.0, 2.0, 3.0, 4.0 } });
        Assert.Empty(NaturalEarthShapes.Decode(blob.AsSpan(0, 10)));
        Assert.Empty(NaturalEarthShapes.Decode(ReadOnlySpan<byte>.Empty));
    }
}
