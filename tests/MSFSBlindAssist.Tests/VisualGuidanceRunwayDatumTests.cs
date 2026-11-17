using System.Globalization;
using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Where visual guidance's path starts and how steep it is: from the painted landing threshold, not the
/// runway end, when the threshold is displaced (KATL 27R 512 ft, KJFK 22R 3,471 ft); at the ILS
/// glideslope's angle, else the PAPI's, else 3° (<see cref="VisualGuidanceVertical.PathAngleDeg"/>).
/// </summary>
public class VisualGuidanceRunwayDatumTests
{
    // ---- The path's angle -------------------------------------------------------------------

    [Theory]
    [InlineData(3.0, 0.0, 3.0)]     // an ILS, no PAPI in the navdata
    [InlineData(3.0, 3.5, 3.0)]     // the ILS wins over a PAPI that disagrees
    [InlineData(5.5, 3.0, 5.5)]     // London City in the MSFS 2024 navdata: PAPI 3.0, ILS 5.5
    [InlineData(0.0, 4.0, 4.0)]     // no ILS: the PAPI's angle
    [InlineData(0.0, 3.5, 3.5)]
    [InlineData(0.0, 2.5, 2.5)]
    [InlineData(0.0, 0.0, 3.0)]     // neither: 3°
    [InlineData(0.0, 9.0, 3.0)]     // LOWL 26's PAPI reads 9.0: not believable
    [InlineData(0.0, 1.5, 3.0)]
    [InlineData(0.1, 3.5, 3.5)]     // an ILS angle that is not believable gives way to the PAPI
    [InlineData(30.0, 0.0, 3.0)]
    public void The_path_takes_the_ILS_angle_then_the_PAPI_then_3_degrees(double ilsDeg, double papiDeg, double expected)
        => Assert.Equal(expected, VisualGuidanceVertical.PathAngleDeg(ilsDeg, papiDeg));

    // ---- The displaced threshold ------------------------------------------------------------

    [Fact]
    public void On_a_displaced_threshold_the_distance_is_to_the_painted_threshold()
    {
        var rig = new VisualGuidanceTestRig(thresholdOffsetFt: 3471.0);
        rig.Place(6.0, 0, 0);
        rig.UpdateAt(0);

        Assert.StartsWith("On final, 6.0 miles. On the centreline. ", rig.Readout);
    }

    [Fact]
    public void On_a_displaced_threshold_the_path_crosses_the_painted_threshold()
    {
        // On the 3° path to the painted threshold, 6 nm out: the runway end's path would be 182 ft higher.
        var rig = new VisualGuidanceTestRig(thresholdOffsetFt: 3471.0);
        rig.Place(6.0, 0, 0);
        rig.UpdateAt(0);

        Assert.Contains(". On path, ", rig.Readout);
    }

    [Fact]
    public void Without_an_ILS_the_path_follows_the_PAPI()
    {
        // On a 4° path 6 nm out: a 3° path would be 639 ft lower.
        var rig = new VisualGuidanceTestRig(glideslopeAngleDeg: 0.0, papiAngleDeg: 4.0, pathDeg: 4.0);
        rig.Place(6.0, 0, 0, vsFpm: -990);
        rig.UpdateAt(0);

        Assert.Contains(". On path, ", rig.Readout);
    }

    [Fact]
    public void The_recording_starts_at_the_painted_threshold_with_the_paths_angle()
    {
        var lines = new List<string>();
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            _ = new VisualGuidanceTestRig(lines.Add, thresholdOffsetFt: 3471.0, glideslopeAngleDeg: 0.0, papiAngleDeg: 3.5);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }

        string start = Assert.Single(lines, l => l.StartsWith("VGREC start", StringComparison.Ordinal));
        // Within a metre of the painted threshold (the runway end is 0.0139° of longitude, 1,058 m, west of
        // it): the rig projects the end back along the course and the app the threshold forward, and on a
        // great circle those two meet 0.2 m apart.
        double Field(string key) => double.Parse(
            System.Text.RegularExpressions.Regex.Match(start, $" {key}=([-0-9.]+) ").Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.Equal(47.0, Field("lat"), 0.00001);
        Assert.Equal(8.0, Field("lon"), 0.00001);
        Assert.Contains(" gp=3.50 ", start);
    }
}
