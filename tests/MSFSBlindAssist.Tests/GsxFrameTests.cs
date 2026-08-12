using System.Text.Json;
using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

public class GsxFrameTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Parses_hello_with_capabilities()
    {
        var f = GsxFrame.Parse(Fixture("gsx-hello.json"));
        Assert.Equal(GsxFrameType.Hello, f.Type);
        Assert.Equal(1, f.Protocol);
        Assert.True(f.GsxRunning);
        Assert.Contains("menu", f.Capabilities);
        Assert.Contains("handlerData", f.Capabilities);
    }

    [Fact]
    public void Parses_patch_and_strips_leading_slash_from_key()
    {
        var f = GsxFrame.Parse("""{"v":1,"type":"patch","path":"/services","value":[]}""");
        Assert.Equal(GsxFrameType.Patch, f.Type);
        Assert.Equal("/services", f.Path);
        Assert.Equal("services", f.Key);
        Assert.Equal(JsonValueKind.Array, f.Value.ValueKind);
    }

    [Fact]
    public void Parses_failing_result_with_error_code_and_id()
    {
        var f = GsxFrame.Parse(Fixture("gsx-result-error.json"));
        Assert.Equal(GsxFrameType.Result, f.Type);
        Assert.False(f.Ok);
        Assert.Equal("unknown_verb", f.ErrorCode);
        Assert.False(string.IsNullOrEmpty(f.Id));
    }

    [Fact]
    public void Parses_engine_event_restarting()
    {
        var f = GsxFrame.Parse("""{"v":1,"type":"event","topic":"engine","gsxRunning":false,"restarting":true}""");
        Assert.Equal(GsxFrameType.Event, f.Type);
        Assert.Equal("engine", f.Topic);
        Assert.True(f.Restarting);
        Assert.False(f.GsxRunning);
    }

    [Fact]
    public void Unknown_type_and_malformed_json_do_not_throw()
    {
        Assert.Equal(GsxFrameType.Unknown, GsxFrame.Parse("""{"type":"whatever"}""").Type);
        Assert.Equal(GsxFrameType.Unknown, GsxFrame.Parse("not json at all").Type);
    }
}
