using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

public class GsxServiceAnnouncerTests
{
    private static GsxServiceState Svc(string id, string state, int? paxDone = null, int? paxTotal = null,
                                       string display = "", string? busPhase = null) =>
        new()
        {
            Id = id, State = state, DisplayName = display == "" ? id : display,
            PaxDone = paxDone, PaxTotal = paxTotal, BusPhase = busPhase,
            StateText = $"{id} is {state}",
        };

    [Fact]
    public void First_update_is_silent_baseline()
    {
        var a = new GsxServiceAnnouncer();
        var said = a.Update(new[] { Svc("Boarding", "available") });
        Assert.Empty(said);
    }

    [Fact]
    public void State_transition_announces_once()
    {
        var a = new GsxServiceAnnouncer();
        a.Update(new[] { Svc("Boarding", "available") });
        var said = a.Update(new[] { Svc("Boarding", "performing") });
        Assert.Single(said);
        Assert.Contains("Board", said[0], StringComparison.OrdinalIgnoreCase);

        // same state again -> silence
        Assert.Empty(a.Update(new[] { Svc("Boarding", "performing") }));
    }

    [Fact]
    public void Repeated_identical_progress_is_suppressed()
    {
        var a = new GsxServiceAnnouncer();
        a.Update(new[] { Svc("Boarding", "performing", 10, 100) });
        var first = a.Update(new[] { Svc("Boarding", "performing", 20, 100) });
        Assert.Single(first);

        var repeat = a.Update(new[] { Svc("Boarding", "performing", 20, 100) });
        Assert.Empty(repeat);
    }

    [Fact]
    public void Completion_announces()
    {
        var a = new GsxServiceAnnouncer();
        a.Update(new[] { Svc("Refueling", "performing") });
        var said = a.Update(new[] { Svc("Refueling", "completed") });
        Assert.Single(said);
        Assert.Contains("complete", said[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Bus_phase_change_announces()
    {
        var a = new GsxServiceAnnouncer();
        a.Update(new[] { Svc("Deboarding", "performing", busPhase: "approaching") });
        var said = a.Update(new[] { Svc("Deboarding", "performing", busPhase: "in position") });
        Assert.Single(said);
        Assert.Contains("in position", said[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reset_re_baselines_so_next_update_is_silent()
    {
        var a = new GsxServiceAnnouncer();
        a.Update(new[] { Svc("Boarding", "available") });
        a.Reset();
        Assert.Empty(a.Update(new[] { Svc("Boarding", "performing") }));
    }
}
