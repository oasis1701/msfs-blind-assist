using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Exact fs2024.sqlite airport rows (ident, reference point, bounding box, taxi-path count),
/// rounded to six decimals, for the "which airport am I at" tests. John Wayne (KSNA) and the
/// heliport beside it are the case the resolver exists for: 41 of KSNA's stands lie inside BOTH
/// boxes (the heliport's grown by the resolver's 300 m margin) and nearer the heliport's
/// reference point than KSNA's own. Stand P 86 is one of them.
/// </summary>
internal static class Fs2024AirportRows
{
    public static readonly AirportCandidate Ksna =
        new("KSNA", 33.675663, -117.868233, -117.877441, -117.860291, 33.682686, 33.663815, 1598);

    public static readonly AirportCandidate Heliport10Cl =
        new("10CL", 33.671719, -117.874863, -117.874863, -117.874695, 33.671719, 33.671665, 0);

    /// <summary>KSNA stand P 86 (a small GA ramp stand): 290 m from 10CL's reference point, 467 m
    /// from KSNA's.</summary>
    public const double KsnaStandP86Lat = 33.673447, KsnaStandP86Lon = -117.872520;
}
