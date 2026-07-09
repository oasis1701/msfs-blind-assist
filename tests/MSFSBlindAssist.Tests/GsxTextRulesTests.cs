// Characterization tests for MSFSBlindAssist.Services.GsxService.TextRules
// (GsxService.TextRules.cs) — the pure, dependency-free text-processing
// rules of the GSX accessibility integration.
//
// This file ports every assert from tools/GsxTextProbe/Program.cs (a console
// probe that CI never runs) into CI-run xUnit rows, so the rules stay
// guarded automatically. Rows carrying the comment "ported from
// tools/GsxTextProbe" are a faithful transcription of an existing probe
// assert (same input, same expected value) — do not "improve" them. Rows
// commented "new boundary row" were added here and were NOT in the probe;
// their expected values were confirmed by running the tests against the
// real TextRules implementation, per the project's characterization
// methodology: this locks ACTUAL behavior, it does not assert a spec. If a
// literal here ever disagrees with real output, the test must be corrected
// to match real output, not the other way around.
//
// The probe's final section (a sweep of every dollar amount found in real
// GSX receipt HTML under %APPDATA%\Virtuali\GSX\Receipts) is intentionally
// NOT ported: it is conditional on files that only exist on a developer's
// live GSX install and is skipped silently when absent in the probe itself
// — there is nothing deterministic to pin in CI. ContainsMoneyAmount and
// NormalizeStatusStableText's <price> behavior are already covered by the
// deterministic price-normalization rows below.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class GsxTextRulesTests
{
    // ─────────────────────────────────────────────────────────────────────
    // SplitTooltipParts — comma-splitting that never breaks inside a
    // digit-grouped amount ("$ 5,989.76"), because a separator comma
    // routinely FOLLOWS a digit ("... $ 5.50, Timer: ...").
    // ─────────────────────────────────────────────────────────────────────

    [Theory]
    // ported from tools/GsxTextProbe
    [InlineData("Refueling in progress, ETA 5 minutes", 2)]
    [InlineData("Total $ 5,989.76", 1)]
    [InlineData("[GSX] Boarding, 3 bags loaded, rear door closing", 3)]
    [InlineData("Passenger boarding 45/100, GPU connected", 2)]
    [InlineData("GPU $ 5.50, Timer: running 00:12:34", 2)]
    [InlineData("Total $ 5,989.76, GPU connected", 2)]
    // new boundary rows
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("just one segment, no digits either side", 2)]
    public void SplitTooltipParts_count(string input, int expectedCount)
    {
        var parts = GsxService.SplitTooltipParts(input);
        Assert.Equal(expectedCount, parts.Count);
    }

    [Fact]
    // ported from tools/GsxTextProbe
    public void SplitTooltipParts_keeps_thousands_amount_intact_in_first_part()
    {
        var parts = GsxService.SplitTooltipParts("Total $ 5,989.76, GPU connected");
        Assert.Equal("Total $ 5,989.76", parts[0]);
    }

    [Fact]
    // new boundary row: null input must not throw and yields an empty list
    public void SplitTooltipParts_null_input_yields_empty_list()
    {
        var parts = GsxService.SplitTooltipParts(null!);
        Assert.Empty(parts);
    }

    [Fact]
    // new boundary row: embedded newlines are split as separate lines before
    // comma-splitting within each line (ReplaceLineEndings + Split('\n')).
    public void SplitTooltipParts_splits_across_embedded_newlines()
    {
        var parts = GsxService.SplitTooltipParts("GPU connected\nStairs, connected");
        Assert.Equal(new[] { "GPU connected", "Stairs", "connected" }, parts);
    }

    // ─────────────────────────────────────────────────────────────────────
    // NormalizeStatusStableText — price normalization on real receipt
    // shapes, and currency false-positive guards (mass units, lowercase
    // ISO-code-shaped English words, embedded-fragment matches).
    // ─────────────────────────────────────────────────────────────────────

    [Theory]
    // ported from tools/GsxTextProbe
    [InlineData("Pushback service $ 769.99")]
    [InlineData("GPU connection EUR 12.50")]
    [InlineData("Jetway operation $100.00/hr")]
    public void NormalizeStatusStableText_contains_price_token(string input)
    {
        Assert.Contains("<price>", GsxService.NormalizeStatusStableText(input));
    }

    [Fact]
    // ported from tools/GsxTextProbe
    public void NormalizeStatusStableText_comma_grouped_amount_fully_consumed()
    {
        Assert.Equal("Total <price>", GsxService.NormalizeStatusStableText("Total $ 5,989.76"));
    }

    [Theory]
    // ported from tools/GsxTextProbe — currency false positives must not eat
    // real quantities (mass units, lowercase code-shaped words, embedded
    // fragments).
    [InlineData("Refueling 12,000 pounds")]
    [InlineData("boarded all 38 passengers")]
    [InlineData("overall 5 items")]
    [InlineData("retry 3 times")]
    public void NormalizeStatusStableText_does_not_produce_price_token(string input)
    {
        Assert.DoesNotContain("<price>", GsxService.NormalizeStatusStableText(input));
    }

    [Theory]
    // ported from tools/GsxTextProbe — pairs whose distinct quantities must
    // stay distinct after normalization (a false-positive price match would
    // collapse both into the same stable string and silence the change).
    [InlineData("Refueling 12,000 pounds", "Refueling 13,000 pounds")]
    [InlineData("boarded all 38 passengers", "boarded all 39 passengers")]
    public void NormalizeStatusStableText_distinct_quantities_stay_distinct(string a, string b)
    {
        Assert.NotEqual(GsxService.NormalizeStatusStableText(a), GsxService.NormalizeStatusStableText(b));
    }

    [Theory]
    // ported from tools/GsxTextProbe — real currency shapes must still
    // match after the false-positive tightening above.
    [InlineData("Fee ALL 500")]
    [InlineData("Fee 25 euros")]
    [InlineData("Fee 1,250.75 USD")]
    public void NormalizeStatusStableText_real_currency_shapes_still_match(string input)
    {
        Assert.Contains("<price>", GsxService.NormalizeStatusStableText(input));
    }

    [Theory]
    // new boundary rows: duration bucketing (5-minute groups) is exercised
    // by NormalizeStatusStableText but had no probe coverage at all.
    [InlineData("ETA 299 seconds", "ETA <duration-0>")]
    [InlineData("ETA 300 seconds", "ETA <duration-1>")]
    [InlineData("ETA 301 seconds", "ETA <duration-1>")]
    [InlineData("ETA 5 minutes", "ETA <duration-1>")]
    [InlineData("ETA 4 minutes", "ETA <duration-0>")]
    public void NormalizeStatusStableText_duration_bucket_edges(string input, string expected)
    {
        Assert.Equal(expected, GsxService.NormalizeStatusStableText(input));
    }

    [Fact]
    // new boundary row: blank input yields the empty string, not a throw.
    public void NormalizeStatusStableText_blank_input_yields_empty_string()
    {
        Assert.Equal(string.Empty, GsxService.NormalizeStatusStableText("   "));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Charge/timer line classification — IsChargeStatusLine, IsTimerStatusLine,
    // IsTimerStatusText (the brief's named member; identical logic to
    // IsTimerStatusLine but declared separately and used on a different call
    // path), FormatGroundConnectionTimerServiceText.
    // ─────────────────────────────────────────────────────────────────────

    [Theory]
    // ported from tools/GsxTextProbe
    [InlineData("GPU timer: running 00:12:34", true)]
    [InlineData("Subtotal $ 881.82", true)]
    [InlineData("Boarding completed", false)]
    public void IsChargeStatusLine_classification(string input, bool expected)
    {
        Assert.Equal(expected, GsxService.IsChargeStatusLine(input));
    }

    [Fact]
    // ported from tools/GsxTextProbe
    public void IsTimerStatusLine_detects_timer_colon()
    {
        Assert.True(GsxService.IsTimerStatusLine("GPU timer: running 00:12:34"));
    }

    [Theory]
    // new boundary rows: IsTimerStatusText is a distinct declared member
    // (used on GsxService.cs:1219) with identical "timer:" logic to
    // IsTimerStatusLine — exercised directly per the task brief.
    [InlineData("GPU timer: running 00:12:34", true)]
    [InlineData("GPU TIMER: running 00:12:34", true)]
    [InlineData("GPU connected", false)]
    [InlineData("", false)]
    public void IsTimerStatusText_classification(string input, bool expected)
    {
        Assert.Equal(expected, GsxService.IsTimerStatusText(input));
    }

    [Fact]
    // ported from tools/GsxTextProbe
    public void FormatGroundConnectionTimerServiceText_formats_service_name()
    {
        Assert.Equal(
            "GPU service is running",
            GsxService.FormatGroundConnectionTimerServiceText("GPU timer: running 00:12:34"));
    }

    [Fact]
    // new boundary row: a line with no "timer:" segment to strip leaves the
    // whole text as the "service name" (the caller only ever passes lines
    // that already matched IsTimerStatusLine, but the helper itself has no
    // such guard).
    public void FormatGroundConnectionTimerServiceText_no_timer_segment_uses_whole_text()
    {
        Assert.Equal(
            "Boarding completed service is running",
            GsxService.FormatGroundConnectionTimerServiceText("Boarding completed"));
    }

    [Fact]
    // new boundary row: blank input yields the empty string.
    public void FormatGroundConnectionTimerServiceText_blank_input_yields_empty_string()
    {
        Assert.Equal(string.Empty, GsxService.FormatGroundConnectionTimerServiceText("   "));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Timer context keys — TimerStatusContextEquals / BuildTimerStatusContextKey.
    // ─────────────────────────────────────────────────────────────────────

    private const string TimerCtxA = "GPU connected\nCurrent charges:\nGPU timer: running 00:05:00, $ 2.50";
    private const string TimerCtxB = "GPU connected\nCurrent charges:\nGPU timer: running 00:09:30, $ 4.75";
    private const string TimerCtxC = "Stairs connected\nCurrent charges:\nStairs timer: running 00:01:00, $ 1.00";

    [Fact]
    // ported from tools/GsxTextProbe — same services, different durations
    // and charges, must be context-equal.
    public void TimerStatusContextEquals_duration_and_charge_changes_are_context_equal()
    {
        Assert.True(GsxService.TimerStatusContextEquals(TimerCtxA, TimerCtxB));
    }

    [Fact]
    // ported from tools/GsxTextProbe — a different service set is context-different.
    public void TimerStatusContextEquals_different_service_is_context_different()
    {
        Assert.False(GsxService.TimerStatusContextEquals(TimerCtxA, TimerCtxC));
    }

    [Fact]
    // ported from tools/GsxTextProbe — ordering lock: a line containing
    // "timer:" must classify as a timer line ("X service is running"), not
    // be dropped as a charge line, even though it also contains a price.
    public void BuildTimerStatusContextKey_timer_before_charge_classification_order()
    {
        string key = GsxService.BuildTimerStatusContextKey("GPU timer: running 00:05:00");
        Assert.Contains("GPU service is running", key, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    // new boundary row: TimerStatusContextEquals has an explicit early
    // return for a blank previous text.
    public void TimerStatusContextEquals_blank_previous_text_is_never_equal()
    {
        Assert.False(GsxService.TimerStatusContextEquals(TimerCtxA, ""));
        Assert.False(GsxService.TimerStatusContextEquals(TimerCtxA, "   "));
    }

    [Fact]
    // new boundary row: blank input yields the empty context key.
    public void BuildTimerStatusContextKey_blank_input_yields_empty_string()
    {
        Assert.Equal(string.Empty, GsxService.BuildTimerStatusContextKey("   "));
    }

    // ─────────────────────────────────────────────────────────────────────
    // TryParsePassengerCount / PaxOnlySegmentRegex.
    // ─────────────────────────────────────────────────────────────────────

    [Theory]
    // ported from tools/GsxTextProbe
    [InlineData("Passenger boarding 5/100 passengers", 5)]
    [InlineData("pax 47/180", 47)]
    [InlineData("Passenger deboarding 432/853 passengers", 432)]
    // ported from tools/GsxTextProbe (probe's "Boarding milestone gate"
    // section, 4-digit-clamp assert — exercises TryParsePassengerCount
    // itself, so it lives here under its own member)
    [InlineData("Passenger deboarding 1023/1200 passengers", 1023)]
    // new boundary rows
    [InlineData("pax 0/100", 0)]
    [InlineData("9999 passengers", 9999)]
    public void TryParsePassengerCount_parses_expected_count(string input, int expected)
    {
        bool ok = GsxService.TryParsePassengerCount(input, out int n);
        Assert.True(ok);
        Assert.Equal(expected, n);
    }

    [Fact]
    // new boundary row: text with no passenger-count shape at all fails to parse.
    public void TryParsePassengerCount_no_match_returns_false()
    {
        bool ok = GsxService.TryParsePassengerCount("GPU connected", out int n);
        Assert.False(ok);
        Assert.Equal(0, n);
    }

    [Theory]
    // ported from tools/GsxTextProbe
    [InlineData("Passenger boarding 5/100 passengers", true)]
    [InlineData("rear loader leaving while 5 boarded", false)]
    // new boundary row: the optional "[GSX] " prefix is still strippable.
    [InlineData("[GSX] Passenger boarding 5/100 passengers", true)]
    public void PaxOnlySegmentRegex_match(string input, bool expected)
    {
        Assert.Equal(expected, GsxService.PaxOnlySegmentRegex.IsMatch(input));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Boarding-progress milestone gate — ComputeBoardingMilestone /
    // IsBoardingAnnouncementBoundary / ShouldAnnounceBoardingProgress.
    //
    // Announced() below is a faithful port of the probe's own local
    // function, which simulates the per-service dict exactly as
    // StripThrottledPaxSegments drives it: announce when
    // ShouldAnnounceBoardingProgress says so, always record the latest
    // milestone.
    // ─────────────────────────────────────────────────────────────────────

    private static List<int> Announced(IEnumerable<int> samples)
    {
        int? last = null;
        var spoken = new List<int>();
        foreach (int p in samples)
        {
            if (GsxService.ShouldAnnounceBoardingProgress(p, last))
                spoken.Add(p);
            last = GsxService.ComputeBoardingMilestone(p);
        }
        return spoken;
    }

    [Fact]
    // ported from tools/GsxTextProbe — step-1 sampling announces at 1 and
    // each decade, no repeats in between.
    public void Announced_step1_sampling_announces_1_and_decades()
    {
        var spoken = Announced(Enumerable.Range(1, 35));
        Assert.Equal("1,10,20,30", string.Join(",", spoken));
    }

    [Fact]
    // ported from tools/GsxTextProbe — a sampler that skips every multiple
    // of 10 (fast boarding, ~1s polling) must still announce roughly once
    // per decade, and the first sample past a decade must announce.
    public void Announced_skipping_sampler_still_announces_roughly_per_decade()
    {
        var spoken = Announced(new[] { 3, 8, 14, 19, 26, 33, 38, 45, 52, 58 });
        Assert.True(spoken.Count >= 4, $"expected >=4 announcements, got {spoken.Count}");
        Assert.Contains(14, spoken);
        Assert.Contains(26, spoken);
    }

    [Fact]
    // ported from tools/GsxTextProbe — repeated identical counts never re-announce.
    public void Announced_repeats_are_silent()
    {
        var spoken = Announced(new[] { 50, 50, 50, 50 });
        Assert.Equal("50", string.Join(",", spoken));
    }

    [Fact]
    // ported from tools/GsxTextProbe — mid-boarding first sight (app started
    // late) seeds silently, then the next decade crossing announces.
    public void Announced_late_start_seeds_silently_then_next_bucket_speaks()
    {
        var spoken = Announced(new[] { 47, 48, 53 });
        Assert.Equal("53", string.Join(",", spoken));
    }

    [Fact]
    // ported from tools/GsxTextProbe — turnaround: counter drops back to low
    // numbers, must announce the restart.
    public void Announced_turnaround_restart_announces()
    {
        var spoken = Announced(new[] { 150, 2 });
        Assert.Contains(2, spoken);
    }

    [Theory]
    // new boundary rows: exact milestone edges, including the 0% case
    // (service started, nobody boarded yet) called out explicitly in the
    // task brief.
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    [InlineData(1, 1)]
    [InlineData(9, 1)]
    [InlineData(10, 2)]
    [InlineData(11, 2)]
    [InlineData(19, 2)]
    [InlineData(20, 3)]
    [InlineData(21, 3)]
    [InlineData(100, 11)]
    public void ComputeBoardingMilestone_edges(int passengers, int expectedMilestone)
    {
        Assert.Equal(expectedMilestone, GsxService.ComputeBoardingMilestone(passengers));
    }

    [Theory]
    // new boundary rows: IsBoardingAnnouncementBoundary at the 0% mark and
    // the decade edges immediately below/at the interval.
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    [InlineData(20, true)]
    public void IsBoardingAnnouncementBoundary_edges(int passengers, bool expected)
    {
        Assert.Equal(expected, GsxService.IsBoardingAnnouncementBoundary(passengers));
    }

    [Fact]
    // new boundary row: first sight of 0% (service just started, nobody
    // boarded) announces immediately.
    public void ShouldAnnounceBoardingProgress_first_sight_of_zero_announces()
    {
        Assert.True(GsxService.ShouldAnnounceBoardingProgress(0, null));
    }

    [Fact]
    // new boundary row: first sight mid-decade (not on a boundary) stays silent.
    public void ShouldAnnounceBoardingProgress_first_sight_mid_decade_is_silent()
    {
        Assert.False(GsxService.ShouldAnnounceBoardingProgress(9, null));
    }

    [Fact]
    // new boundary row: first sight exactly on a decade boundary announces.
    public void ShouldAnnounceBoardingProgress_first_sight_on_decade_boundary_announces()
    {
        Assert.True(GsxService.ShouldAnnounceBoardingProgress(10, null));
    }

    [Theory]
    // new boundary rows: with a known last milestone, staying within the
    // same milestone bucket (even at its top edge, 19) stays silent, while
    // crossing into the next bucket at the exact edge (20) announces.
    [InlineData(19, 2, false)]
    [InlineData(20, 2, true)]
    [InlineData(9, 1, false)]
    [InlineData(10, 1, true)]
    public void ShouldAnnounceBoardingProgress_milestone_bucket_edges(
        int passengers, int lastMilestone, bool expected)
    {
        Assert.Equal(expected, GsxService.ShouldAnnounceBoardingProgress(passengers, lastMilestone));
    }
}
