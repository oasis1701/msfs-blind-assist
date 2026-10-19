// Characterization tests for SimTabletSimBriefImport (Services/SimTabletSimBriefImport.cs): the
// order of the reader calls the SimBrief import makes on the sim tablet, and what it says when a
// step fails. A scripted stand-in plays the tablet: each reader call is answered from a queue, the
// way the live Flight Planner answered on 2026-10-19.

using MSFSBlindAssist.Models;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class SimTabletSimBriefImportTests
{
    private sealed class FakeTablet
    {
        public readonly List<string> Calls = new();
        private readonly Dictionary<string, Queue<string>> _answers = new();
        private readonly Dictionary<string, string> _last = new();

        // Answers for a reader member ("route.open", "route.state", ...), used in order; the last
        // one repeats once the queue runs dry.
        public FakeTablet On(string member, params string[] answers)
        {
            _answers[member] = new Queue<string>(answers);
            return this;
        }

        public Task<string> Eval(string expression)
        {
            int at = expression.IndexOf(SimTabletSimBriefImport.AgentGlobal + ".route.", StringComparison.Ordinal);
            string call = at < 0 ? expression : expression.Substring(at + SimTabletSimBriefImport.AgentGlobal.Length + 1);
            call = call.Substring(0, call.LastIndexOf(" : ''", StringComparison.Ordinal));
            Calls.Add(call);
            string member = call.Substring(0, call.IndexOf('('));
            if (_answers.TryGetValue(member, out var q) && q.Count > 0) _last[member] = q.Dequeue();
            return Task.FromResult(_last.TryGetValue(member, out var a) ? a : "");
        }
    }

    private static SimBriefOFP Ofp(string depRwy = "31", string arrRwy = "01") => new()
    {
        OriginIcao = "KFFC", DestIcao = "KSAV", Route = "DCT SOONE DCT NOKIE DCT", OriginRunway = depRwy, DestRunway = arrRwy
    };

    private static string State(string arrRwy, string approach = "None", bool airports = true) =>
        "{\"ok\":true,\"atlas\":true,\"routeShown\":true,\"airports\":" + (airports
            ? "[{\"icao\":\"KFFC\",\"runway\":\"runway 31\",\"runwayKey\":\"31\",\"runways\":[\"runway 13\",\"runway 31\"]}," +
              "{\"icao\":\"KSAV\",\"runway\":\"runway " + arrRwy + "\",\"runwayKey\":\"" + SimTabletRoute.RunwayKey(arrRwy) + "\"," +
              "\"runways\":[\"runway 01\",\"runway 10\",\"runway 19\",\"runway 28\"]}]"
            : "[]") +
        ",\"procedures\":[\"None\",\"None\",\"" + approach + "\"],\"waypoints\":[\"SOONE\",\"NOKIE\"]}";

    private const string Ok = "{\"ok\":true}";
    private static readonly Func<int, CancellationToken, Task> NoWait = (_, _) => Task.CompletedTask;

    [Fact]
    public async Task TheFullImport_OpensTypesSavesWaitsAndSetsTheArrivalRunway()
    {
        var tablet = new FakeTablet()
            .On("route.open", "{\"ok\":true,\"done\":false}", "{\"ok\":true,\"done\":true}")
            .On("route.enter", Ok)
            .On("route.save", "{\"ok\":false,\"error\":\"save-disabled\"}", Ok)
            .On("route.state", State("10", airports: false), State("10"), State("10"), State("01", "ILS 1 via Direct"))
            .On("route.setRunway", "{\"ok\":true,\"picked\":\"runway 01\"}");
        var r = await new SimTabletSimBriefImport(tablet.Eval, NoWait).RunAsync(Ofp());

        Assert.True(r.Success, r.Message);
        Assert.Equal("SimBrief plan loaded: KFFC runway 31 to KSAV runway 01, 2 waypoints. Approach: ILS 1 via Direct. " +
                     "Use Send to avionics when you are ready.", r.Message);
        Assert.Equal(new[]
        {
            "route.open()", "route.open()",
            "route.enter(\"KFFC DCT SOONE DCT NOKIE DCT KSAV\")",
            "route.save()", "route.save()",
            "route.state()", "route.state()",                 // airports not in, then in
            "route.setRunway(1,\"01\")",                      // the departure's 31 already matched
            "route.state()", "route.state()",                 // still 10, then 01: the pick shows a poll late
            "route.state()"                                   // the reading the summary speaks
        }, tablet.Calls);
    }

    [Fact]
    public async Task APlanAlreadyInTheFlightPlanner_SkipsSave_AndSaysSo()
    {
        var tablet = new FakeTablet()
            .On("route.open", "{\"ok\":true,\"done\":true}")
            .On("route.enter", "{\"ok\":true,\"unchanged\":true}")
            .On("route.state", State("01", "ILS 1 via Direct"));
        var r = await new SimTabletSimBriefImport(tablet.Eval, NoWait).RunAsync(Ofp());

        Assert.True(r.Success, r.Message);
        Assert.DoesNotContain("route.save()", tablet.Calls);
        Assert.Equal("SimBrief plan loaded: KFFC runway 31 to KSAV runway 01, 2 waypoints. Approach: ILS 1 via Direct. " +
                     "The Flight Planner already had this route. Use Send to avionics when you are ready.", r.Message);
    }

    [Fact]
    public async Task ASaveThatNeverEnables_ClosesTheRouteBox_AndFails()
    {
        var tablet = new FakeTablet()
            .On("route.open", "{\"ok\":true,\"done\":true}")
            .On("route.enter", Ok)
            .On("route.save", "{\"ok\":false,\"error\":\"save-disabled\"}")
            .On("route.closeBox", "{\"ok\":true,\"done\":true}");
        var r = await new SimTabletSimBriefImport(tablet.Eval, NoWait).RunAsync(Ofp());

        Assert.False(r.Success);
        Assert.Equal("The sim tablet did not take the route.", r.Message);
        Assert.Equal(SimTabletSimBriefImport.SaveTries, tablet.Calls.Count(c => c == "route.save()"));
        Assert.Equal("route.closeBox()", tablet.Calls[^1]);
    }

    [Fact]
    public async Task ATabletThatDoesNotAnswer_IsNotConnected()
    {
        var r = await new SimTabletSimBriefImport(_ => Task.FromResult(""), NoWait).RunAsync(Ofp());
        Assert.False(r.Success);
        Assert.Equal("The sim tablet is not connected.", r.Message);
    }

    [Fact]
    public async Task ALockedPlan_IsRefusedAloud_AndNothingIsSaved()
    {
        var tablet = new FakeTablet()
            .On("route.open", "{\"ok\":true,\"done\":true}")
            .On("route.enter", "{\"ok\":false,\"error\":\"locked\"}");
        var r = await new SimTabletSimBriefImport(tablet.Eval, NoWait).RunAsync(Ofp());
        Assert.False(r.Success);
        Assert.Equal("The sim tablet's flight plan is locked, so a route cannot be entered now.", r.Message);
        Assert.DoesNotContain("route.save()", tablet.Calls);
    }

    [Fact]
    public async Task NoFlightPlannerOnTheDock_IsSaid()
    {
        var tablet = new FakeTablet().On("route.open", "{\"ok\":false,\"error\":\"no-flight-planner\"}");
        var r = await new SimTabletSimBriefImport(tablet.Eval, NoWait).RunAsync(Ofp());
        Assert.Equal("The sim tablet has no Flight Planner on its dock.", r.Message);
    }

    [Fact]
    public async Task ARouteThatNeverLoads_NamesBothAirports()
    {
        var tablet = new FakeTablet()
            .On("route.open", "{\"ok\":true,\"done\":true}")
            .On("route.enter", Ok)
            .On("route.save", Ok)
            .On("route.state", State("10", airports: false));
        var r = await new SimTabletSimBriefImport(tablet.Eval, NoWait).RunAsync(Ofp());
        Assert.False(r.Success);
        Assert.Equal("The sim tablet did not load the route from KFFC to KSAV.", r.Message);
        Assert.Equal(SimTabletSimBriefImport.LoadTries, tablet.Calls.Count(c => c == "route.state()"));
    }

    [Fact]
    public async Task ARunwayTheTabletDoesNotList_IsSaid_AndNotPicked()
    {
        var tablet = new FakeTablet()
            .On("route.open", "{\"ok\":true,\"done\":true}")
            .On("route.enter", Ok)
            .On("route.save", Ok)
            .On("route.state", State("10"));
        var r = await new SimTabletSimBriefImport(tablet.Eval, NoWait).RunAsync(Ofp(arrRwy: "05"));
        Assert.True(r.Success);
        Assert.Contains("SimBrief planned runway 05 at KSAV, which the tablet does not list.", r.Message);
        Assert.StartsWith("SimBrief plan loaded: KFFC runway 31 to KSAV runway 10,", r.Message);
        Assert.DoesNotContain(tablet.Calls, c => c.StartsWith("route.setRunway", StringComparison.Ordinal));
    }

    [Fact]
    public async Task APlanWithoutRunways_PicksNone()
    {
        var tablet = new FakeTablet()
            .On("route.open", "{\"ok\":true,\"done\":true}")
            .On("route.enter", Ok)
            .On("route.save", Ok)
            .On("route.state", State("10"));
        var r = await new SimTabletSimBriefImport(tablet.Eval, NoWait).RunAsync(Ofp(depRwy: "", arrRwy: ""));
        Assert.True(r.Success);
        Assert.DoesNotContain(tablet.Calls, c => c.StartsWith("route.setRunway", StringComparison.Ordinal));
    }
}
