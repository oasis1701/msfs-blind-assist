using System;
using MSFSBlindAssist.FirstOfficer;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// Owner, 2026-10-14: when MSFSBA or the First Officer window starts with the aircraft already
/// flying, the checklists through After Takeoff are marked complete. "Flying without a takeoff
/// the window watched": first seen at ≥ 100 ft and ≥ 50 kt, or ≥ 500 ft within 5 s of leaving the
/// ground (a flight loaded in the air; the first airborne sample may still carry a stale ground
/// AGL). A watched takeoff never fires. Thresholds are judgements.
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
