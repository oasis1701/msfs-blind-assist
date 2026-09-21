// PR #238 deferred finding §6: a shared stop guarding TWO runways was cleared by ONE Continue.
//
// When two runways resolve to the same stop the label merges ("runway 09 and runway 01") and one
// segment is tagged; guidance had exactly one HoldShort state and one ContinuePastHoldShort per stop
// point, so a single Continue authorised crossing both. This repo states the opposing rule in its
// own words in TaxiGuidanceManager: explicit crossing clearance is required for EACH runway —
// controllers issue them one at a time, and an aircraft must have crossed the previous runway before
// the next crossing clearance is issued.
//
// The finding was raised as a design question, not a patch, because it is new state on a safety
// path; implemented on the owner's ruling. Measured frequency: 0 of 2,518 sampled fs2024 routes
// produced a start hold naming more than one runway, and shared segment stops are likewise rare — so
// the single-runway hold must be untouched, which is what an empty stage list guarantees.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RunwayHoldStagesTests
{
    [Fact]
    public void A_stop_naming_one_runway_has_no_stages()
    {
        Assert.Empty(RunwayHoldStages.From("runway 09"));
        Assert.Empty(RunwayHoldStages.From("runway 06L at D5"));
    }

    [Fact]
    public void A_stop_naming_no_runway_has_no_stages()
    {
        Assert.Empty(RunwayHoldStages.From("end of taxiway B"));
        Assert.Empty(RunwayHoldStages.From(""));
        Assert.Empty(RunwayHoldStages.From(null));
    }

    [Fact]
    public void A_shared_stop_stages_each_runway_in_label_order()
    {
        var stages = RunwayHoldStages.From("runway 09 and runway 01");

        Assert.Equal(new[] { "09", "01" }, stages);
    }

    [Fact]
    public void Three_runways_stage_three_times()
    {
        var stages = RunwayHoldStages.From("runway 09 and runway 01 and runway 15");

        Assert.Equal(new[] { "09", "01", "15" }, stages);
    }

    // Both ends of one pavement are ONE runway and ONE clearance. ComposeSharedLabel never writes
    // such a label, but a kept scenery label could, and asking for two Continues to cross one runway
    // would be worse than the defect this closes.
    [Fact]
    public void Reciprocal_designators_are_one_runway_and_one_stage()
    {
        Assert.Empty(RunwayHoldStages.From("runway 26R and runway 08L"));
    }

    [Fact]
    public void A_repeated_designator_is_one_stage()
    {
        Assert.Empty(RunwayHoldStages.From("runway 09 and runway 09"));
    }

    // The label still names every runway the stop guards, as it always has; the CONTINUE PROMPT is
    // what changes, because the pilot has to know the press does not buy both crossings.
    [Fact]
    public void The_hold_sentence_names_the_runway_this_continue_authorises()
    {
        Assert.Equal(
            "Stop. Hold short of runway 09 and runway 01. Press continue when cleared for runway 09.",
            RunwayHoldStages.ComposeHold("runway 09 and runway 01", "09"));
    }

    [Fact]
    public void A_label_less_staged_hold_still_reads_as_a_sentence()
    {
        Assert.Equal(
            "Stop. Hold short. Press continue when cleared for runway 09.",
            RunwayHoldStages.ComposeHold(null, "09"));
    }

    // The aircraft does NOT move on this press, so the sentence must not read like a resume — every
    // other Continue in the manager resumes guidance, and a blind pilot pressing the key has every
    // reason to expect movement.
    [Fact]
    public void The_advance_sentence_says_the_aircraft_is_still_holding()
    {
        string s = RunwayHoldStages.ComposeAdvance("09", "01");

        Assert.Equal("Runway 09 cleared. Still holding. Press continue when cleared for runway 01.", s);
        Assert.Contains("Still holding", s, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Continuing", s, System.StringComparison.OrdinalIgnoreCase);
    }

    // Found by the in-sim test (KJFK 31L+04L, 2026-09-21). The staged hold itself worked, but the
    // STATUS query (taxi status / Ctrl+Y's sibling) still answered "Holding short of runway 31L at K
    // and runway 04L. Press continue when cleared." — naming BOTH runways as though nothing had been
    // cleared, and not saying which runway the next press authorises. A pilot who asks where they
    // stand mid-stage has to remember it themselves, which is exactly what this feature exists to
    // stop.
    [Fact]
    public void The_status_line_names_the_runway_still_outstanding()
    {
        Assert.Equal(
            "Holding short of runway 31L at K and runway 04L. Press continue when cleared for runway 04L.",
            RunwayHoldStages.ComposeStatus("runway 31L at K and runway 04L", "04L"));
    }

    [Fact]
    public void A_label_less_staged_status_still_reads_as_a_sentence()
    {
        Assert.Equal(
            "Holding short. Press continue when cleared for runway 09.",
            RunwayHoldStages.ComposeStatus(null, "09"));
    }
}
