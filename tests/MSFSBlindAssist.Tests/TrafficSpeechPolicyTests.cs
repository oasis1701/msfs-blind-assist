// TrafficSpeechPolicy — which ground-traffic callouts may interrupt, and how many lines go out per
// evaluation. PR #247 review R7/L4/L11: "one interrupt per evaluation" did not stop the NEXT
// evaluation (a second later) cutting a "Stop" off, and every interrupt cancelled the one-shot
// runway-status / queue lines queued before it. Owner decision: only safety callouts interrupt.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class TrafficSpeechPolicyTests
{
    private static readonly DateTime Now = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

    private static TrafficCallout C(TrafficCalloutKind kind, double dist = 100, string? msg = null)
        => new(kind, dist, msg ?? kind.ToString(), () => { });

    private static SpeechPlan Plan(DateTime? lastAlert = null, bool suppressed = false, params TrafficCallout[] c)
        => TrafficSpeechPolicy.Plan(c, Now, lastAlert ?? DateTime.MinValue, suppressed);

    /// <summary>Every line a plan hands to the announcer: the caller speaks, and latches, exactly these.</summary>
    private static IEnumerable<TrafficCallout> Planned(SpeechPlan p)
        => new[] { p.Interrupt, p.Alert }.OfType<TrafficCallout>().Concat(p.Info);

    [Theory]
    [InlineData(TrafficCalloutKind.Warning, true)]
    [InlineData(TrafficCalloutKind.RunwayCritical, true)]
    [InlineData(TrafficCalloutKind.Caution, true)]
    [InlineData(TrafficCalloutKind.Converging, false)]
    [InlineData(TrafficCalloutKind.OnRoute, false)]
    [InlineData(TrafficCalloutKind.Awareness, false)]
    [InlineData(TrafficCalloutKind.QueueMoving, false)]
    [InlineData(TrafficCalloutKind.MoveUp, false)]
    [InlineData(TrafficCalloutKind.RunwayInfo, false)]
    [InlineData(TrafficCalloutKind.QueuePosition, false)]
    public void Only_safety_callouts_interrupt(TrafficCalloutKind kind, bool interrupts)
        => Assert.Equal(interrupts, TrafficSpeechPolicy.Interrupts(kind));

    [Fact]
    public void Stop_outranks_a_runway_event_which_outranks_slow_down()
    {
        var p = Plan(null, false, C(TrafficCalloutKind.Caution, 50), C(TrafficCalloutKind.RunwayCritical, 5000),
                     C(TrafficCalloutKind.Warning, 200));
        Assert.Equal(TrafficCalloutKind.Warning, p.Interrupt!.Kind);
        p = Plan(null, false, C(TrafficCalloutKind.Caution, 50), C(TrafficCalloutKind.RunwayCritical, 5000));
        Assert.Equal(TrafficCalloutKind.RunwayCritical, p.Interrupt!.Kind);
    }

    [Fact]
    public void Between_two_of_a_kind_the_nearer_wins()
        => Assert.Equal("near", Plan(null, false,
            C(TrafficCalloutKind.Warning, 240, "far"), C(TrafficCalloutKind.Warning, 90, "near")).Interrupt!.Message);

    [Fact]
    public void One_alert_line_per_evaluation_the_most_urgent()
    {
        var p = Plan(null, false, C(TrafficCalloutKind.Awareness, 50), C(TrafficCalloutKind.Converging, 500),
                     C(TrafficCalloutKind.QueueMoving, 10), C(TrafficCalloutKind.OnRoute, 20));
        Assert.Null(p.Interrupt);
        Assert.Equal(TrafficCalloutKind.Converging, p.Alert!.Kind);
    }

    [Fact]
    public void Alert_lines_are_spaced()
    {
        Assert.Null(Plan(Now.AddMilliseconds(-2999), false, C(TrafficCalloutKind.Awareness)).Alert);
        Assert.NotNull(Plan(Now.AddMilliseconds(-3000), false, C(TrafficCalloutKind.Awareness)).Alert);
    }

    [Fact]
    public void Informational_lines_all_go_out_and_are_never_spaced()
    {
        var p = Plan(Now, false, C(TrafficCalloutKind.RunwayInfo), C(TrafficCalloutKind.QueuePosition),
                     C(TrafficCalloutKind.RunwayInfo));
        Assert.Equal(3, p.Info.Count);
        Assert.Null(p.Alert);
    }

    [Fact]
    public void While_speech_is_suppressed_only_the_interrupt_goes_out()
    {
        var p = Plan(null, true, C(TrafficCalloutKind.Warning), C(TrafficCalloutKind.Awareness),
                     C(TrafficCalloutKind.RunwayInfo));
        Assert.NotNull(p.Interrupt);
        Assert.Null(p.Alert);
        Assert.Empty(p.Info);
    }

    [Fact]
    public void Nothing_to_say_is_an_empty_plan()
    {
        var p = Plan(null, false);
        Assert.Null(p.Interrupt);
        Assert.Null(p.Alert);
        Assert.Empty(p.Info);
    }

    [Fact]
    public void A_less_urgent_interrupt_never_cuts_a_more_urgent_one_off_within_three_seconds()
    {
        // "Stop" spoken 1 s ago; this evaluation has a "Slow down" for another aircraft. It is withheld: not the
        // interrupt, and never moved to the queued alert slot (PR #247 focused re-review I1) — so it is neither
        // spoken nor latched, and the next sweep offers it again.
        var slowDown = C(TrafficCalloutKind.Caution);
        var p = TrafficSpeechPolicy.Plan(new[] { slowDown }, Now, DateTime.MinValue, false,
            TrafficCalloutKind.Warning, Now.AddSeconds(-1));
        Assert.Null(p.Interrupt);
        Assert.Null(p.Alert);                  // no ordinary alert candidate: the slot stays empty
        Assert.DoesNotContain(slowDown, Planned(p));
        p = TrafficSpeechPolicy.Plan(new[] { slowDown }, Now, DateTime.MinValue, false,
            TrafficCalloutKind.Warning, Now.AddMilliseconds(-TrafficSpeechPolicy.InterruptProtectMs));
        Assert.Same(slowDown, p.Interrupt);
    }

    [Fact]
    public void A_more_urgent_interrupt_may_cut_in()
        => Assert.NotNull(TrafficSpeechPolicy.Plan(new[] { C(TrafficCalloutKind.Warning) }, Now, DateTime.MinValue, false,
            TrafficCalloutKind.Caution, Now.AddSeconds(-1)).Interrupt);

    [Fact]
    public void An_equally_urgent_interrupt_does_not_cut_in_within_three_seconds()
    {
        // PR #247 integration follow-up R3: a second "Stop" (for another aircraft) no longer cuts the first one
        // off a second later. Withheld, it is never moved to the queued alert slot (PR #247 focused re-review
        // I1): the slot goes to the best ordinary alert candidate, and the "Stop" is not planned at all.
        var stop = C(TrafficCalloutKind.Warning, 90);
        var onRoute = C(TrafficCalloutKind.OnRoute, 400);
        var p = TrafficSpeechPolicy.Plan(new[] { stop, onRoute }, Now, DateTime.MinValue, false,
            TrafficCalloutKind.Warning, Now.AddSeconds(-1));
        Assert.Null(p.Interrupt);
        Assert.Same(onRoute, p.Alert);
        Assert.DoesNotContain(stop, Planned(p));
    }

    [Fact]
    public void An_equally_urgent_interrupt_cuts_in_once_the_protect_window_has_passed()
        => Assert.NotNull(TrafficSpeechPolicy.Plan(new[] { C(TrafficCalloutKind.Warning) }, Now, DateTime.MinValue, false,
            TrafficCalloutKind.Warning, Now.AddMilliseconds(-TrafficSpeechPolicy.InterruptProtectMs)).Interrupt);

    [Fact]
    public void A_withheld_interrupt_is_offered_again_and_interrupts_once_the_protect_window_has_passed()
    {
        // Nothing latched the withheld "Stop", so every sweep offers the same candidate again: inside the window it
        // is still not planned (not the interrupt, not the alert line); once InterruptProtectMs has passed since
        // the last interrupt it IS the interrupt (PR #247 focused re-review I1).
        var stop = C(TrafficCalloutKind.Warning, 90);
        var candidates = new[] { stop, C(TrafficCalloutKind.Awareness, 500) };
        var lastInterruptUtc = Now.AddSeconds(-2);

        var p = TrafficSpeechPolicy.Plan(candidates, Now, DateTime.MinValue, false,
            TrafficCalloutKind.Warning, lastInterruptUtc);
        Assert.DoesNotContain(stop, Planned(p));

        p = TrafficSpeechPolicy.Plan(candidates, lastInterruptUtc.AddMilliseconds(TrafficSpeechPolicy.InterruptProtectMs),
            DateTime.MinValue, false, TrafficCalloutKind.Warning, lastInterruptUtc);
        Assert.Same(stop, p.Interrupt);
    }

    [Fact]
    public void Planning_never_marks_anything_spoken()
    {
        bool marked = false;
        TrafficSpeechPolicy.Plan(new[] { new TrafficCallout(TrafficCalloutKind.Warning, 1, "x", () => marked = true) },
            Now, DateTime.MinValue, false);
        Assert.False(marked);   // the caller runs OnEmitted only for what it actually speaks
    }
}
