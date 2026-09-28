namespace MSFSBlindAssist.Services;

/// <summary>
/// Pure geometry and phrasing for <see cref="GroundTrafficMonitor"/>: closest point of approach,
/// relative motion, runway occupancy/final classification, route projection, the departure queue,
/// spoken names. No simulator state — every member is pinned by characterization tests.
/// Split by area into partial files: <c>GroundTrafficLogic.Geometry.cs</c>, <c>.Runway.cs</c>,
/// <c>.Queue.cs</c>, <c>.Motion.cs</c>, <c>.Names.cs</c>.
/// </summary>
internal static partial class GroundTrafficLogic
{
    private const double MetresPerDegLat = 110_540.0;
    private const double MetresPerDegLonEquator = 111_320.0;
    private const double KtsToMps = 0.514444;
    public const double MetresPerNm = 1852.0;
}
