// RunwayWatchLinger — keeping a runway watch alive across a CROSSING. PR #247 B1 review: Continue at
// a crossing hold ends the Holding source at the hold line, and the on-the-runway source only starts
// at the pavement edge, so the watch stopped, restarted on the pavement and spoke a second full first
// status mid-crossing (interrupting when anything was on the runway or on short final), then stopped
// again on vacating.
//
// Fixture: a 30 m half-width runway; the linger began 110 m from the centreline. Every case runs on
// both sides (side +1 / -1): the verdict depends on the sign of the offset only relative to where the
// linger began.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class RunwayWatchLingerTests
{
    private static readonly DateTime T0 = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
    private const double HalfWidthM = 30.0;
    private const double StartM = 110.0;

    private static RunwayLingerVerdict Evaluate(double side, double lateralM, double seconds = 5.0)
        => RunwayWatchLinger.Evaluate(new RunwayWatchLinger.Anchor(StartM * side, T0),
            lateralM * side, HalfWidthM, T0.AddSeconds(seconds));

    [Theory]
    [InlineData(1.0)]
    [InlineData(-1.0)]
    public void Approaching_the_runway_from_where_it_began_keeps_the_watch(double side)
        => Assert.Equal(RunwayLingerVerdict.Keep, Evaluate(side, 60));

    [Theory]
    [InlineData(1.0, 10)]
    [InlineData(1.0, -10)]
    [InlineData(-1.0, 10)]
    [InlineData(-1.0, -10)]
    public void On_the_pavement_keeps_the_watch(double side, double lateralM)
        => Assert.Equal(RunwayLingerVerdict.Keep, Evaluate(side, lateralM));

    [Theory]
    [InlineData(1.0, -85)]
    [InlineData(1.0, -90)]    // exactly half-width + 60 m: not yet clear
    [InlineData(-1.0, -85)]
    [InlineData(-1.0, -90)]
    public void Past_the_centreline_but_not_yet_clear_keeps_the_watch(double side, double lateralM)
        => Assert.Equal(RunwayLingerVerdict.Keep, Evaluate(side, lateralM));

    [Theory]
    [InlineData(1.0)]
    [InlineData(-1.0)]
    public void Clear_on_the_far_side_releases_the_watch(double side)
        => Assert.Equal(RunwayLingerVerdict.ClearFarSide, Evaluate(side, -91));

    [Theory]
    [InlineData(1.0, 140, RunwayLingerVerdict.Keep)]        // the boundary: 30 m farther out than it began
    [InlineData(1.0, 141, RunwayLingerVerdict.TurnedAway)]
    [InlineData(-1.0, 140, RunwayLingerVerdict.Keep)]
    [InlineData(-1.0, 141, RunwayLingerVerdict.TurnedAway)]
    public void Moving_away_on_the_side_it_began_releases_the_watch(double side, double lateralM, RunwayLingerVerdict expected)
        => Assert.Equal(expected, Evaluate(side, lateralM));

    [Theory]
    [InlineData(1.0)]
    [InlineData(-1.0)]
    public void A_stopped_aircraft_does_not_keep_the_watch_past_a_minute(double side)
    {
        Assert.Equal(RunwayLingerVerdict.Keep, Evaluate(side, 60, seconds: 60.0));
        Assert.Equal(RunwayLingerVerdict.TimedOut, Evaluate(side, 60, seconds: 60.001));
    }

    // The runway's along-track extent for CanBegin: pavement from 400 m before the along origin to
    // 2,600 m after it (deliberately not zero-based, so both ends are real bounds).
    private const double ExtentMinM = -400.0;
    private const double ExtentMaxM = 2600.0;

    [Fact]
    public void A_linger_begins_only_near_the_centreline()
    {
        Assert.True(RunwayWatchLinger.CanBegin(250.0, 1000.0, ExtentMinM, ExtentMaxM));
        Assert.True(RunwayWatchLinger.CanBegin(-250.0, 1000.0, ExtentMinM, ExtentMaxM));
        Assert.False(RunwayWatchLinger.CanBegin(250.01, 1000.0, ExtentMinM, ExtentMaxM));
        Assert.False(RunwayWatchLinger.CanBegin(-250.01, 1000.0, ExtentMinM, ExtentMaxM));
    }

    // PR #247 B2 review: a lateral offset alone is measured against the runway's infinite centreline,
    // so a watch lost kilometres beyond a runway end — or carried to another airport with a
    // same-named runway — could still begin a linger.
    [Theory]
    [InlineData(-550.0, true)]    // 150 m before the first end
    [InlineData(-551.0, false)]   // 151 m before it
    [InlineData(2750.0, true)]    // 150 m beyond the far end
    [InlineData(2751.0, false)]   // 151 m beyond it
    [InlineData(-400.0, true)]    // at either end
    [InlineData(2600.0, true)]
    public void A_linger_begins_only_beside_its_runway(double alongM, bool expected)
    {
        Assert.Equal(expected, RunwayWatchLinger.CanBegin(50.0, alongM, ExtentMinM, ExtentMaxM));
        Assert.Equal(expected, RunwayWatchLinger.CanBegin(-50.0, alongM, ExtentMinM, ExtentMaxM));
    }
}
