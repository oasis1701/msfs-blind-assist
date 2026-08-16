using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The announcer's gsx.log output: state transitions, and the per-run suppression TALLY that
/// exists instead of per-tick suppression lines.
///
/// <para>
/// The tally is the whole design: the gates below run at GSX's ~1 Hz republish rate and
/// discard most of it, so logging each swallow would reproduce in the file exactly the spam
/// the gates exist to prevent — one 186-passenger deboard alone is ~580 lines — and would
/// evict the rotation window long before a post-flight report arrives. Counting answers the
/// same question ("was a gate the reason nothing was spoken?") in one line per service run.
/// </para>
/// </summary>
public class GsxServiceAnnouncerDiagnosticsTests
{
    private static GsxServiceState Svc(string id, string state, int? paxDone = null, int? paxTotal = null,
                                       string? busPhase = null, int? bagsPercent = null, string? op = null) =>
        new()
        {
            Id = id, State = state, DisplayName = id, Operator = op,
            PaxDone = paxDone, PaxTotal = paxTotal, BusPhase = busPhase, BagsPercent = bagsPercent,
            StateText = $"{id} is {state}",
        };

    private static (GsxServiceAnnouncer Announcer, List<string> Lines) Wired()
    {
        var lines = new List<string>();
        return (new GsxServiceAnnouncer { Diagnostic = lines.Add }, lines);
    }

    [Fact]
    public void With_no_sink_wired_nothing_is_emitted_and_speech_is_unchanged()
    {
        // The sink is null by default so the announcer stays pure for every other test in
        // the suite — and so diagnostics can never alter what a pilot hears.
        var a = new GsxServiceAnnouncer();
        a.Update(new[] { Svc("Refueling", "performing") });
        var said = a.Update(new[] { Svc("Refueling", "completed") });

        Assert.Equal("Refueling complete.", Assert.Single(said));
    }

    [Fact]
    public void A_state_transition_is_recorded_with_both_states()
    {
        // The line that was missing when a live refuel's lifecycle had to be reconstructed
        // from vendor documentation instead of simply read out of a log.
        var (a, lines) = Wired();
        a.Update(new[] { Svc("Refueling", "performing", op: "United Ground Express") });
        a.Update(new[] { Svc("Refueling", "completed", op: "United Ground Express") });

        string state = Assert.Single(lines, l => l.StartsWith("ev=state", StringComparison.Ordinal));
        Assert.Contains("svc=\"Refueling\"", state, StringComparison.Ordinal);
        Assert.Contains("from=\"performing\"", state, StringComparison.Ordinal);
        Assert.Contains("to=\"completed\"", state, StringComparison.Ordinal);
        Assert.Contains("operator=\"United Ground Express\"", state, StringComparison.Ordinal);
        Assert.Contains("spoke=true", state, StringComparison.Ordinal);
    }

    [Fact]
    public void An_intended_silence_is_recorded_as_such_with_its_reason()
    {
        // Without this, a service returning to requestable — which is silent BY DESIGN — is
        // indistinguishable in the log from a completion callout that went missing. It is
        // the single most likely "the announcement never came" report.
        var (a, lines) = Wired();
        a.Update(new[] { Svc("Refueling", "performing") });
        var said = a.Update(new[] { Svc("Refueling", "available") });

        Assert.Empty(said);
        string state = Assert.Single(lines, l => l.StartsWith("ev=state", StringComparison.Ordinal));
        Assert.Contains("to=\"available\"", state, StringComparison.Ordinal);
        Assert.Contains("spoke=false", state, StringComparison.Ordinal);
        Assert.Contains("silent by design", state, StringComparison.Ordinal);
    }

    [Fact]
    public void Swallowed_ticks_are_counted_and_flushed_as_one_summary_per_run()
    {
        var (a, lines) = Wired();
        a.Update(new[] { Svc("Boarding", "performing", paxDone: 0, paxTotal: 100) });

        // 1..25: the milestone gate speaks a few and swallows the rest. Not one of those
        // swallows may produce a line of its own.
        for (int done = 1; done <= 25; done++)
            a.Update(new[] { Svc("Boarding", "performing", paxDone: done, paxTotal: 100) });

        Assert.Empty(lines.Where(l => l.StartsWith("ev=hushed", StringComparison.Ordinal)));
        Assert.Empty(lines.Where(l => l.StartsWith("ev=summary", StringComparison.Ordinal)));

        a.Update(new[] { Svc("Boarding", "completed", paxDone: 25, paxTotal: 100) });

        string summary = Assert.Single(lines, l => l.StartsWith("ev=summary", StringComparison.Ordinal));
        Assert.Contains("svc=\"Boarding\"", summary, StringComparison.Ordinal);
        Assert.Contains("ticks=26", summary, StringComparison.Ordinal);   // 25 pax ticks + the transition
        Assert.Contains("gateMilestone=", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("gateMilestone=0 ", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bus_countdown_is_tallied_as_a_countdown_not_a_milestone()
    {
        var (a, lines) = Wired();
        a.Update(new[] { Svc("Boarding", "performing", busPhase: "on the way, ETA 15 secs") }); // baseline
        a.Update(new[] { Svc("Boarding", "performing", busPhase: "on the way, ETA 14 secs") }); // first phase SPEAKS
        a.Update(new[] { Svc("Boarding", "performing", busPhase: "on the way, ETA 13 secs") }); // tick, hushed
        a.Update(new[] { Svc("Boarding", "performing", busPhase: "on the way, ETA 12 secs") }); // tick, hushed
        a.Update(new[] { Svc("Boarding", "completed", busPhase: "on the way, ETA 12 secs") });

        string summary = Assert.Single(lines, l => l.StartsWith("ev=summary", StringComparison.Ordinal));
        Assert.Contains("gateCountdown=2", summary, StringComparison.Ordinal);
        Assert.Contains("gateMilestone=0", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void The_fuel_throttle_is_tallied_separately()
    {
        var t0 = new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
        var (a, lines) = Wired();
        GsxServiceState Fuel(double current) => new()
        {
            Id = "Refueling", DisplayName = "Refuel", State = "performing",
            FuelCurrent = current, FuelUnit = "kg", StateText = "refuelling",
        };

        a.Update(new[] { Fuel(100) }, t0);
        a.Update(new[] { Fuel(200) }, t0.AddSeconds(2));   // spoken (first reading)
        a.Update(new[] { Fuel(300) }, t0.AddSeconds(4));   // throttled
        a.Update(new[] { Fuel(400) }, t0.AddSeconds(6));   // throttled
        a.Update(new[] { Svc("Refueling", "completed") }, t0.AddSeconds(8));

        string summary = Assert.Single(lines, l => l.StartsWith("ev=summary", StringComparison.Ordinal));
        Assert.Contains("gateThrottle=2", summary, StringComparison.Ordinal);
        Assert.Contains("spoke=", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void The_tick_counts_reconcile_even_when_one_tick_is_charged_to_two_gates()
    {
        // A real boarding row carries detail.pax AND detail.bagsPercent together, and the pax
        // branch falls through to bags, so ONE tick can be rejected twice. ticks/spoke/silent
        // must still reconcile — an earlier version reported those double charges under a
        // name that implied ticks, producing "ticks=187 … 246 suppressed", which reads as a
        // gate ~8x more aggressive than it is and would send someone to tune a good threshold.
        var (a, lines) = Wired();
        a.Update(new[] { Svc("Boarding", "performing", paxDone: 0, paxTotal: 100, bagsPercent: 0) });
        for (int i = 1; i <= 12; i++)
            a.Update(new[] { Svc("Boarding", "performing", paxDone: i, paxTotal: 100, bagsPercent: i) });
        a.Update(new[] { Svc("Boarding", "completed", paxDone: 12, paxTotal: 100, bagsPercent: 12) });

        string summary = Assert.Single(lines, l => l.StartsWith("ev=summary", StringComparison.Ordinal));

        int Field(string key) => int.Parse(summary.Split(key + "=")[1].Split(' ')[0].Trim());
        int ticks = Field("ticks"), spoke = Field("spoke"), silent = Field("silent");

        Assert.Equal(ticks - spoke, silent);
        Assert.True(spoke <= ticks, "a run cannot speak more often than it ticked");
        // The gate counters are a DIFFERENT denominator and may legitimately exceed `silent`.
        Assert.True(Field("gateMilestone") >= silent,
                    "pax and bags reject independently, so the gate tally should exceed the silent-tick count here");
    }

    [Fact]
    public void A_run_interrupted_by_a_reset_still_flushes_its_tally()
    {
        // A disconnect mid-boarding is exactly when the tally is worth having.
        var (a, lines) = Wired();
        a.Update(new[] { Svc("Boarding", "performing", paxDone: 0, paxTotal: 100) });
        for (int done = 1; done <= 5; done++)
            a.Update(new[] { Svc("Boarding", "performing", paxDone: done, paxTotal: 100) });

        a.Reset();

        string summary = Assert.Single(lines, l => l.StartsWith("ev=summary", StringComparison.Ordinal));
        Assert.Contains("at=\"reset\"", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_baseline_pass_emits_nothing_at_all()
    {
        // Connecting mid-turnaround is silent by design; the log must not imply otherwise.
        var (a, lines) = Wired();
        a.Update(new[] { Svc("Boarding", "performing", paxDone: 40, paxTotal: 100) });

        Assert.Empty(lines);
    }

    [Fact]
    public void An_idle_service_produces_no_summary_noise()
    {
        // A service that never ticked has nothing to report; a zero-tick summary per
        // transition would be pure noise across the dozen rows GSX publishes.
        var (a, lines) = Wired();
        a.Update(new[] { Svc("Catering", "available") });
        a.Update(new[] { Svc("Catering", "performing") });

        Assert.Empty(lines.Where(l => l.StartsWith("ev=summary", StringComparison.Ordinal)));
        Assert.Single(lines, l => l.StartsWith("ev=state", StringComparison.Ordinal));
    }
}
