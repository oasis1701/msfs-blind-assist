using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

public class GsxServiceAnnouncerTests
{
    private static GsxServiceState Svc(string id, string state, int? paxDone = null, int? paxTotal = null,
                                       string display = "", string? busPhase = null, int? bagsPercent = null) =>
        new()
        {
            Id = id, State = state, DisplayName = display == "" ? id : display,
            PaxDone = paxDone, PaxTotal = paxTotal, BusPhase = busPhase, BagsPercent = bagsPercent,
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

    [Fact]
    public void Bags_change_alone_announced_when_pax_present()
    {
        // Regression: service carries both pax and bags data; when only bags changes,
        // must announce bags (not the stale pax phrase)
        var a = new GsxServiceAnnouncer();
        // Baseline: Deboarding with pax done=150/186 and bags=40%
        a.Update(new[] { Svc("Deboarding", "performing", 150, 186, bagsPercent: 40) });

        // Update: pax unchanged, bags rise to 70%
        var said = a.Update(new[] { Svc("Deboarding", "performing", 150, 186, bagsPercent: 70) });
        Assert.Single(said);
        // Must mention bags, not passengers
        Assert.Contains("bags", said[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passenger", said[0], StringComparison.OrdinalIgnoreCase);

        // Same bags value again -> silence (no repeat announcement)
        Assert.Empty(a.Update(new[] { Svc("Deboarding", "performing", 150, 186, bagsPercent: 70) }));
    }
}
