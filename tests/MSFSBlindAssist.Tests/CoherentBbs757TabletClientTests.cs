using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The parts of the 757 tablet client that need no simulator: when an installed agent counts as
/// deaf and is installed again (an aircraft reload can hand the client a view whose scripts have
/// not run yet: it would freeze CDU 1 on its seed and never get CDU 2), which keys it queues, and
/// what it answers with nothing to read.
/// </summary>
public class CoherentBbs757TabletClientTests
{
    private static readonly DateTime Now = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(false, true, null, true)]        // no listener at all
    [InlineData(false, false, 0, true)]
    [InlineData(true, true, null, false)]        // healthy
    [InlineData(true, false, null, false)]       // not ready, first seen now
    [InlineData(true, false, 4900, false)]       // not ready, still within the wait
    [InlineData(true, false, 5000, true)]        // not ready for 5 s
    [InlineData(true, false, 60000, true)]
    public void A_deaf_agent_is_installed_again(bool listening, bool ready, int? notReadyForMs, bool expected)
    {
        DateTime? since = notReadyForMs is { } ms ? Now.AddMilliseconds(-ms) : null;

        Assert.Equal(expected, CoherentBbs757TabletClient.NeedsReinstall(listening, ready, since, Now));
    }

    [Fact]
    public void Only_cockpit_keys_for_cdu_1_or_2_are_queued()
    {
        using var client = new CoherentBbs757TabletClient(() => "");

        client.PressKey(1, "RTE");
        client.PressKey(2, "KEYCLR");
        Assert.Equal(2, client.PendingKeyCount);

        client.PressKey(3, "RTE");
        client.PressKey(0, "RTE");
        client.PressKey(1, "rte");
        client.PressKey(1, "CLR_Long");
        client.PressKey(1, "RTE');x('");
        client.PressKey(1, null!);
        Assert.Equal(2, client.PendingKeyCount);
    }

    [Fact]
    public void The_tablet_agent_caps_its_listener_registrations_and_is_ready_only_once_both_handlers_are_on()
    {
        string agent = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-bbs757-tablet-agent.js"));
        string code = System.Text.RegularExpressions.Regex.Replace(agent, @"//[^\n]*", "");

        // A permanently deaf view keeps its last agent after three registrations.
        Assert.Contains("var MAX_REGISTRATIONS = 3;", code);
        Assert.Contains("registrations >= MAX_REGISTRATIONS", code);
        // Strict ES5: no function declaration inside a block.
        Assert.DoesNotContain("function hook", code);
        Assert.Contains("var hook = function", code);
        // ready follows both .on() calls, inside the hook's try.
        int on2 = code.IndexOf("'BBS_CDU_DISPLAY_2'", StringComparison.Ordinal);
        int ready = code.IndexOf("state.ready = true", StringComparison.Ordinal);
        Assert.True(on2 > 0 && ready > on2, "state.ready must be set after both handlers are on");
        Assert.DoesNotContain("=>", code);
        Assert.DoesNotMatch(@"\b(let|const|class)\s", code);
    }

    [Fact]
    public async Task With_nothing_to_read_it_is_not_readable_and_reads_no_navigraph_state()
    {
        using var client = new CoherentBbs757TabletClient(() => "");

        Assert.False(client.IsReadable);
        Assert.False(client.HasFrame(1));
        Assert.Equal((false, (string?)null, (string?)null), await client.GetNavigraphAsync());
        Assert.Null(await client.ScrapeAsync(2, CancellationToken.None));
    }

    [Fact]
    public async Task A_disposed_client_scrapes_nothing()
    {
        // After a switch away the definition hands out its disposed client: a background read
        // through it answers null, never throws.
        var client = new CoherentBbs757TabletClient(() => "");
        client.Dispose();

        Assert.Null(await client.ScrapeAsync(2, CancellationToken.None));
        Assert.Null(await client.ScrapeAsync(1, new CancellationToken(canceled: true)));
    }

    [Fact]
    public void PressExpression_carries_the_hold()
    {
        Assert.Equal("__MSFSBA_BBS757.press(2,\"KEYCLR\",1300)", CoherentBbs757TabletClient.PressExpression(2, "KEYCLR", 1300));
        Assert.Equal("__MSFSBA_BBS757.press(1,\"L1\",250)", CoherentBbs757TabletClient.PressExpression(1, "L1", CoherentBbs757TabletClient.KeyHoldMs));
        // Clamped to 100-1500 ms, as the agent clamps it.
        Assert.Equal("__MSFSBA_BBS757.press(2,\"KEYCLR\",1500)", CoherentBbs757TabletClient.PressExpression(2, "KEYCLR", 5000));
        Assert.Equal("__MSFSBA_BBS757.press(2,\"KEYCLR\",100)", CoherentBbs757TabletClient.PressExpression(2, "KEYCLR", 10));
    }

    [Fact]
    public void A_key_with_its_own_hold_is_queued_like_any_other()
    {
        using var client = new CoherentBbs757TabletClient(() => "");

        client.PressKey(2, "KEYCLR", 1300);
        client.PressKey(2, "KEYA");
        client.PressKey(2, "KEYCLR_LONG", 1300);   // not a key: not queued

        Assert.Equal(2, client.PendingKeyCount);
    }

    [Fact]
    public void Cycle_is_hold_plus_gap_clamped()
    {
        Assert.Equal(200, CoherentBbs757TabletClient.CycleMs(100, 100));
        Assert.Equal(CoherentBbs757TabletClient.KeyCycleMs, CoherentBbs757TabletClient.CycleMs(CoherentBbs757TabletClient.KeyHoldMs, CoherentBbs757TabletClient.KeyGapMs));
        // Hold 100-1500, gap 50-1000.
        Assert.Equal(100 + 50, CoherentBbs757TabletClient.CycleMs(10, 0));
        Assert.Equal(1500 + 1000, CoherentBbs757TabletClient.CycleMs(9000, 9000));
    }

    [Fact]
    public void Standard_keys_keep_250_and_400()
    {
        using var client = new CoherentBbs757TabletClient(() => "");   // never started

        client.PressKey(1, "RTE");
        client.PressKey(2, "KEYCLR", 1300);
        client.PressKey(2, "KEYA", Bbs757CduTyping.HoldMs, Bbs757CduTyping.GapMs);
        client.PressKey(2, "KEYB", 10, 5000);   // clamped
        client.PressKey(2, "NOT_A_KEY", 100, 100);   // not queued

        var queued = client.DrainQueuedKeysForTests();
        Assert.Equal(new[]
        {
            (1, "RTE", 250, 400),
            (2, "KEYCLR", 1300, 400),
            (2, "KEYA", 100, 300),
            (2, "KEYB", 100, 1000),
        }, queued);
        Assert.Equal(0, client.PendingKeyCount);
    }

    [Fact]
    public void Agent_press_takes_a_hold_argument()
    {
        string code = AgentCode();

        Assert.Contains("A.press = function (cdu, key, holdMs)", code);
        Assert.Contains("var HOLD_MS = 250;", code);
        Assert.Contains("var MIN_HOLD_MS = 100;", code);
        Assert.Contains("var MAX_HOLD_MS = 1500;", code);
        // A missing or non-numeric hold is HOLD_MS; the release waits the clamped hold.
        Assert.Contains("typeof holdMs === 'number' && isFinite(holdMs) ? holdMs : HOLD_MS", code);
        Assert.Matches(@"(?s)setTimeout\(function \(\) \{.*?\}, hold\);", code);
        Assert.DoesNotContain("}, HOLD_MS);", code);
    }

    [Fact]
    public void Agent_version_check()
    {
        Assert.False(CoherentBbs757TabletClient.AgentIsCurrent(CoherentBbs757TabletClient.AgentVersion - 1));
        Assert.True(CoherentBbs757TabletClient.AgentIsCurrent(CoherentBbs757TabletClient.AgentVersion));
        Assert.False(CoherentBbs757TabletClient.AgentIsCurrent(null));

        // The agent the client installs is the version it asks for, and its scrape says so: an
        // agent older than the client's would be installed again on every poll.
        string code = AgentCode();
        Assert.Contains("var VERSION = " + CoherentBbs757TabletClient.AgentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";", code);
        Assert.Contains("version: A.version", code);
    }

    [Fact]
    public void Watch_expression_is_json()
    {
        string expr = CoherentBbs757TabletClient.WatchExpression(new[] { "B757_OVHD_BATTERY_BUTTON", "ODD'NAME\"X" });

        // The names travel as one JSON array literal, so a quote in a name cannot end the string.
        Assert.StartsWith("window.__MSFSBA_BBS757 && __MSFSBA_BBS757.watch ? __MSFSBA_BBS757.watch(", expr);
        int open = expr.IndexOf("watch([", StringComparison.Ordinal) + "watch(".Length;
        int close = expr.LastIndexOf("])", StringComparison.Ordinal) + 1;
        var names = System.Text.Json.JsonSerializer.Deserialize<string[]>(expr.Substring(open, close - open));
        Assert.Equal(new[] { "B757_OVHD_BATTERY_BUTTON", "ODD'NAME\"X" }, names);
    }

    [Fact]
    public void Agent_has_watch_and_changes()
    {
        string code = AgentCode();

        Assert.Contains("A.watch = function (names)", code);
        Assert.Contains("A.changes = function ()", code);
        Assert.Contains("SimVar.GetSimVarValue('L:' + ", code);
        // The first call after watch is the baseline: it returns no changes.
        Assert.Contains("watch.baseline", code);
        Assert.Contains("watching: ", code);
    }

    [Fact]
    public async Task Reinstall_resends_the_list()
    {
        using var client = new CoherentBbs757TabletClient(() => "");
        var sent = new List<string>();
        client.EvalForTests = expr =>
        {
            sent.Add(expr);
            if (expr.Contains(".watch(", StringComparison.Ordinal)) return Task.FromResult("ok");
            return Task.FromResult("{\"ok\":true,\"watching\":true,\"seq\":1,\"changes\":{}}");
        };
        client.SetWatchList(new[] { "A_LIGHT", "B_SWITCH" });

        await client.PollWatchAsync(CancellationToken.None);
        await client.PollWatchAsync(CancellationToken.None);
        Assert.Equal(1, sent.Count(e => e.Contains(".watch(", StringComparison.Ordinal)));

        // A new agent (an aircraft reload) holds no list: the next poll sends it again.
        client.AgentInstalled();
        await client.PollWatchAsync(CancellationToken.None);
        Assert.Equal(2, sent.Count(e => e.Contains(".watch(", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task An_agent_that_lost_its_list_gets_it_again()
    {
        using var client = new CoherentBbs757TabletClient(() => "");
        int watches = 0;
        bool watching = true;
        client.EvalForTests = expr =>
        {
            if (expr.Contains(".watch(", StringComparison.Ordinal)) { watches++; return Task.FromResult("ok"); }
            return Task.FromResult("{\"ok\":true,\"watching\":" + (watching ? "true" : "false") + ",\"seq\":1,\"changes\":{}}");
        };
        client.SetWatchList(new[] { "A_LIGHT" });

        await client.PollWatchAsync(CancellationToken.None);
        watching = false;
        await client.PollWatchAsync(CancellationToken.None);   // answers "not watching"
        await client.PollWatchAsync(CancellationToken.None);
        Assert.Equal(2, watches);
    }

    [Fact]
    public async Task Changes_are_raised_and_an_empty_set_is_not()
    {
        // The client posts to the context it was made on: made with none, it raises inline.
        var saved = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        using var client = new CoherentBbs757TabletClient(() => "");
        SynchronizationContext.SetSynchronizationContext(saved);
        string answer = "{\"ok\":true,\"watching\":true,\"seq\":1,\"changes\":{}}";
        client.EvalForTests = expr => Task.FromResult(expr.Contains(".watch(", StringComparison.Ordinal) ? "ok" : answer);
        var raised = new List<IReadOnlyDictionary<string, double>>();
        client.LvarsChanged += c => raised.Add(c);
        client.SetWatchList(new[] { "A_LIGHT", "B_SWITCH" });

        await client.PollWatchAsync(CancellationToken.None);
        Assert.Empty(raised);

        answer = "{\"ok\":true,\"watching\":true,\"seq\":2,\"changes\":{\"A_LIGHT\":1,\"B_SWITCH\":2.5}}";
        await client.PollWatchAsync(CancellationToken.None);
        var only = Assert.Single(raised);
        Assert.Equal(1.0, only["A_LIGHT"]);
        Assert.Equal(2.5, only["B_SWITCH"]);
    }

    [Fact]
    public async Task No_list_sends_nothing()
    {
        using var client = new CoherentBbs757TabletClient(() => "");
        int evals = 0;
        client.EvalForTests = _ => { evals++; return Task.FromResult(""); };

        await client.PollWatchAsync(CancellationToken.None);
        client.SetWatchList(Array.Empty<string>());
        await client.PollWatchAsync(CancellationToken.None);
        Assert.Equal(0, evals);
    }

    /// <summary>The tablet agent's source with its line comments removed.</summary>
    private static string AgentCode()
    {
        string agent = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-bbs757-tablet-agent.js"));
        return System.Text.RegularExpressions.Regex.Replace(agent, @"//[^\n]*", "");
    }
}
