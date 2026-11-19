// Tests for ParkingSpot.FitsAircraft — the stand-size filter behind the taxi planner's
// "fit aircraft" checkbox and the gate-teleport dialog.
//
// Two rules, both learned the hard way:
//
//   UNITS. The method takes FEET. A GSX spot carries the author's stated limit in METRES
//   (MaxWingspanMeters) and its Radius is metres too (maxwingspan/2), so the original
//   "Radius >= wingspanFeet/2" test compared metres against a feet threshold and hid
//   almost every GSX stand.
//
//   BUCKETS. A navdata radius is not a measurement, it is a round METRE value rendered in
//   feet, and the common buckets land just UNDER the ICAO code ceiling they stand for:
//   18 m radius caps a 36 m span (exactly Code C), 40 m caps 80 m (exactly Code F), but
//   32 m caps 64 m while Code E runs to 65 m. Live 2026-09-20 at EGLL that excluded a
//   747-400 (64.44 m / 211.4 ft) from all 86 of Heathrow's 105 ft Gate Heavy stands by
//   0.7 ft, so the pilot's list opened with ten cargo stands and the first passenger gate
//   was 2.4 km away across an active runway.
//
// Whole-DB measurement for the 2 % tolerance (fs2020, 299,014 parking rows): a 747-400
// gains 491 stands and a 777-300ER 493, while an A380 gains NONE — the tolerance reaches
// the Code E ceiling and stops short of Code F. At EGLL alone: 747-400 27 -> 132 stands,
// A380 5 -> 5, A320 214 -> 214.

using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Tests;

public class ParkingSpotFitTests
{
    // Wing spans in FEET, as SimConnectManager.AircraftWingSpan reports them.
    private const double A320 = 117.5;
    private const double B744 = 211.4;   // 64.44 m — ICAO Code E, just under the 65 m line
    private const double B77W = 212.7;   // 64.80 m — Code E
    private const double B748 = 224.4;   // 68.40 m — Code F
    private const double A388 = 261.8;   // 79.75 m — Code F, just under the 80 m line

    private static ParkingSpot Navdata(double radiusFeet) =>
        new() { Source = GateSource.Navdata, Radius = radiusFeet };

    private static ParkingSpot Gsx(double? maxWingspanMeters) =>
        new()
        {
            Source = GateSource.Gsx,
            MaxWingspanMeters = maxWingspanMeters,
            // A GSX spot's Radius is METRES (maxwingspan/2) — set it the way the mapper
            // does, so a test that accidentally fell through to the navdata branch would
            // give an obviously wrong answer rather than a plausible one.
            Radius = maxWingspanMeters.HasValue ? maxWingspanMeters.Value / 2.0 : 100.0,
        };

    // ── the EGLL case ────────────────────────────────────────────────────

    [Fact]
    public void A747_FitsAGateHeavyStand_ItMissedByThreeQuartersOfAFoot()
    {
        // 105 ft radius = 32.0 m = the Code E bucket. Half of a 747-400's span is 105.7 ft.
        Assert.True(Navdata(105).FitsAircraft(B744));
        Assert.True(Navdata(105).FitsAircraft(B77W));
    }

    [Fact]
    public void TheToleranceStopsShortOfTheNextCodeLetter()
    {
        // A Code F aircraft must still be refused a Code E stand — that is the whole
        // reason the tolerance is 2 % and not "a bit more to be safe".
        Assert.False(Navdata(105).FitsAircraft(B748));
        Assert.False(Navdata(105).FitsAircraft(A388));

        // ...and the A380 still only fits the 40 m bucket, not the 38.1 m one.
        Assert.False(Navdata(125).FitsAircraft(A388));
        Assert.True(Navdata(131).FitsAircraft(A388));
    }

    [Fact]
    public void AStandFarTooSmallIsStillRefused()
    {
        Assert.False(Navdata(23).FitsAircraft(A320));   // 7 m radius: a light-aircraft spot
        Assert.False(Navdata(46).FitsAircraft(B744));
    }

    [Fact]
    public void ANarrowbodyIsUnaffected()
    {
        Assert.True(Navdata(59).FitsAircraft(A320));    // 18 m = Code C, exactly
        Assert.True(Navdata(72).FitsAircraft(A320));
    }

    // ── GSX is preferred wherever its number exists ──────────────────────

    [Fact]
    public void GsxMaxWingspanIsUsedInMetres_NotAsAFeetRadius()
    {
        // 65 m stand, 747-400 at 64.44 m: fits. Were this compared as feet it would be
        // 65 >= 105.7 → false, the unit mix-up this method records.
        Assert.True(Gsx(65.0).FitsAircraft(B744));
        Assert.False(Gsx(52.0).FitsAircraft(B744));     // Code D stand: correctly refused
    }

    [Fact]
    public void GsxNumberWinsOverTheNavdataRadiusOnASpotCarryingBoth()
    {
        // The author said 52 m. The radius says otherwise — and is ignored, because a
        // stated limit beats a proxy for one.
        var spot = new ParkingSpot
        {
            Source = GateSource.Navdata,
            Radius = 131,                 // would admit an A380 on the navdata rule
            MaxWingspanMeters = 52.0,     // but the stand is stated Code D
        };

        Assert.False(spot.FitsAircraft(B744));
        Assert.False(spot.FitsAircraft(A388));
    }

    [Fact]
    public void AGsxSpotWithNoStatedSizeIsNeverHidden()
    {
        // No size info is not the same as "too small" — hiding it would lose the pilot a
        // stand that may well be theirs.
        Assert.True(Gsx(null).FitsAircraft(A388));
    }

    [Fact]
    public void AnUnknownAircraftWingspanMakesTheFilterANoOp()
    {
        Assert.True(Navdata(23).FitsAircraft(0));
        Assert.True(Navdata(23).FitsAircraft(-1));
        Assert.True(Gsx(10.0).FitsAircraft(0));
    }
}
