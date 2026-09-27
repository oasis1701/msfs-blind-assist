// The Coherent MCDU client owns its view (CoherentViewOwnership) so no one-shot eval opens a
// second socket on it, and once the MCDU service exists the D / Shift+D flight-info readout
// always evaluates through the client. PR #253 review: a client that could not load its page
// agent (a damaged install, or an antivirus quarantining the file) never connects — so if it
// claimed the view anyway, that readout said "not ready" for the whole flight, where it had
// worked as a one-shot before the client existed.
//
// The ownership registry is process-wide and xUnit runs test classes in parallel, so every
// test uses a view name of its own. No Coherent view has these names, so a started client never
// opens a socket — even on a machine with the simulator running, where its page-list request
// reaches the real debugger.

using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class CoherentA32nxMcduClientViewTests
{
    private static string NewView() => "TEST_MCDU_" + Guid.NewGuid().ToString("N").ToUpperInvariant();

    private static string MissingAgent() => throw new FileNotFoundException("coherent-a32nx-mcdu-agent.js");

    /// <summary>A one-shot eval that records what it was asked and answers <paramref name="answer"/>.</summary>
    private sealed class OneShot
    {
        private readonly string _answer;
        public List<(string View, string Expression)> Calls { get; } = new();
        public OneShot(string answer = "flight-info") => _answer = answer;
        public Task<string> Eval(string view, string expression)
        {
            Calls.Add((view, expression));
            return Task.FromResult(_answer);
        }
    }

    [Fact]
    public void Without_its_agent_the_client_never_claims_its_view()
    {
        var view = NewView();
        using var client = new CoherentA32nxMcduClient(view, MissingAgent, new OneShot().Eval);

        client.Start();

        Assert.False(CoherentViewOwnership.IsClaimed(view));
    }

    [Fact]
    public void With_its_agent_the_client_claims_its_view_until_it_stops()
    {
        var view = NewView();
        using var client = new CoherentA32nxMcduClient(view, () => "/* agent */", new OneShot().Eval);

        client.Start();
        Assert.True(CoherentViewOwnership.IsClaimed(view));

        client.Stop();
        Assert.False(CoherentViewOwnership.IsClaimed(view));
    }

    [Fact]
    public async Task Without_its_agent_an_eval_goes_out_as_a_one_shot()
    {
        var view = NewView();
        var oneShot = new OneShot();
        using var client = new CoherentA32nxMcduClient(view, MissingAgent, oneShot.Eval);
        client.Start();

        string answer = await client.EvalForResultAsync("readFlightInfo()");

        Assert.Equal("flight-info", answer);
        Assert.Equal(new[] { (view, "readFlightInfo()") }, oneShot.Calls);
    }

    [Fact]
    public async Task With_its_agent_an_eval_never_goes_out_as_a_one_shot()
    {
        // It owns the view: the eval waits for its own socket ("" while that is down).
        var view = NewView();
        var oneShot = new OneShot();
        using var client = new CoherentA32nxMcduClient(view, () => "/* agent */", oneShot.Eval);
        client.Start();

        string answer = await client.EvalForResultAsync("readFlightInfo()");

        Assert.Equal("", answer);
        Assert.Empty(oneShot.Calls);
    }
}
