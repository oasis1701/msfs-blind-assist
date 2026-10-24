// Characterization tests for the registry of the pilot's own door presses:
// the Cabin window marks a door or cargo line when it sends that door's write, and the app-wide door
// announcement leaves that door to the window's read-back while the mark lasts, so a change is said once.
// Every test uses its own instance and its own clock; none touches FenixCabinOwnPresses.Shared.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixCabinOwnPressesTests
{
    private static readonly DateTime T0 = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Thirty = TimeSpan.FromSeconds(30);

    [Fact]
    public void A_mark_is_pending_for_its_window_and_only_for_its_line()
    {
        var presses = new FenixCabinOwnPresses();
        Assert.False(presses.IsPending("door.d1l", T0));

        DateTime end = presses.Mark("door.d1l", T0, Thirty);

        Assert.Equal(T0 + Thirty, end);
        Assert.True(presses.IsPending("door.d1l", T0));
        Assert.True(presses.IsPending("door.d1l", T0 + Thirty - TimeSpan.FromMilliseconds(1)));
        // The window's end is the read-back's: from then on the line is the app-wide announcement's again.
        Assert.False(presses.IsPending("door.d1l", T0 + Thirty));
        Assert.False(presses.IsPending("door.d1l", T0 + Thirty + TimeSpan.FromSeconds(1)));

        Assert.False(presses.IsPending("door.d1r", T0));
        Assert.False(presses.IsPending("cargo.forward", T0));
    }

    [Fact]
    public void A_later_press_of_the_same_line_moves_the_end()
    {
        var presses = new FenixCabinOwnPresses();
        presses.Mark("cargo.aft", T0, Thirty);
        presses.Mark("cargo.aft", T0 + TimeSpan.FromSeconds(20), Thirty);

        Assert.True(presses.IsPending("cargo.aft", T0 + TimeSpan.FromSeconds(45)));
        Assert.False(presses.IsPending("cargo.aft", T0 + TimeSpan.FromSeconds(50)));
    }

    [Fact]
    public void A_withdrawn_mark_ends_at_once_unless_a_later_press_replaced_it()
    {
        // A press the Fenix refused withdraws its own mark.
        var presses = new FenixCabinOwnPresses();
        DateTime end = presses.Mark("door.d4r", T0, Thirty);
        presses.Withdraw("door.d4r", end);
        Assert.False(presses.IsPending("door.d4r", T0));

        // A refused press does not take away the mark of a later accepted one.
        DateTime first = presses.Mark("door.d4r", T0, Thirty);
        presses.Mark("door.d4r", T0 + TimeSpan.FromSeconds(1), Thirty);
        presses.Withdraw("door.d4r", first);
        Assert.True(presses.IsPending("door.d4r", T0 + TimeSpan.FromSeconds(2)));

        // Withdrawing a line never marked does nothing.
        presses.Withdraw("door.d1l", end);
        Assert.False(presses.IsPending("door.d1l", T0));
    }

    [Fact]
    public void The_window_and_the_announcements_share_one_registry_safely_across_threads()
    {
        Assert.Same(FenixCabinOwnPresses.Shared, FenixCabinOwnPresses.Shared);

        // The window marks on the UI thread while the poll reads on a pool thread.
        var presses = new FenixCabinOwnPresses();
        Parallel.For(0, 2000, i =>
        {
            string key = "door.d" + (i % 4);
            if (i % 2 == 0) presses.Mark(key, T0, Thirty);
            else presses.IsPending(key, T0);
        });
        Assert.True(presses.IsPending("door.d0", T0));
    }
}
