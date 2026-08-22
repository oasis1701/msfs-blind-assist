using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The general "is this spoken phrase worth saying, or is it just a countdown ticking?"
/// filter, shared by the message slot and the service bus phase (GsxServiceAnnouncer).
/// </summary>
public class GsxPhraseGateTests
{
    [Fact]
    public void First_non_empty_phrase_announces()
        => Assert.True(GsxPhraseGate.ShouldAnnounce("", "on the way, ETA 15 secs"));

    [Fact]
    public void An_exact_repeat_is_silent()
        => Assert.False(GsxPhraseGate.ShouldAnnounce("in position", "in position"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_current_is_silent(string current)
        => Assert.False(GsxPhraseGate.ShouldAnnounce("anything", current));

    [Fact]
    public void A_ticking_countdown_is_silent()
    {
        // The bug this exists for: "on the way, ETA 15 secs" -> "… 14 secs" -> "… 13 secs"
        // is one phase with a counter, not fifteen announcements.
        Assert.False(GsxPhraseGate.ShouldAnnounce("on the way, ETA 15 secs", "on the way, ETA 14 secs"));
        Assert.False(GsxPhraseGate.ShouldAnnounce("on the way, ETA 10 secs", "on the way, ETA 9 secs")); // length change too
    }

    [Fact]
    public void A_change_in_the_phase_WORDS_announces()
    {
        Assert.True(GsxPhraseGate.ShouldAnnounce("on the way, ETA 15 secs", "in position"));
        Assert.True(GsxPhraseGate.ShouldAnnounce("in position", "leaving"));
    }

    [Fact]
    public void A_digit_run_glued_to_a_letter_is_an_identifier_and_announces()
        => Assert.True(GsxPhraseGate.ShouldAnnounce("to gate B25", "to gate B27"));

    [Theory]
    [InlineData("on the way, ETA 15 secs", "on the way, ETA 3 secs", true)]   // digit-run-only
    [InlineData("Pushback in 5 seconds", "Pushback in 4 seconds", true)]
    [InlineData("front loader loading", "rear loader loading", false)]         // words differ
    public void IsDigitRunOnlyChange_pins_the_boundary(string a, string b, bool expected)
        => Assert.Equal(expected, GsxPhraseGate.IsDigitRunOnlyChange(a, b));

    // ── A countdown that crosses a unit boundary is still a countdown ────────
    // Live gsx.log, one boarding: eleven bus callouts, seven distinct once digits are
    // blanked, because the ETA recalculates and flip-flops across 60 seconds --
    //   "Board bus on the way, ETA 1 min 5 secs."
    //   "Board bus on the way, ETA 55 secs."
    //   "Board bus on the way, ETA 1 min 5 secs."
    //   "Board bus on the way, ETA 36 secs."
    // Blanking digit runs alone cannot see these as the same phrase: crossing the minute
    // boundary changes the WORDS, not just the numbers, so "1 min 5 secs" and "55 secs"
    // differ structurally and every crossing read as news. A whole run of duration terms
    // therefore collapses together, so the phase words are what is compared.

    [Theory]
    [InlineData("on the way, ETA 1 min 5 secs", "on the way, ETA 55 secs")]
    [InlineData("on the way, ETA 55 secs", "on the way, ETA 1 min 5 secs")]
    [InlineData("on the way, ETA 17 mins 46 secs", "on the way, ETA 42 secs")]
    [InlineData("on the way, ETA 2 hours 3 mins", "on the way, ETA 45 mins")]
    [InlineData("on the way, ETA 1 minute 5 seconds", "on the way, ETA 55 seconds")]
    public void A_duration_crossing_a_unit_boundary_is_a_tick(string before, string after)
    {
        Assert.True(GsxPhraseGate.IsDigitRunOnlyChange(before, after));
        Assert.False(GsxPhraseGate.ShouldAnnounce(before, after));
    }

    [Fact]
    public void A_real_phase_change_still_speaks_even_with_a_duration_riding_along()
    {
        // The phase words are what matters; only the duration collapses.
        Assert.True(GsxPhraseGate.ShouldAnnounce("on the way, ETA 55 secs", "approaching, ETA 1 min 5 secs"));
        Assert.True(GsxPhraseGate.ShouldAnnounce("on the way, ETA 1 min", "in position"));
    }

    [Fact]
    public void A_distance_is_not_a_duration()
    {
        // GSX's pushback line "Start after 49.0 meters" must not be mistaken for a countdown
        // -- "meters" is not a time unit, so the distance still reads as a standalone digit
        // run and 49 -> 30 stays a tick, while the words around it still decide news.
        Assert.True(GsxPhraseGate.ShouldAnnounce("Commencing push. Start after 49.0 meters",
                                                 "All engines clear. Start after 30.0 meters"));
    }
}
