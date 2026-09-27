// The A32NX MCDU Coherent client's poll loop sleeps a second at a time while the window is
// closed. Showing the window, or a transport switch asking for a fresh frame, must not wait
// that sleep out: the screen reader reads the list the moment it gets focus, and it read the
// screen from before the window was closed (PR #253 review). WakeableDelay is that sleep.

using System.Diagnostics;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class WakeableDelayTests
{
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task A_wake_ends_a_long_wait_at_once()
    {
        var delay = new WakeableDelay();
        var wait = delay.WaitAsync(Long, CancellationToken.None);
        await Task.Delay(50);

        delay.Wake();

        await wait.WaitAsync(Guard);   // throws TimeoutException if the wake did not end it
    }

    [Fact]
    public async Task Without_a_wake_the_wait_lasts_its_delay()
    {
        var delay = new WakeableDelay();
        var clock = Stopwatch.StartNew();

        await delay.WaitAsync(TimeSpan.FromMilliseconds(150), CancellationToken.None);

        Assert.True(clock.ElapsedMilliseconds >= 120, $"{clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task A_wake_before_the_wait_ends_the_next_wait_at_once()
    {
        // The window can be shown while the loop is busy with a read rather than asleep.
        var delay = new WakeableDelay();
        delay.Wake();

        await delay.WaitAsync(Long, CancellationToken.None).WaitAsync(Guard);
    }

    [Fact]
    public async Task Several_wakes_end_only_one_wait()
    {
        var delay = new WakeableDelay();
        delay.Wake();
        delay.Wake();
        await delay.WaitAsync(Long, CancellationToken.None).WaitAsync(Guard);

        var clock = Stopwatch.StartNew();
        await delay.WaitAsync(TimeSpan.FromMilliseconds(150), CancellationToken.None);

        Assert.True(clock.ElapsedMilliseconds >= 120, $"{clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Cancelling_ends_the_wait_with_an_OperationCanceledException()
    {
        var delay = new WakeableDelay();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => delay.WaitAsync(Long, cts.Token));
    }
}
