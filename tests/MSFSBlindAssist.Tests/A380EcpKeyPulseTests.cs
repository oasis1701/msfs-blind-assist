// FBW #10934 ("athr off logic", a380x 27a73e219, 2026-09-19) moved the FWS's 125 ms UpdateThrottler
// check to the very top of FwsCore.update(), AHEAD of the block that reads the ECAM control-panel
// keys (TO CONFIG, CLR, RCL, C/L, CHECK, UP, DOWN, ABN PROC) — the block whose own comment still
// says "acquire discrete inputs at a higher frequency". A key is now sampled once per FWS cycle
// instead of every frame, so the checklist window's old 45 ms press was seen about one time in
// three, silently: the cursor did not move and nothing was spoken.

using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class A380EcpKeyPulseTests
{
    [Fact]
    public void A_press_is_held_through_a_whole_fws_cycle_with_margin()
    {
        // Samples are frame-aligned, so consecutive ones are 125 ms plus up to a frame apart.
        Assert.True(A380EcpKeyPulse.HoldMs >= 2 * A380EcpKeyPulse.FwsCycleMs,
            $"A {A380EcpKeyPulse.HoldMs} ms press can fall between two FWS samples.");
    }

    [Fact]
    public void Consecutive_presses_are_separated_by_a_whole_fws_cycle_with_margin()
    {
        // The FWS turns a key into a PULSE on its rising edge across two samples: with no sample
        // seeing it released in between, two presses of the same key merge into one.
        Assert.True(A380EcpKeyPulse.ReleaseMs >= 2 * A380EcpKeyPulse.FwsCycleMs,
            $"Two presses {A380EcpKeyPulse.ReleaseMs} ms apart can merge into one.");
    }

    [Theory]
    [InlineData(0L, 1_000_000L, 0)]            // nothing pressed yet
    [InlineData(10_000L, 10_000L, 250)]        // pressed again the instant it was released
    [InlineData(10_000L, 10_100L, 150)]        // the checklist burst's 85 ms render wait + a fast scrape
    [InlineData(10_000L, 10_250L, 0)]          // already released long enough
    [InlineData(10_000L, 12_000L, 0)]
    [InlineData(10_000L, 9_900L, 350)]         // the last press is still HELD: its release, then 250
    public void Every_press_waits_out_the_release_time_left(long lastReleaseTick, long now, int expectedWaitMs)
    {
        // The guard sits on the PRESS, not on each caller: a press queued during the checklist
        // window's closing scrape followed the previous release by 85 ms plus the scrape, and the
        // close-time C/L by however long the pilot took to press Escape.
        Assert.Equal(expectedWaitMs, A380EcpKeyPulse.WaitBeforePressMs(lastReleaseTick, now));
    }

    [Fact]
    public void A_press_reserved_while_another_is_held_waits_for_its_release()
    {
        // The checklist window's close-time C/L can start while the C/L it pressed on opening is
        // still held; recording the release only once it HAPPENED let the second press go out on
        // top of the first — one press as far as the FWS could tell, and the overlay stayed up.
        var clock = new A380EcpKeyPulse.PressClock();

        Assert.Equal(0, clock.Reserve(1_000));      // held 1000-1250
        Assert.Equal(400, clock.Reserve(1_100));    // mid-hold: pressed at 1500, held to 1750
        Assert.Equal(0, clock.Reserve(2_100));      // released at 1750, 350 ms ago
    }

    [Fact]
    public void A_late_release_moves_the_clock_on()
    {
        // Task.Delay overshoots; the release that actually happened is what the next press waits on.
        var clock = new A380EcpKeyPulse.PressClock();
        clock.Reserve(1_000);                          // scheduled release 1250
        clock.MarkReleased(1_300);

        Assert.Equal(150, clock.Reserve(1_400));
    }

    [Fact]
    public void An_early_mark_never_moves_a_later_reservation_back()
    {
        var clock = new A380EcpKeyPulse.PressClock();
        clock.Reserve(1_000);                          // release 1250
        clock.Reserve(1_100);                          // pressed 1500, release 1750
        clock.MarkReleased(1_260);                     // the FIRST press's release lands

        Assert.Equal(250 - (1_800 - 1_750), clock.Reserve(1_800));
    }

    [Fact]
    public void The_cycle_is_the_throttle_fbw_ships()
    {
        // FwsCore.ts: `fwsUpdateThrottler = new UpdateThrottler(125)`. Re-derive both constants
        // above if FBW changes it.
        Assert.Equal(125, A380EcpKeyPulse.FwsCycleMs);
    }
}
