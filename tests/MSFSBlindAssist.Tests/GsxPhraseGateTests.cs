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
}
