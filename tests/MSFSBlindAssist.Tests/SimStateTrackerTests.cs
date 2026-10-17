// The pause, slew and sim-rate signals as MSFS 2024 delivered them live (probe, 2026-10-17): Esc
// reports Pause_EX1 = 8, active pause 4, closing the menu 0; the sim's own full-pause blips report 1; a
// flight load runs Sim 0, flags 0, Sim 1; SIMULATION RATE steps 0.25, 0.5, 1, 2, 4. Each test replays
// one measured sequence.

using System.Globalization;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class SimStateTrackerTests
{
    private static SimStateTracker Seeded(uint flags = 0, bool slew = false)
    {
        var tracker = new SimStateTracker();
        tracker.OnSimRunning(true);
        tracker.OnPauseFlags(flags);
        tracker.OnSlew(slew);
        return tracker;
    }

    [Fact]
    public void The_first_delivery_of_each_signal_only_seeds()
    {
        var tracker = new SimStateTracker();
        tracker.OnSimRunning(true);

        Assert.Equal(SimStateChange.None, tracker.OnPauseFlags(SimStateTracker.SimPauseFlag));
        Assert.Equal(SimStateChange.None, tracker.OnSlew(true));
    }

    [Fact]
    public void Esc_pauses_and_closing_the_menu_unpauses()
    {
        var tracker = Seeded();

        Assert.Equal(SimStateChange.Paused, tracker.OnPauseFlags(8));
        Assert.Equal(SimStateChange.Unpaused, tracker.OnPauseFlags(0));
    }

    [Fact]
    public void Active_pause_turns_on_and_off()
    {
        var tracker = Seeded();

        Assert.Equal(SimStateChange.ActivePauseOn, tracker.OnPauseFlags(4));
        Assert.Equal(SimStateChange.ActivePauseOff, tracker.OnPauseFlags(0));
    }

    [Fact]
    public void Esc_during_active_pause_ends_it_and_pauses_in_one_change()
    {
        var tracker = Seeded();
        tracker.OnPauseFlags(4);

        Assert.Equal(SimStateChange.Paused | SimStateChange.ActivePauseOff, tracker.OnPauseFlags(8));
        Assert.Equal(SimStateChange.Unpaused, tracker.OnPauseFlags(0));
    }

    [Fact]
    public void The_full_pause_flag_alone_is_never_spoken()
    {
        var tracker = Seeded();

        Assert.Equal(SimStateChange.None, tracker.OnPauseFlags(SimStateTracker.FullPauseFlag));
        Assert.Equal(SimStateChange.None, tracker.OnPauseFlags(0));
    }

    [Fact]
    public void Esc_on_top_of_the_sims_full_pause_reads_the_menu_bit_alone()
    {
        // Live: 1 (held after entering the cockpit), Esc -> 0, 9, 8, then 0 when the menu closed.
        var tracker = Seeded(flags: 1);

        Assert.Equal(SimStateChange.None, tracker.OnPauseFlags(0));
        Assert.Equal(SimStateChange.Paused, tracker.OnPauseFlags(9));
        Assert.Equal(SimStateChange.None, tracker.OnPauseFlags(8));
        Assert.Equal(SimStateChange.Unpaused, tracker.OnPauseFlags(0));
    }

    [Fact]
    public void A_flight_load_ends_the_menu_pause_silently()
    {
        // Leaving for the main menu goes through Esc (8); the next load runs Sim 0, flags 0, Sim 1,
        // then the 13 ms full-pause blips.
        var tracker = Seeded();
        Assert.Equal(SimStateChange.Paused, tracker.OnPauseFlags(8));

        tracker.OnSimRunning(false);
        Assert.Equal(SimStateChange.None, tracker.OnPauseFlags(0));
        tracker.OnSimRunning(true);
        Assert.Equal(SimStateChange.None, tracker.OnPauseFlags(1));
        Assert.Equal(SimStateChange.None, tracker.OnPauseFlags(0));

        Assert.Equal(SimStateChange.Paused, tracker.OnPauseFlags(8));
    }

    [Fact]
    public void Slew_turns_on_and_off()
    {
        var tracker = Seeded();

        Assert.Equal(SimStateChange.SlewOn, tracker.OnSlew(true));
        Assert.Equal(SimStateChange.None, tracker.OnSlew(true));
        Assert.Equal(SimStateChange.SlewOff, tracker.OnSlew(false));
    }

    [Fact]
    public void Slew_changes_while_no_flight_runs_are_silent()
    {
        var tracker = Seeded(slew: true);
        tracker.OnSimRunning(false);

        Assert.Equal(SimStateChange.None, tracker.OnSlew(false));
        tracker.OnSimRunning(true);
        Assert.Equal(SimStateChange.SlewOn, tracker.OnSlew(true));
    }

    [Fact]
    public void Reset_makes_the_next_deliveries_seed_again()
    {
        var tracker = Seeded();
        tracker.OnPauseFlags(8);
        tracker.OnSlew(true);

        tracker.Reset();

        Assert.Equal(SimStateChange.None, tracker.OnPauseFlags(0));
        Assert.Equal(SimStateChange.None, tracker.OnSlew(false));
    }

    [Fact]
    public void Before_any_sim_delivery_a_flight_counts_as_running()
    {
        var tracker = new SimStateTracker();
        tracker.OnPauseFlags(0);

        Assert.Equal(SimStateChange.Paused, tracker.OnPauseFlags(8));
    }

    [Theory]
    [InlineData(SimStateChange.Paused, "Sim paused")]
    [InlineData(SimStateChange.Unpaused, "Sim unpaused")]
    [InlineData(SimStateChange.ActivePauseOn, "Active pause on")]
    [InlineData(SimStateChange.ActivePauseOff, "Active pause off")]
    [InlineData(SimStateChange.SlewOn, "Slew on")]
    [InlineData(SimStateChange.SlewOff, "Slew off")]
    [InlineData(SimStateChange.Paused | SimStateChange.ActivePauseOff, "Sim paused. Active pause off")]
    public void Describe_words_each_change(SimStateChange change, string expected)
    {
        Assert.Equal(expected, SimStateTracker.Describe(change));
    }

    [Fact]
    public void Describe_says_nothing_when_nothing_changed()
    {
        Assert.Null(SimStateTracker.Describe(SimStateChange.None));
    }

    [Fact]
    public void The_first_sim_rate_only_seeds()
    {
        var tracker = new SimStateTracker();

        Assert.Null(tracker.OnSimRate(4));
    }

    [Fact]
    public void Each_sim_rate_step_is_reported()
    {
        // Live: 1 -> 0.5 -> 1 -> 0.5 -> 0.25 -> 0.5 -> 1 -> 2 -> 4.
        var tracker = new SimStateTracker();
        tracker.OnSimRate(1);

        Assert.Equal(0.5, tracker.OnSimRate(0.5));
        Assert.Equal(0.25, tracker.OnSimRate(0.25));
        Assert.Equal(1, tracker.OnSimRate(1));
        Assert.Equal(2, tracker.OnSimRate(2));
        Assert.Equal(4, tracker.OnSimRate(4));
    }

    [Fact]
    public void The_same_sim_rate_again_is_silent()
    {
        var tracker = new SimStateTracker();
        tracker.OnSimRate(2);

        Assert.Null(tracker.OnSimRate(2));
        Assert.Null(tracker.OnSimRate(2.0000001));
    }

    [Fact]
    public void A_sim_rate_of_zero_is_ignored()
    {
        var tracker = new SimStateTracker();
        tracker.OnSimRate(1);

        Assert.Null(tracker.OnSimRate(0));
        Assert.Null(tracker.OnSimRate(1));
    }

    [Fact]
    public void Sim_rate_changes_while_no_flight_runs_are_silent()
    {
        var tracker = new SimStateTracker();
        tracker.OnSimRate(4);
        tracker.OnSimRunning(false);

        Assert.Null(tracker.OnSimRate(1));
        tracker.OnSimRunning(true);
        Assert.Equal(2, tracker.OnSimRate(2));
    }

    [Fact]
    public void Reset_makes_the_next_sim_rate_seed_again()
    {
        var tracker = new SimStateTracker();
        tracker.OnSimRate(1);

        tracker.Reset();

        Assert.Null(tracker.OnSimRate(4));
    }

    [Theory]
    [InlineData(4, "Sim rate 4x")]
    [InlineData(1, "Sim rate 1x")]
    [InlineData(0.5, "Sim rate 0.5x")]
    [InlineData(0.25, "Sim rate 0.25x")]
    public void DescribeSimRate_uses_the_sim_performance_wording(double rate, string expected)
    {
        Assert.Equal(expected, SimStateTracker.DescribeSimRate(rate));
    }

    [Fact]
    public void DescribeSimRate_keeps_a_point_under_a_comma_decimal_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("Sim rate 0.5x", SimStateTracker.DescribeSimRate(0.5));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
