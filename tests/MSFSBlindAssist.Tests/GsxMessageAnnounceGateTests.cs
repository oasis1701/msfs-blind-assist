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
    public void A_digit_run_glued_to_a_letter_is_an_identifier_and_announces()
    {
        // "…to gate B25" -> "…to gate B27" is a reassignment, not a counter tick.
        Assert.True(GsxMessageAnnounceGate.ShouldAnnounce("Follow me to gate B25", "Follow me to gate B27"));
        Assert.True(GsxMessageAnnounceGate.ShouldAnnounce("Stand A6 assigned", "Stand A7 assigned"));
    }

    [Fact]
    public void The_gate_itself_treats_a_blank_as_silent_and_an_exact_repeat_as_silent()
    {
        // The CALLER (GsxService.AnnounceMessageIfChanged) resets its last-spoken text when
        // the slot blanks — the pre-Remote-API ClearLastTooltip policy — so the same banner
        // shown again after a gap IS spoken again. That reset is the caller's; the pure gate
        // only ever answers "is this text, against that last-spoken text, worth speaking".
        Assert.False(GsxMessageAnnounceGate.ShouldAnnounce("Set parking brake.", ""));
        Assert.False(GsxMessageAnnounceGate.ShouldAnnounce("Set parking brake.", "Set parking brake."));
        Assert.True(GsxMessageAnnounceGate.ShouldAnnounce("", "Set parking brake."));
    }
}
