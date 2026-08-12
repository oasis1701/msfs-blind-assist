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
