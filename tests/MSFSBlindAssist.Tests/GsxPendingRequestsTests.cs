using System.Text.Json;
using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

public class GsxPendingRequestsTests
{
    [Fact]
    public void Ids_are_unique()
    {
        var p = new GsxPendingRequests();
        var (a, _) = p.Register();
        var (b, _) = p.Register();
        Assert.NotEqual(a, b);
        Assert.Equal(2, p.PendingCount);
    }

    [Fact]
    public async Task Completes_the_matching_request_only()
    {
        var p = new GsxPendingRequests();
        var (id1, t1) = p.Register();
        var (_, t2) = p.Register();

        var json = $"{{\"type\":\"result\",\"ok\":true,\"id\":\"{id1}\"}}";
        Assert.True(p.Complete(GsxFrame.Parse(json)));

        var r1 = await t1;
        Assert.True(r1.Ok);
        Assert.False(t2.IsCompleted);
        Assert.Equal(1, p.PendingCount);
    }

    [Fact]
    public async Task Completes_out_of_order()
    {
        var p = new GsxPendingRequests();
        var (id1, t1) = p.Register();
        var (id2, t2) = p.Register();

        p.Complete(GsxFrame.Parse($"{{\"type\":\"result\",\"ok\":true,\"id\":\"{id2}\"}}"));
        p.Complete(GsxFrame.Parse($"{{\"type\":\"result\",\"ok\":true,\"id\":\"{id1}\"}}"));

        Assert.True((await t1).Ok);
        Assert.True((await t2).Ok);
    }

    [Fact]
    public async Task Carries_the_error_code_and_message()
    {
        var p = new GsxPendingRequests();
        var (id, t) = p.Register();
        var json = $"{{\"type\":\"result\",\"ok\":false,\"id\":\"{id}\",\"error\":{{\"code\":\"unknown_verb\",\"message\":\"unknown verb: x\"}}}}";
        p.Complete(GsxFrame.Parse(json));

        var r = await t;
        Assert.False(r.Ok);
        Assert.Equal("unknown_verb", r.ErrorCode);
        Assert.Contains("unknown verb", r.ErrorMessage!);
    }

    // ── Plumbing: the awaiting caller can reach the whole frame (Task 2, Part A) ──
    // Task 1 added GsxFrame.Payload/GsxFrame.Error, but Complete() used to discard
    // everything except Ok/ErrorCode/ErrorMessage when building the GsxResult a
    // caller awaits — so a typed interpreter like GsxGateSelectResult.FromFrame had
    // no live frame to parse. These two pin that GsxResult now carries the frame
    // through, additively (Frame is a new, defaulted member — every existing
    // GsxResult member keeps its prior meaning).

    [Fact]
    public async Task Complete_carries_the_whole_frame_through_to_the_awaited_result()
    {
        var p = new GsxPendingRequests();
        var (id, t) = p.Register();
        // Plain (non-interpolated) raw string + Replace for the one dynamic value:
        // a $$"""...""" interpolated raw string hits CS9007 on the nested JSON's
        // consecutive closing braces (same trap GsxRemoteConnection.cs documents).
        var json = """
            {"type":"result","ok":true,"id":"ID_PLACEHOLDER",
             "payload":{"code":"ok","status":"prepared",
                        "gate":{"uiName":"Gate A12","gate":"A12","number":12,"bglName":"Parking 12"},
                        "warnings":["too_small"]}}
            """.Replace("ID_PLACEHOLDER", id);

        p.Complete(GsxFrame.Parse(json));
        var r = await t;

        Assert.True(r.Ok);
        Assert.NotNull(r.Frame);
        Assert.Equal(GsxFrameType.Result, r.Frame!.Type);
        Assert.Equal(JsonValueKind.Object, r.Frame.Payload.ValueKind);
        Assert.Equal("Gate A12", r.Frame.Payload.GetProperty("gate").GetProperty("uiName").GetString());
        Assert.Equal("too_small", r.Frame.Payload.GetProperty("warnings")[0].GetString());
    }

    [Fact]
    public async Task Complete_carries_the_error_object_through_too_not_just_code_and_message()
    {
        var p = new GsxPendingRequests();
        var (id, t) = p.Register();
        var json = """
            {"type":"result","ok":false,"id":"ID_PLACEHOLDER",
             "error":{"code":"assigned_to_other",
                      "gate":{"uiName":"Gate A12","gate":"A12","number":12,"bglName":"Parking 12"}}}
            """.Replace("ID_PLACEHOLDER", id);

        p.Complete(GsxFrame.Parse(json));
        var r = await t;

        Assert.False(r.Ok);
        Assert.Equal("assigned_to_other", r.ErrorCode);
        Assert.NotNull(r.Frame);
        Assert.Equal(JsonValueKind.Object, r.Frame!.Error.ValueKind);
        Assert.Equal("Gate A12", r.Frame.Error.GetProperty("gate").GetProperty("uiName").GetString());
    }

    [Fact]
    public async Task FailAll_produces_a_result_with_no_frame_to_parse()
    {
        // A locally-synthesized failure (disconnect, timeout, send error) never had
        // a live GSX frame -- Frame must stay null, never a throw or a fabricated one.
        var p = new GsxPendingRequests();
        var (_, t) = p.Register();

        p.FailAll("connection lost");
        var r = await t;

        Assert.False(r.Ok);
        Assert.Null(r.Frame);
    }

    [Fact]
    public void Unknown_or_missing_id_is_ignored()
    {
        var p = new GsxPendingRequests();
        p.Register();
        Assert.False(p.Complete(GsxFrame.Parse("""{"type":"result","ok":true,"id":"nope"}""")));
        Assert.False(p.Complete(GsxFrame.Parse("""{"type":"result","ok":true}""")));
        Assert.Equal(1, p.PendingCount);
    }

    [Fact]
    public async Task FailAll_completes_everything_as_not_ok()
    {
        var p = new GsxPendingRequests();
        var (_, t1) = p.Register();
        var (_, t2) = p.Register();

        p.FailAll("socket closed");

        Assert.False((await t1).Ok);
        Assert.Equal("socket closed", (await t2).ErrorMessage);
        Assert.Equal(0, p.PendingCount);
    }

    [Fact]
    public void PendingCount_returns_to_zero_after_FailAll()
    {
        var p = new GsxPendingRequests();
        p.Register();
        p.Register();
        p.Register();
        Assert.Equal(3, p.PendingCount);

        p.FailAll("disconnected");

        Assert.Equal(0, p.PendingCount);
    }

    [Fact]
    public async Task Store_is_reusable_after_FailAll_returns()
    {
        var p = new GsxPendingRequests();
        var (id1, t1) = p.Register();

        p.FailAll("disconnected");

        var r1 = await t1;
        Assert.False(r1.Ok);
        Assert.Equal("disconnected", r1.ErrorMessage);

        // After FailAll, the store should be fully reusable for the next connection.
        // New registrations should work normally.
        var (id2, t2) = p.Register();
        Assert.NotEqual(id1, id2);

        // And they should complete via normal Complete() call, not fail automatically.
        p.Complete(GsxFrame.Parse($"{{\"type\":\"result\",\"ok\":true,\"id\":\"{id2}\"}}"));
        var r2 = await t2;
        Assert.True(r2.Ok);

        Assert.Equal(0, p.PendingCount);
    }

    [Fact]
    public async Task Concurrent_FailAll_and_Register_do_not_deadlock()
    {
        var p = new GsxPendingRequests();

        // Create many registrations
        var registrations = Enumerable.Range(0, 50).Select(_ => p.Register()).ToList();

        // Run FailAll and Register concurrently multiple times
        var tasks = new List<Task>();

        for (int i = 0; i < 3; i++)
        {
            tasks.Add(Task.Run(() => p.FailAll($"attempt {i}")));
            tasks.AddRange(Enumerable.Range(0, 10).Select(_ => Task.Run(() => {
                var (_, task) = p.Register();
                // Don't await the task, just register it
            })));
        }

        // This should complete without deadlock or timeout
        var timeout = Task.Delay(TimeSpan.FromSeconds(5));
        var allTasks = Task.WhenAll(tasks);
        var completed = await Task.WhenAny(allTasks, timeout);

        // If we got the timeout task, we deadlocked
        Assert.Same(allTasks, completed);

        // Final FailAll to clean up any remaining registrations
        p.FailAll("final cleanup");

        // PendingCount should now be 0
        Assert.Equal(0, p.PendingCount);
    }
}
