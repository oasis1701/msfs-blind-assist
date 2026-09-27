// When "Looking around." is worth saying (Services/SurroundingsLookupNotice).
//
// A COLD first Alt+L or Ctrl+Shift+L can wait seconds with nothing said at all: a first-time
// scenery scan and a census of the Community folder, with the OSM mirror's wait running beside
// them rather than after them. A blind pilot
// has no spinner, so silence and "the key did nothing" are the same experience. But the notice is
// ~0.8 s of speech in front of the answer, so on a cache hit — the usual case, every press after
// the first at one airport — it would be pure noise talking over what they asked for.

using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.Surroundings;

namespace MSFSBlindAssist.Tests;

public class SurroundingsLookupNoticeTests
{
    [Fact]
    public async Task An_answer_already_in_hand_is_never_announced()
        => Assert.False(await SurroundingsLookupNotice.IsSlowAsync(Task.CompletedTask, TimeSpan.FromMinutes(5)));

    [Fact]
    public async Task An_answer_that_has_not_come_within_the_delay_is()
        => Assert.True(await SurroundingsLookupNotice.IsSlowAsync(new TaskCompletionSource().Task, TimeSpan.Zero));

    [Fact]
    public async Task A_failed_lookup_is_not_reported_as_slow_and_its_exception_is_left_to_the_caller()
    {
        // The caller awaits the same task straight afterwards and has its own catch for this, so
        // throwing HERE would replace "Surroundings lookup failed." with an unhandled exception on
        // a pool thread.
        var failed = Task.FromException(new InvalidOperationException("boom"));
        Assert.False(await SurroundingsLookupNotice.IsSlowAsync(failed, TimeSpan.FromMinutes(5)));
        Assert.True(failed.IsFaulted);
    }

    [Fact]
    public void The_delay_leaves_room_for_the_notice_itself_before_the_OSM_wait_expires()
        // "Looking around." is about 0.8 s of speech, and a lookup waiting only on the mirror must
        // still hear it before the catalog build's own OSM wait (OnlineFeatureStore.CatalogWait) is up.
        => Assert.InRange(SurroundingsLookupNotice.Delay, TimeSpan.FromSeconds(1),
                          OnlineFeatureStore.CatalogWait - TimeSpan.FromSeconds(0.8));

    [Fact]
    public void A_line_inside_the_delay_interrupts_like_any_hotkey_answer()
    {
        Assert.Equal(SurroundingsLookupDelivery.Immediate, SurroundingsLookupNotice.Delivery(TimeSpan.Zero, announcerSuppressed: false));
        Assert.Equal(SurroundingsLookupDelivery.Immediate,
            SurroundingsLookupNotice.Delivery(SurroundingsLookupNotice.Delay - TimeSpan.FromMilliseconds(1), announcerSuppressed: false));
    }

    [Fact]
    public void A_line_from_the_delay_on_is_queued_so_it_cannot_cut_off_a_newer_instruction()
    {
        // A cold lookup takes 3-10 s. In that time the pilot can have been told "Stop. Hold short of
        // runway 27L." — spoken interrupting — and an interrupting answer cut it off mid-word. The
        // queued "Looking around." notice fires at exactly this delay, so a late answer also lands
        // behind the notice instead of over it.
        Assert.Equal(SurroundingsLookupDelivery.Queued, SurroundingsLookupNotice.Delivery(SurroundingsLookupNotice.Delay, announcerSuppressed: false));
        Assert.Equal(SurroundingsLookupDelivery.Queued, SurroundingsLookupNotice.Delivery(TimeSpan.FromSeconds(8), announcerSuppressed: false));
    }

    [Fact]
    public void A_late_line_still_interrupts_while_the_announcer_is_suppressed_because_a_queued_one_is_dropped()
        // ScreenReaderAnnouncer.Announce returns without speaking while Suppressed (a first-detect
        // grace window); AnnounceImmediate does not. A pilot who pressed a key must never hear nothing.
        => Assert.Equal(SurroundingsLookupDelivery.Immediate,
            SurroundingsLookupNotice.Delivery(TimeSpan.FromSeconds(8), announcerSuppressed: true));

    [Fact]
    public void The_notice_waits_only_for_what_is_left_of_its_delay_since_the_press()
    {
        // ML-5: the clock starts at the KEY PRESS. The position request and the airport resolution
        // ahead of the builds have already spent some of the delay.
        Assert.Equal(SurroundingsLookupNotice.Delay, SurroundingsLookupNotice.NoticeWait(TimeSpan.Zero));
        Assert.Equal(SurroundingsLookupNotice.Delay - TimeSpan.FromMilliseconds(400),
                     SurroundingsLookupNotice.NoticeWait(TimeSpan.FromMilliseconds(400)));
        Assert.Equal(TimeSpan.Zero, SurroundingsLookupNotice.NoticeWait(SurroundingsLookupNotice.Delay));
        Assert.Equal(TimeSpan.Zero, SurroundingsLookupNotice.NoticeWait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task With_nothing_left_of_the_delay_an_answer_not_yet_in_hand_is_slow_at_once()
    {
        // Everything before the builds already took longer than the delay: the notice speaks at
        // once rather than waiting a further full delay — and an answer already in hand still never
        // earns it. IsSlowAsync with a zero wait is already pinned above; this pins that NoticeWait's
        // clamp hands it exactly that zero, which the MainForm wiring relies on.
        var spent = SurroundingsLookupNotice.NoticeWait(TimeSpan.FromSeconds(2));
        Assert.True(await SurroundingsLookupNotice.IsSlowAsync(new TaskCompletionSource().Task, spent));
        Assert.False(await SurroundingsLookupNotice.IsSlowAsync(Task.CompletedTask, spent));
    }
}
