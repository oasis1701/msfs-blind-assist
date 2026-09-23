using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// <see cref="OverpassClient.ClassifyBody"/>: what ONE parse of an Overpass body says it is. It
/// replaced a pair of helpers that parsed every body twice (review CL-6) and could disagree — the
/// verbatim overpass.openstreetmap.fr body below, a "runtime error" remark on an EMPTY element list,
/// was "empty" to one and "failed" to the other, which only never mattered because the failure
/// test happened to run first.
/// </summary>
public class OverpassClientTests
{
    private static OverpassClient.BodyKind Kind(string body) => OverpassClient.ClassifyBody(body);

    [Fact]
    public void A_runtime_error_remark_is_a_failed_body_even_with_http_200()
    {
        // Verbatim shape from overpass.openstreetmap.fr, which has no area database.
        string body = "{\"version\":0.6,\"elements\":[],\"remark\":\"runtime error: open64: 2 No such file or directory /data/work/overpass/database/area_tags_local.bin File_Blocks::File_Blocks::1\"}";
        Assert.Equal(OverpassClient.BodyKind.Failed, Kind(body));
    }

    [Fact]
    public void A_timed_out_query_remark_is_a_failed_body_even_with_elements()
        => Assert.Equal(OverpassClient.BodyKind.Failed,
            Kind("{\"elements\":[{\"type\":\"node\",\"id\":1}],\"remark\":\"runtime error: Query timed out in \\\"query\\\" at line 1 after 51 seconds.\"}"));

    [Fact]
    public void An_ordinary_response_is_empty_or_has_elements_never_failed()
    {
        Assert.Equal(OverpassClient.BodyKind.Empty, Kind("{\"version\":0.6,\"elements\":[]}"));
        Assert.Equal(OverpassClient.BodyKind.Elements, Kind("{\"elements\":[{\"type\":\"node\",\"id\":1,\"lat\":1.0,\"lon\":2.0}]}"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html><body>OSM3S Response</body></html>")]
    [InlineData("{\"version\":0.6}")]
    [InlineData("{\"elements\":{}}")]
    [InlineData("[1,2,3]")]
    public void A_body_that_is_not_an_overpass_result_is_failed(string body)
        => Assert.Equal(OverpassClient.BodyKind.Failed, Kind(body));
}
