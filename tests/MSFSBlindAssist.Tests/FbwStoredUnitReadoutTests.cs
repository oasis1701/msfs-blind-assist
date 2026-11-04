// FBW's script writes some L:vars in a real unit (nautical miles, knots, feet), which the sim
// stores in its base unit (metres, m/s), and the app reads an on-request L:var raw. These
// readouts convert back (FlyByWireStoredUnits). Values are the stored ones, as the app gets
// them. The live reads they come from (A380, MSFS 2020): cross-track 264.51 stored = 0.1428 NM
// (2026-10-09) and -354.03 stored = -0.19 NM (2026-11-04); preselected speed -1 kt stored as
// -0.5144 (2026-11-04). Run under sv-SE, whose decimal comma must not reach the text.

using System.Globalization;
using System.Runtime.CompilerServices;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Tests.FirstOfficer;

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

    // The Vertical Deviation row reads its siblings from the panel's sim handle (_displaySim):
    // hand it a seeded cache holding the aircraft's own keys, as the panel's reads would. Both
    // are reached by UnsafeAccessor, never reflection (SeededSimConnectCache explains why).
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_displaySim")]
    private static extern ref SimConnectManager? A320DisplaySim(FlyByWireA320Definition def);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_displaySim")]
    private static extern ref SimConnectManager? A380DisplaySim(FlyByWireA380Definition def);

    private static SimConnectManager Seeded(string altitudeKey, double altitudeFt, double targetFt) =>
        SeededSimConnectCache.With((altitudeKey, altitudeFt),
            ("A32NX_PFD_TARGET_ALTITUDE", MetresFromFeet(targetFt)),
            ("A32NX_RADIO_RECEIVER_GS_IS_VALID", 0));

    [Fact]
    public void Profile_deviation_reads_feet_on_each_airframe_from_its_own_altitude_key()
    {
        // The A32NX and A330 register indicated altitude as INDICATED_ALTITUDE, the A380 as
        // "INDICATED ALTITUDE"; the A32NX row used the A380's and always read "active".
        foreach (FlyByWireA320Definition def in new[] { new FlyByWireA320Definition(), new HeadwindA330Definition() })
        {
            A320DisplaySim(def) = Seeded("INDICATED_ALTITUDE", 520, 1000);
            Assert.Equal("480 feet below profile", Text(def, "A32NX_PFD_LINEAR_DEVIATION_ACTIVE", 1));
        }
        var a380 = new FlyByWireA380Definition();
        A380DisplaySim(a380) = Seeded("INDICATED ALTITUDE", 520, 1000);
        Assert.Equal("480 feet below profile", Text(a380, "A32NX_PFD_LINEAR_DEVIATION_ACTIVE", 1));
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
