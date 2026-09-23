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
}
