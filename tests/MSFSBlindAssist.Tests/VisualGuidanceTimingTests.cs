using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The timing arithmetic visual guidance's limits and filters run on (VG-20): the frame time from
/// the clock, rate × dt, per-second smoothing factors and rates over a time window.
/// </summary>
public class VisualGuidanceTimingTests
{
    private static readonly DateTime t0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FrameSeconds_is_zero_without_a_previous_update()
        => Assert.Equal(0.0, VisualGuidanceTiming.FrameSeconds(null, t0));

    [Fact]
    public void FrameSeconds_is_zero_when_the_clock_stands_still_or_goes_back()
    {
        Assert.Equal(0.0, VisualGuidanceTiming.FrameSeconds(t0, t0));
        Assert.Equal(0.0, VisualGuidanceTiming.FrameSeconds(t0, t0.AddSeconds(-2)));
    }

    [Fact]
    public void FrameSeconds_is_the_measured_interval()
        => Assert.Equal(1.0 / 30, VisualGuidanceTiming.FrameSeconds(t0, t0.AddSeconds(1.0 / 30)), 6);

    [Fact]
    public void FrameSeconds_counts_a_long_gap_as_one_second()
        => Assert.Equal(1.0, VisualGuidanceTiming.FrameSeconds(t0, t0.AddSeconds(10)));

    [Theory]
    [InlineData(0.0, 10.0, 3.0, 1.0 / 30, 0.1)]
    [InlineData(0.0, -10.0, 3.0, 0.5, -1.5)]
    [InlineData(0.0, 0.05, 3.0, 1.0 / 30, 0.05)]
    [InlineData(4.0, 10.0, 3.0, 0.0, 4.0)]
    public void RateLimit_moves_at_most_rate_times_frame_time(double prev, double target, double rate, double dt, double expected)
        => Assert.Equal(expected, VisualGuidanceTiming.RateLimit(prev, target, rate, dt), 9);

    [Fact]
    public void Smooth_at_one_second_is_the_old_per_update_factor()
        => Assert.Equal(13.0, VisualGuidanceTiming.Smooth(10, 20, 0.7, 1.0), 9);

    // 30 steps of 1/30 s toward a constant sample land where one step of 1 s does.
    [Fact]
    public void Smooth_is_the_same_in_one_step_or_thirty()
    {
        double oneStep = VisualGuidanceTiming.Smooth(10, 20, 0.7, 1.0);
        double value = 10;
        for (int i = 0; i < 30; i++) value = VisualGuidanceTiming.Smooth(value, 20, 0.7, 1.0 / 30);
        Assert.Equal(oneStep, value, 9);
    }

    [Fact]
    public void Smooth_starts_from_the_first_sample()
        => Assert.Equal(5.0, VisualGuidanceTiming.Smooth(null, 5, 0.7, 1.0 / 30));

    [Fact]
    public void Smooth_with_no_time_keeps_the_previous_value()
        => Assert.Equal(10.0, VisualGuidanceTiming.Smooth(10, 20, 0.7, 0.0));

    [Theory]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(60)]
    public void WindowedRate_measures_a_ramp_at_any_sample_rate(int hz)
    {
        var rate = new VisualGuidanceWindowedRate(1.0);
        for (int i = 0; i <= 3 * hz; i++)
        {
            double t = (double)i / hz;
            rate.Add(t0.AddSeconds(t), 2.0 * t);
        }
        Assert.NotNull(rate.Rate);
        Assert.Equal(2.0, rate.Rate!.Value, 6);
    }

    // Up at 2 per second for 3 s, then down at 1 per second for 2 s: a 1 s window sees only the descent.
    [Fact]
    public void WindowedRate_forgets_samples_older_than_its_window()
    {
        var rate = new VisualGuidanceWindowedRate(1.0);
        double value = 0;
        for (int i = 0; i <= 5 * 30; i++)
        {
            double t = i / 30.0;
            value = t <= 3.0 ? 2.0 * t : 6.0 - (t - 3.0);
            rate.Add(t0.AddSeconds(t), value);
        }
        Assert.NotNull(rate.Rate);
        Assert.Equal(-1.0, rate.Rate!.Value, 6);
    }

    [Fact]
    public void WindowedRate_has_no_rate_from_one_sample_or_a_tiny_span()
    {
        var rate = new VisualGuidanceWindowedRate(1.0);
        rate.Add(t0, 1.0);
        Assert.Null(rate.Rate);
        rate.Add(t0.AddSeconds(0.005), 2.0);
        Assert.Null(rate.Rate);
    }

    // A time sync can step the PC clock back. Samples stamped after the new time must not stay at
    // the head of the window: the rate starts afresh from the new clock and reads the ramp again.
    [Fact]
    public void WindowedRate_starts_afresh_after_the_clock_steps_back()
    {
        var rate = new VisualGuidanceWindowedRate(1.0);
        double t = 0;
        for (int i = 0; i <= 3 * 30; i++)
        {
            t = i / 30.0;
            rate.Add(t0.AddSeconds(t), 2.0 * t);
        }
        // The clock steps back 3 s; the value keeps ramping at 2 per second in real time.
        double value = 2.0 * t;
        var readings = new List<double?>();
        for (int i = 1; i <= 2 * 30; i++)
        {
            value += 2.0 / 30;
            rate.Add(t0.AddSeconds(t - 3.0 + i / 30.0), value);
            readings.Add(rate.Rate);
        }
        // Four places: over the first 1/30 s span, DateTime's 100 ns ticks shift the rate by about 2e-6.
        Assert.All(readings.Where(r => r.HasValue), r => Assert.Equal(2.0, r!.Value, 4));
        Assert.NotNull(readings[^1]);
    }

    [Fact]
    public void WindowedRate_clear_forgets_every_sample()
    {
        var rate = new VisualGuidanceWindowedRate(1.0);
        rate.Add(t0, 1.0);
        rate.Add(t0.AddSeconds(0.5), 2.0);
        rate.Clear();
        Assert.Null(rate.Rate);
    }
}
