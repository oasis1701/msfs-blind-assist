// FBW's script writes some L:vars in a real unit (nautical miles, knots, feet), which the sim
// stores in its base unit (metres, m/s), and the app reads an on-request L:var raw. These
// readouts convert back (FlyByWireStoredUnits). Values are the stored ones, as the app gets
// them. The live reads they come from (A380, MSFS 2020): cross-track 264.51 stored = 0.1428 NM
// (2026-10-09) and -354.03 stored = -0.19 NM (2026-11-04); preselected speed -1 kt stored as
// -0.5144 (2026-11-04). Run under sv-SE, whose decimal comma must not reach the text.

using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FbwStoredUnitReadoutTests : IDisposable
{
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;

    public FbwStoredUnitReadoutTests() => CultureInfo.CurrentCulture = new CultureInfo("sv-SE");

    public void Dispose() => CultureInfo.CurrentCulture = _previousCulture;

    private static string Text(IAircraftDefinition def, string key, double stored)
    {
        Assert.True(def.TryGetDisplayOverride(key, stored, out string text), $"{key} has no readout");
        return text;
    }

    private static double Metres(double nm) => nm * FlyByWireStoredUnits.MetresPerNauticalMile;
    private static double MetresPerSecond(double kt) => kt * FlyByWireStoredUnits.MetresPerSecondPerKnot;
    private static double MetresFromFeet(double ft) => ft * FlyByWireStoredUnits.MetresPerFoot;

    [Fact]
    public void The_live_cross_track_reads_reconvert()
    {
        Assert.Equal(0.1428, FlyByWireStoredUnits.NauticalMiles(264.51), 4);
        Assert.Equal(-0.1912, FlyByWireStoredUnits.NauticalMiles(-354.0339), 4);
        Assert.Equal(-1.0, FlyByWireStoredUnits.Knots(-0.514444444444444), 6);
    }

    [Fact]
    public void A380_cross_track_reads_nautical_miles_with_a_dot()
    {
        var def = new FlyByWireA380Definition();
        Assert.Equal("0.14 NM right of track", Text(def, "A32NX_FG_CROSS_TRACK_ERROR", 264.51));
        Assert.Equal("0.19 NM left of track", Text(def, "A32NX_FG_CROSS_TRACK_ERROR", -354.0339));
        Assert.Equal("on track", Text(def, "A32NX_FG_CROSS_TRACK_ERROR", 10));
    }

    [Fact]
    public void A320_cross_track_reads_nautical_miles_with_a_dot()
    {
        var def = new FlyByWireA320Definition();
        Assert.Equal("0.14 NM right", Text(def, "A32NX_FG_CROSS_TRACK_ERROR", 264.51));
        Assert.Equal("2.50 NM left", Text(def, "A32NX_FG_CROSS_TRACK_ERROR", Metres(-2.5)));
        Assert.Equal("On track", Text(def, "A32NX_FG_CROSS_TRACK_ERROR", 10));
    }

    [Fact]
    public void Preselected_speed_reads_knots_on_both()
    {
        foreach (IAircraftDefinition def in new IAircraftDefinition[] { new FlyByWireA380Definition(), new FlyByWireA320Definition() })
        {
            Assert.Equal("250 knots", Text(def, "A32NX_SpeedPreselVal", MetresPerSecond(250)));
            Assert.Equal("none", Text(def, "A32NX_SpeedPreselVal", MetresPerSecond(-1)));
        }
    }

    [Fact]
    public void A380_managed_speed_reads_knots()
    {
        var def = new FlyByWireA380Definition();
        Assert.Equal("280 knots", Text(def, "A32NX_SPEEDS_MANAGED_PFD", MetresPerSecond(280)));
        Assert.Equal("none", Text(def, "A32NX_SPEEDS_MANAGED_PFD", 0));
    }

    [Fact]
    public void Profile_target_altitude_reads_feet_on_both()
    {
        foreach (IAircraftDefinition def in new IAircraftDefinition[] { new FlyByWireA380Definition(), new FlyByWireA320Definition() })
        {
            Assert.Equal("12000 feet", Text(def, "A32NX_PFD_TARGET_ALTITUDE", MetresFromFeet(12000)));
            Assert.Equal("none", Text(def, "A32NX_PFD_TARGET_ALTITUDE", 0));
        }
    }

    [Fact]
    public void The_headwind_a330_inherits_the_a320_readouts()
    {
        // Its package writes the same vars in the same units (checked 2026-11-04).
        var def = new HeadwindA330Definition();
        Assert.Equal("0.14 NM right", Text(def, "A32NX_FG_CROSS_TRACK_ERROR", 264.51));
        Assert.Equal("250 knots", Text(def, "A32NX_SpeedPreselVal", MetresPerSecond(250)));
        Assert.Equal("12000 feet", Text(def, "A32NX_PFD_TARGET_ALTITUDE", MetresFromFeet(12000)));
    }
}
