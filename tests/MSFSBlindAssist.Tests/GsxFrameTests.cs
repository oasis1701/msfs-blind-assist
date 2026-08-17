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

    [Fact]
    public void Successful_result_exposes_its_payload_object_for_downstream_parsers()
    {
        // GsxGateSelectResult (and any future verb-result parser) needs the raw
        // payload — GsxFrame itself does not interpret it.
        var f = GsxFrame.Parse("""{"type":"result","id":"g-1","ok":true,"payload":{"code":"ok","status":"prepared"}}""");
        Assert.Equal(JsonValueKind.Object, f.Payload.ValueKind);
        Assert.Equal("prepared", f.Payload.GetProperty("status").GetString());
    }

    [Fact]
    public void Failing_result_exposes_its_error_object_beyond_code_and_message()
    {
        // error.candidates (gate.select's ambiguous case) and similar members
        // live only on the raw object; ErrorCode/ErrorMessage don't carry them.
        var f = GsxFrame.Parse("""{"type":"result","id":"g-2","ok":false,"error":{"code":"ambiguous","candidates":[]}}""");
        Assert.Equal(JsonValueKind.Object, f.Error.ValueKind);
        Assert.Equal(JsonValueKind.Array, f.Error.GetProperty("candidates").ValueKind);
    }

    [Fact]
    public void Payload_and_error_default_to_undefined_when_the_frame_does_not_carry_them()
    {
        var patch = GsxFrame.Parse("""{"type":"patch","path":"/services","value":[]}""");
        Assert.Equal(JsonValueKind.Undefined, patch.Payload.ValueKind);
        Assert.Equal(JsonValueKind.Undefined, patch.Error.ValueKind);

        // A result frame with ok:true and no payload member at all.
        var noPayload = GsxFrame.Parse("""{"type":"result","id":"g-3","ok":true}""");
        Assert.Equal(JsonValueKind.Undefined, noPayload.Payload.ValueKind);
    }
}
