// Whether a "Taxiway X." change callout may be spoken immediately, must be deferred until
// the start-warning chatter window closes, or is a repeat of the taxiway already announced
// (or already waiting to be). PR #238 review follow-up (Task 5 Defect B of the group-C
// fixes).
//
// Before this gate, both taxiway-change call sites (AdvanceToNearestSegment,
// AdvanceSegment) spoke via a bare AnnounceInstruction with NO window check at all, so the
// callout cut off the 10.4 s start warning the window (_startChatterSuppressUntil) exists
// to protect -- both CLAUDE.md and docs/taxi-guidance.md claimed it already waited the
// window out, but the code never checked it.
//
// Unlike the other three window-gated callouts (turn, destination-ahead, curve -- see
// StartWarningChatterGate), the taxiway-change callout has no proximity LATCH that clears
// early and loses the callout for good if held too long: it is simply "the name of the
// taxiway the aircraft is currently on," which stays true for as long as the aircraft
// stays on that taxiway. So there is no speed/distance projection here -- only "is the
// window open" (defer vs speak now) and, once the window closes, "is the deferred name
// still the one the aircraft is on, or has the route moved on since" (speak vs discard
// silently, per the brief's resolution: losing a real taxiway name is unacceptable, but
// announcing a STALE one is worse than saying nothing).

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class TaxiwayChangeGateTests
{
    [Fact]
    public void An_empty_new_name_is_skipped()
    {
        Assert.Equal(TaxiwayChangeGate.Decision.Skip,
            TaxiwayChangeGate.Classify(null, lastAnnouncedTaxiway: "A", windowOpen: false));
        Assert.Equal(TaxiwayChangeGate.Decision.Skip,
            TaxiwayChangeGate.Classify("", lastAnnouncedTaxiway: "A", windowOpen: false));
    }

    [Fact]
    public void The_same_taxiway_already_announced_is_skipped_case_insensitively()
    {
        Assert.Equal(TaxiwayChangeGate.Decision.Skip,
            TaxiwayChangeGate.Classify("b", lastAnnouncedTaxiway: "B", windowOpen: false));
        // Still a skip even while the window is open -- a repeat is never worth deferring.
        Assert.Equal(TaxiwayChangeGate.Decision.Skip,
            TaxiwayChangeGate.Classify("B", lastAnnouncedTaxiway: "B", windowOpen: true));
    }

    [Fact]
    public void A_new_taxiway_speaks_now_when_the_window_is_closed()
    {
        Assert.Equal(TaxiwayChangeGate.Decision.SpeakNow,
            TaxiwayChangeGate.Classify("C", lastAnnouncedTaxiway: "B", windowOpen: false));
    }

    [Fact]
    public void A_new_taxiway_defers_when_the_window_is_open()
    {
        Assert.Equal(TaxiwayChangeGate.Decision.Defer,
            TaxiwayChangeGate.Classify("C", lastAnnouncedTaxiway: "B", windowOpen: true));
    }

    [Fact]
    public void A_first_taxiway_from_an_empty_last_announced_is_a_real_change()
    {
        // _lastAnnouncedTaxiway starts as "" (never "null"); an empty dedupe field must
        // never be confused with "already announced empty" and skip a real first taxiway.
        Assert.Equal(TaxiwayChangeGate.Decision.SpeakNow,
            TaxiwayChangeGate.Classify("A", lastAnnouncedTaxiway: "", windowOpen: false));
    }

    [Fact]
    public void A_deferred_name_still_current_when_the_window_closes_is_spoken()
    {
        Assert.True(TaxiwayChangeGate.IsStillCurrent(pendingTaxiwayName: "C", currentTaxiwayName: "C"));
        Assert.True(TaxiwayChangeGate.IsStillCurrent(pendingTaxiwayName: "c", currentTaxiwayName: "C"));
    }

    [Fact]
    public void A_deferred_name_the_route_has_since_moved_past_is_discarded_silently()
    {
        // The route advanced onto a THIRD taxiway (D) while the window was still open (a
        // second Defer decision would have overwritten the pending value with "D" -- but if
        // some other path spoke "D" immediately instead and left a stale "C" pending, the
        // flush must not resurrect it).
        Assert.False(TaxiwayChangeGate.IsStillCurrent(pendingTaxiwayName: "C", currentTaxiwayName: "D"));
    }

    [Fact]
    public void Nothing_pending_is_never_current()
    {
        Assert.False(TaxiwayChangeGate.IsStillCurrent(pendingTaxiwayName: null, currentTaxiwayName: "C"));
        Assert.False(TaxiwayChangeGate.IsStillCurrent(pendingTaxiwayName: "", currentTaxiwayName: "C"));
    }

    [Fact]
    public void A_current_name_that_is_gone_is_never_still_current()
    {
        // _route went null (StopGuidance/arrival) between deferring and the flush frame.
        Assert.False(TaxiwayChangeGate.IsStillCurrent(pendingTaxiwayName: "C", currentTaxiwayName: null));
    }
}
