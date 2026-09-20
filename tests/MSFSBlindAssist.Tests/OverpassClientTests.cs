using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Tests;

public class OverpassClientTests
{
    [Fact]
    public void A_runtime_error_remark_is_a_failed_response_even_with_http_200()
    {
        // Verbatim shape from overpass.openstreetmap.fr, which has no area database.
        string body = "{\"version\":0.6,\"elements\":[],\"remark\":\"runtime error: open64: 2 No such file or directory /data/work/overpass/database/area_tags_local.bin File_Blocks::File_Blocks::1\"}";
        Assert.True(OverpassClient.IsFailedResponse(body));
    }

    [Fact]
    public void A_timed_out_query_remark_is_a_failed_response()
        => Assert.True(OverpassClient.IsFailedResponse("{\"elements\":[{\"type\":\"node\",\"id\":1}],\"remark\":\"runtime error: Query timed out in \\\"query\\\" at line 1 after 51 seconds.\"}"));

    [Fact]
    public void An_ordinary_response_is_not_failed_even_when_empty()
    {
        Assert.False(OverpassClient.IsFailedResponse("{\"version\":0.6,\"elements\":[]}"));
        Assert.False(OverpassClient.IsFailedResponse("{\"elements\":[{\"type\":\"node\",\"id\":1,\"lat\":1.0,\"lon\":2.0}]}"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html><body>OSM3S Response</body></html>")]
    [InlineData("{\"version\":0.6}")]
    [InlineData("[1,2,3]")]
    public void A_body_that_is_not_an_overpass_result_is_failed(string body)
        => Assert.True(OverpassClient.IsFailedResponse(body));
}
