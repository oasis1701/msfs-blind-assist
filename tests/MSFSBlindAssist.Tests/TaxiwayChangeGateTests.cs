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
//
// PR #238 review, Important C (re-fix): a THIRD silent case was added alongside "the route
// moved on" -- a recalculation can adopt a new route that starts on the very taxiway a
// deferral names, without ever going through this gate's Classify/Defer path, and stamps
// the dedupe field itself as part of its own "Route changed" sentence. ShouldSpeakDeferred
// is IsStillCurrent plus that extra "not already spoken by someone else" check.

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

    [Fact]
    public void Two_missing_names_are_never_still_current_either()
    {
        // Minor 6: IsStillCurrent's second IsNullOrEmpty guard (on currentTaxiwayName) was
        // removed as provably redundant given the first -- a non-empty pendingTaxiwayName can
        // never .Equals a null/empty currentTaxiwayName, so the first guard plus the .Equals
        // call alone already implies it. The FIRST guard stays, and this is exactly the input
        // that proves why: a bare string.Equals(null, null, OrdinalIgnoreCase) is TRUE, which
        // would contradict "a missing pending name... is never still current" above.
        Assert.False(TaxiwayChangeGate.IsStillCurrent(pendingTaxiwayName: null, currentTaxiwayName: null));
        Assert.False(TaxiwayChangeGate.IsStillCurrent(pendingTaxiwayName: "", currentTaxiwayName: ""));
    }

    // Minor 1: Classify used to be judged only against _lastAnnouncedTaxiway, and
    // TaxiGuidanceManager stamped that field the instant a name was DEFERRED, not once it was
    // actually SPOKEN. A deferral later discarded as stale (IsStillCurrent false at flush time)
    // then left a name permanently marked "announced" that the pilot never actually heard --
    // and a genuine later re-arrival on that same name was silently skipped forever. Classify
    // now also takes the CURRENTLY-pending name, so "decided but not yet spoken" and "actually
    // spoken" are tracked separately.

    [Fact]
    public void A_name_already_pending_is_skipped_even_though_it_was_never_spoken()
    {
        // A second advance onto the SAME still-pending taxiway (e.g. the taxiway is split
        // across several navdata segments) must not re-defer a redundant duplicate -- even
        // though the name was only DECIDED, not yet actually spoken, so lastAnnouncedTaxiway
        // does not yet record it.
        Assert.Equal(TaxiwayChangeGate.Decision.Skip,
            TaxiwayChangeGate.Classify("B", lastAnnouncedTaxiway: "", windowOpen: true, pendingTaxiwayName: "B"));
    }

    [Fact]
    public void A_name_only_pending_does_not_block_a_different_new_name()
    {
        Assert.Equal(TaxiwayChangeGate.Decision.SpeakNow,
            TaxiwayChangeGate.Classify("C", lastAnnouncedTaxiway: "", windowOpen: false, pendingTaxiwayName: "B"));
    }

    [Fact]
    public void Once_pending_is_cleared_the_same_name_is_a_fresh_decision()
    {
        // Once a deferred name has been delivered (spoken or discarded) and
        // _pendingTaxiwayAnnouncement is cleared back to null, the SAME name reappearing later
        // must be judged fresh, not skipped as a duplicate of a decision that was never spoken.
        Assert.Equal(TaxiwayChangeGate.Decision.SpeakNow,
            TaxiwayChangeGate.Classify("B", lastAnnouncedTaxiway: "", windowOpen: false, pendingTaxiwayName: null));
    }

    // PR #238 review, Important C: FlushPendingTaxiwayAnnouncement used to speak a deferred
    // name whenever IsStillCurrent said so, with no check against _lastAnnouncedTaxiway.
    // TryRecalculateRoute can adopt a brand-new route between the defer and the flush WITHOUT
    // going through AnnounceOrDeferTaxiwayChange at all -- it stamps _lastAnnouncedTaxiway
    // itself as part of the "Route changed. Now via ..." sentence it speaks immediately, and a
    // recalculation run from the aircraft's own position normally starts the new route on the
    // very taxiway the aircraft is already on. So the new route's CURRENT segment can carry
    // the exact same name as a stale deferral, and IsStillCurrent alone cannot tell "nothing
    // changed" apart from "current again by coincidence, and already spoken by someone else":
    // without ShouldSpeakDeferred's extra check, the flush both repeats a name the pilot was
    // just told and, because AnnounceInstruction is an interrupting AnnounceImmediate, can cut
    // the recalculation's own "Route changed" sentence off mid-word -- the one sentence naming
    // which runways the new route crosses.
    [Fact]
    public void A_deferred_name_already_spoken_by_a_recalculation_is_not_repeated()
    {
        Assert.False(TaxiwayChangeGate.ShouldSpeakDeferred(
            pendingTaxiwayName: "B", currentTaxiwayName: "B", lastAnnouncedTaxiway: "B"));
    }

    [Fact]
    public void A_deferred_name_already_spoken_is_not_repeated_case_insensitively()
    {
        Assert.False(TaxiwayChangeGate.ShouldSpeakDeferred(
            pendingTaxiwayName: "b", currentTaxiwayName: "B", lastAnnouncedTaxiway: "B"));
    }

    [Fact]
    public void A_deferred_name_not_yet_spoken_by_anyone_is_still_delivered()
    {
        // Ordinary defer-then-flush with no intervening recalculation: lastAnnouncedTaxiway is
        // still the OLDER name, so the deferred one is genuinely new information.
        Assert.True(TaxiwayChangeGate.ShouldSpeakDeferred(
            pendingTaxiwayName: "B", currentTaxiwayName: "B", lastAnnouncedTaxiway: "A"));
    }

    [Fact]
    public void A_stale_deferred_name_is_still_discarded_regardless_of_lastAnnounced()
    {
        // IsStillCurrent's existing stale-discard case must survive unchanged: the route moved
        // on to a different taxiway, so this is never spoken no matter what lastAnnounced says.
        Assert.False(TaxiwayChangeGate.ShouldSpeakDeferred(
            pendingTaxiwayName: "C", currentTaxiwayName: "D", lastAnnouncedTaxiway: "A"));
    }

    [Fact]
    public void Nothing_pending_is_never_spoken_by_the_flush()
    {
        Assert.False(TaxiwayChangeGate.ShouldSpeakDeferred(
            pendingTaxiwayName: null, currentTaxiwayName: "C", lastAnnouncedTaxiway: "A"));
    }

    [Fact]
    public void A_discarded_deferral_can_be_re_announced_later_for_the_same_name()
    {
        // End-to-end simulation of TaxiGuidanceManager's own field-juggling for the
        // "B -> unnamed connector -> B" shape (PR #238 review, Minor 1's motivating case):
        // deferred while the window is open, then discarded (never spoken) because the route
        // moved on to an unnamed connector before the window closed, then "B" reappears. Under
        // the OLD design (stamping _lastAnnouncedTaxiway at DEFER time) this second "B" was
        // permanently skipped even though the pilot never heard it once.
        string lastAnnounced = "";
        string? pending = null;

        // 1. Advance onto the first "B" segment, window open: defer.
        Assert.Equal(TaxiwayChangeGate.Decision.Defer,
            TaxiwayChangeGate.Classify("B", lastAnnounced, windowOpen: true, pendingTaxiwayName: pending));
        pending = "B"; // AnnounceOrDeferTaxiwayChange's Defer case -- lastAnnounced NOT touched

        // 2. The window closes while the route has already moved on to an unnamed connector:
        // the flush discards "B" silently without ever speaking it.
        Assert.False(TaxiwayChangeGate.IsStillCurrent(pending, currentTaxiwayName: ""));
        pending = null; // one-shot delivery either way; lastAnnounced is untouched by a discard

        // 3. The aircraft advances onto a SECOND "B" segment. Because "B" was never actually
        // spoken, it must be treated as new, not skipped.
        Assert.Equal(TaxiwayChangeGate.Decision.SpeakNow,
            TaxiwayChangeGate.Classify("B", lastAnnounced, windowOpen: false, pendingTaxiwayName: pending));
    }
}
