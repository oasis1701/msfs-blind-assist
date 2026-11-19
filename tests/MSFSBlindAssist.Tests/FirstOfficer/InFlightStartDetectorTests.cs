using System;
using MSFSBlindAssist.FirstOfficer;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// Owner, 2026-10-14: when MSFSBA or the First Officer window starts with the aircraft already
/// flying, the checklists through After Takeoff are marked complete. "Flying without a takeoff
/// the window watched": first seen at ≥ 100 ft and ≥ 50 kt, or ≥ 500 ft within 5 s of the first
/// airborne sample at flying speed (known AGL and IAS, IAS ≥ 50 kt) after the ground (a flight
/// loaded in the air; the first airborne samples may still carry a stale ground AGL, or be zeroed
/// for longer than the window). A watched takeoff never fires. Thresholds are judgements.
/// </summary>
public class InFlightStartDetectorTests
{
    private static readonly DateTime T0 = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void First_seen_flying_fires_once()
    {
        var d = new InFlightStartDetector();
        Assert.True(d.Update(false, 35000, 280, T0));
        Assert.False(d.Update(false, 35000, 280, T0.AddSeconds(1)));
        Assert.False(d.Update(false, 34000, 280, T0.AddMinutes(5)));
    }

    [Fact]
    public void Unknown_inputs_never_fire_and_change_nothing()
    {
        var d = new InFlightStartDetector();
        Assert.False(d.Update(false, double.NaN, 280, T0));
        Assert.False(d.Update(false, 35000, double.NaN, T0));
        Assert.True(d.Update(false, 35000, 280, T0.AddSeconds(2)));
    }

    [Fact]
    public void First_seen_low_after_liftoff_waits_until_100_ft()
    {
        var d = new InFlightStartDetector();
        Assert.False(d.Update(false, 40, 150, T0));
        Assert.False(d.Update(false, 90, 155, T0.AddSeconds(2)));
        Assert.True(d.Update(false, 120, 160, T0.AddSeconds(4)));
    }

    // ---- Fix round 1 (2026-10-14): the put-in-the-air window starts at the first airborne sample
    // ---- whose AGL and IAS are both known and IAS >= 50 kt, not at the first airborne sample.

    [Fact]
    public void Ground_then_zeroed_airborne_samples_for_10_s_then_cruise_fires()
    {
        // A flight loaded while the window sat on the ground: the sim may deliver zeroed samples
        // (not on the ground, ~0 ft, 0 kt) for longer than the window before the real data lands.
        var d = new InFlightStartDetector();
        Assert.False(d.Update(true, 0, 0, T0));
        for (int s = 1; s <= 10; s++)
            Assert.False(d.Update(false, 2, 0, T0.AddSeconds(s)));
        Assert.True(d.Update(false, 35000, 280, T0.AddSeconds(11)));
    }

    [Fact]
    public void Window_boundary_exactly_5_s_after_the_first_flying_speed_sample_is_still_inside()
    {
        // A stale low-but-fast first sample (5 ft, 60 kt) starts the window.
        var inside = new InFlightStartDetector();
        Assert.False(inside.Update(true, 0, 0, T0));
        Assert.False(inside.Update(false, 5, 60, T0.AddSeconds(1)));
        Assert.True(inside.Update(false, 8000, 250, T0.AddSeconds(1) + InFlightStartDetector.PutInAirWindow));

        var outside = new InFlightStartDetector();
        Assert.False(outside.Update(true, 0, 0, T0));
        Assert.False(outside.Update(false, 5, 60, T0.AddSeconds(1)));
        Assert.False(outside.Update(false, 8000, 250,
            T0.AddSeconds(1) + InFlightStartDetector.PutInAirWindow + TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public void Stale_low_then_fresh_high_just_past_the_window_is_a_takeoff()
    {
        var d = new InFlightStartDetector();
        Assert.False(d.Update(true, 0, 0, T0));
        Assert.False(d.Update(false, 5, 60, T0.AddSeconds(1)));        // starts the window
        Assert.False(d.Update(false, 8000, 250, T0.AddSeconds(7)));    // 6 s later: past it, a takeoff
        Assert.False(d.Update(false, 8000, 250, T0.AddSeconds(8)));
        Assert.False(d.Update(false, 35000, 280, T0.AddMinutes(10)));
    }

    [Theory]
    [InlineData(double.NaN, 150)]
    [InlineData(20, double.NaN)]
    [InlineData(double.NaN, double.NaN)]
    public void Ground_then_unknown_airborne_sample_does_not_start_the_window(double agl, double ias)
    {
        var d = new InFlightStartDetector();
        Assert.False(d.Update(true, 0, 0, T0));
        Assert.False(d.Update(false, agl, ias, T0.AddSeconds(1)));     // unknown: neither fires nor starts the window
        Assert.True(d.Update(false, 8000, 250, T0.AddSeconds(8)));     // 7 s on: still inside, as the window starts here
    }

    [Fact]
    public void Slower_than_50_kt_in_the_air_neither_starts_nor_advances_the_window_and_50_kt_does()
    {
        var d = new InFlightStartDetector();
        Assert.False(d.Update(true, 0, 0, T0));
        Assert.False(d.Update(false, 800, 49.9, T0.AddSeconds(1)));    // too slow: nothing starts
        Assert.False(d.Update(false, 800, 49.9, T0.AddSeconds(30)));   // and it does not run out either
        Assert.False(d.Update(false, 10, 50, T0.AddSeconds(31)));      // exactly 50 kt starts the window
        Assert.True(d.Update(false, 900, 120, T0.AddSeconds(34)));
    }

    [Fact]
    public void Main_menu_or_parked_never_fires()
    {
        var d = new InFlightStartDetector();
        Assert.False(d.Update(false, 2000, 0, T0));          // 0 kt: not flying
        Assert.False(d.Update(true, 0, 0, T0.AddSeconds(1)));
        Assert.False(d.Update(true, 0, 0, T0.AddSeconds(2)));
    }

    [Fact]
    public void Watched_takeoff_never_fires()
    {
        var d = new InFlightStartDetector();
        Assert.False(d.Update(true, 3, 140, T0));
        Assert.False(d.Update(false, 10, 150, T0.AddSeconds(1)));   // liftoff
        Assert.False(d.Update(false, 180, 160, T0.AddSeconds(4)));  // brisk climb, still < 500
        Assert.False(d.Update(false, 600, 170, T0.AddSeconds(12))); // past the window: a takeoff
        Assert.False(d.Update(false, 35000, 280, T0.AddMinutes(30)));
    }

    [Fact]
    public void Ground_then_put_in_the_air_fires()
    {
        var d = new InFlightStartDetector();
        Assert.False(d.Update(true, 0, 0, T0));
        Assert.True(d.Update(false, 8000, 250, T0.AddMinutes(3)));
    }

    [Fact]
    public void Ground_then_stale_low_sample_then_high_within_window_fires()
    {
        var d = new InFlightStartDetector();
        Assert.False(d.Update(true, 0, 0, T0));
        Assert.False(d.Update(false, 2, 0, T0.AddMinutes(2)));                  // stale ground AGL/IAS
        Assert.True(d.Update(false, 8000, 250, T0.AddMinutes(2).AddSeconds(1))); // fresh data
    }

    [Fact]
    public void After_landing_a_second_flight_loaded_in_the_air_fires_again()
    {
        var d = new InFlightStartDetector();
        Assert.True(d.Update(false, 35000, 280, T0));
        Assert.False(d.Update(true, 0, 30, T0.AddHours(1)));                     // landed
        Assert.True(d.Update(false, 12000, 250, T0.AddHours(1).AddMinutes(2))); // loaded in the air
    }

    // A flight loaded in the air straight from another flight in the air sends no ground sample in
    // between: the window resets the detector on the flight load, so it fires for the new flight
    // (PR 160 second review). Without the reset it stayed "airborne" and never fired again.
    [Fact]
    public void A_reset_between_two_flights_loaded_in_the_air_fires_again()
    {
        var d = new InFlightStartDetector();
        Assert.True(d.Update(false, 35000, 280, T0));
        Assert.False(d.Update(false, 12000, 250, T0.AddMinutes(30)));            // no reset: one flight

        d.Reset();
        Assert.True(d.Update(false, 12000, 250, T0.AddMinutes(31)));
    }

    [Fact]
    public void A_reset_then_a_flight_loaded_on_the_ground_and_a_takeoff_never_fires()
    {
        var d = new InFlightStartDetector();
        Assert.True(d.Update(false, 35000, 280, T0));

        d.Reset();
        Assert.False(d.Update(true, 0, 0, T0.AddMinutes(1)));
        Assert.False(d.Update(false, 10, 150, T0.AddMinutes(20)));               // liftoff
        Assert.False(d.Update(false, 600, 170, T0.AddMinutes(20).AddSeconds(12)));
    }

    [Fact]
    public void A_bounce_on_landing_never_fires()
    {
        var d = new InFlightStartDetector();
        Assert.True(d.Update(false, 35000, 280, T0));
        Assert.False(d.Update(true, 0, 140, T0.AddHours(1)));
        Assert.False(d.Update(false, 8, 135, T0.AddHours(1).AddSeconds(1)));
        Assert.False(d.Update(true, 0, 130, T0.AddHours(1).AddSeconds(2)));
    }
}
