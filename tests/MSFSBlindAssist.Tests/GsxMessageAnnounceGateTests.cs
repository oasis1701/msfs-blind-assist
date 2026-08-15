using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins when a change to GSX's own "message" slot (the follow-me / marshaller /
/// idle banner text — the pre-Remote-API transport's primary announcement stream)
/// is worth speaking. Mirrors the old PublishLiveServiceText policy: exact repeats
/// and digit-only ticks are silent; an empty/invisible slot says nothing.
/// </summary>
public class GsxMessageAnnounceGateTests
{
    [Fact]
    public void First_non_empty_text_announces()
        => Assert.True(GsxMessageAnnounceGate.ShouldAnnounce("", "Follow me car is approaching."));

    [Fact]
    public void Same_text_again_is_silent()
        => Assert.False(GsxMessageAnnounceGate.ShouldAnnounce("Follow me car is approaching.", "Follow me car is approaching."));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_slot_is_silent(string current)
        => Assert.False(GsxMessageAnnounceGate.ShouldAnnounce("Anything before", current));

    [Fact]
    public void A_digit_only_change_is_a_counter_tick_not_news()
        => Assert.False(GsxMessageAnnounceGate.ShouldAnnounce("Pushback in 5 seconds", "Pushback in 4 seconds"));

    [Fact]
    public void A_digit_run_that_changes_length_is_still_a_tick()
        => Assert.False(GsxMessageAnnounceGate.ShouldAnnounce("Waiting, 10 seconds", "Waiting, 9 seconds"));

    [Fact]
    public void A_wording_change_announces()
        => Assert.True(GsxMessageAnnounceGate.ShouldAnnounce("Follow me car is approaching.", "Follow me car has arrived. Follow it to your parking."));

    [Fact]
    public void Text_returning_after_a_blank_is_not_re_read()
    {
        // The caller does NOT reset the last-spoken text when the slot blanks, so a
        // banner flickering off and on with the same words is not spoken twice.
        Assert.False(GsxMessageAnnounceGate.ShouldAnnounce("Set parking brake.", ""));
        Assert.False(GsxMessageAnnounceGate.ShouldAnnounce("Set parking brake.", "Set parking brake."));
    }
}
