using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Precomputed runway-aligned equirectangular frame for signed cross-track /
/// along-track projections. One shared home for the math that previously lived
/// as identical local lambdas in HoldShortNodeResolver, TaxiAssistForm's
/// FindFarSideRunwayNode, and TaxiAssistForm's GetTaxiwaysCrossingRunway —
/// a projection fix applied to one copy silently missed the others.
///
/// Sign convention (unchanged from the original lambdas):
///   SignedCrossTrack positive = LEFT side looking down the runway heading,
///   negative = RIGHT side. Along increases down the runway heading from the
///   start threshold.
/// </summary>
public readonly struct RunwayFrame
{
    private const double DEG_TO_M_LAT = 111320.0;

    private readonly double _rwEast;
    private readonly double _rwNorth;
    private readonly double _degToMLon;
    private readonly double _startLat;
    private readonly double _startLon;

    /// <summary>Runway length in metres (feet-stored length converted; falls
    /// back to the threshold-to-threshold great-circle distance).</summary>
    public double LengthM { get; }

    private RunwayFrame(double rwEast, double rwNorth, double degToMLon,
        double startLat, double startLon, double lengthM)
    {
        _rwEast = rwEast;
        _rwNorth = rwNorth;
        _degToMLon = degToMLon;
        _startLat = startLat;
        _startLon = startLon;
        LengthM = lengthM;
    }

    /// <summary>
    /// Builds the frame for <paramref name="runway"/>. <paramref name="refLat"/>
    /// is the latitude at which degrees-longitude are scaled to metres (callers
    /// historically use the aircraft latitude or the runway start latitude —
    /// both are fine at airport scale).
    /// </summary>
    public static RunwayFrame For(Runway runway, double refLat)
    {
        double hdgRad = runway.Heading * Math.PI / 180.0;
        double lengthM = runway.Length > 0
            ? runway.Length * 0.3048   // stored in feet
            : TaxiGraph.CalculateDistanceMeters(
                runway.StartLat, runway.StartLon, runway.EndLat, runway.EndLon);
        return new RunwayFrame(
            Math.Sin(hdgRad), Math.Cos(hdgRad),
            DEG_TO_M_LAT * Math.Cos(refLat * Math.PI / 180.0),
            runway.StartLat, runway.StartLon, lengthM);
    }

    /// <summary>Signed cross-track distance (metres) of a point from the runway
    /// centerline: positive = left of the heading, negative = right.</summary>
    public double SignedCrossTrack(double lat, double lon)
    {
        double pDy = (lat - _startLat) * DEG_TO_M_LAT;
        double pDx = (lon - _startLon) * _degToMLon;
        return _rwEast * pDy - _rwNorth * pDx;
    }

    /// <summary>Along-track distance (metres) of a point down the runway heading
    /// from the start threshold (negative = before the threshold).</summary>
    public double Along(double lat, double lon)
    {
        double pDx = (lon - _startLon) * _degToMLon;
        double pDy = (lat - _startLat) * DEG_TO_M_LAT;
        return _rwEast * pDx + _rwNorth * pDy;
    }
}
