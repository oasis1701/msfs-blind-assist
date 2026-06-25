namespace MSFSBlindAssist.Services.TaxiAugment;

/// <summary>
/// Self-contained geometry for the taxi-data augmentation/merger pipeline.
///
/// <para>This intentionally does NOT delegate to <c>NavigationCalculator</c> or
/// <c>TaxiGraph</c>'s geometry helpers, for two reasons. (1) Isolation: <c>TaxiDataMerger</c>
/// is a pure, dependency-light module exercised standalone by <c>tools/TaxiAugmentProbe</c>;
/// keeping its geometry here avoids dragging routing/UI types into the probe. (2) Correctness:
/// <see cref="MidpointLon"/>, <see cref="WrapDeltaDeg"/>, and the longitude-wrapping inside
/// <see cref="PointToSegmentMeters"/> are antimeridian-safe, whereas
/// <c>TaxiGraph.PerpendicularDistanceMetersStatic</c> uses a raw <c>(lon - lon)</c> equirectangular
/// projection that breaks across ±180°. Consolidating onto that helper would REGRESS the
/// antimeridian behavior the probe asserts (the "DATELINE" case). If these are ever unified,
/// the antimeridian-safe math must move INTO the shared helper, not the other way around.</para>
/// </summary>
public static class TaxiGeo
{
    public static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000.0;
        double dLat = (lat2 - lat1) * Math.PI / 180.0, dLon = (lon2 - lon1) * Math.PI / 180.0;
        double a = Math.Sin(dLat/2)*Math.Sin(dLat/2) +
                   Math.Cos(lat1*Math.PI/180)*Math.Cos(lat2*Math.PI/180)*Math.Sin(dLon/2)*Math.Sin(dLon/2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1-a));
    }
    public static double BearingDeg(double lat1, double lon1, double lat2, double lon2)
    {
        double y = Math.Sin((lon2-lon1)*Math.PI/180)*Math.Cos(lat2*Math.PI/180);
        double x = Math.Cos(lat1*Math.PI/180)*Math.Sin(lat2*Math.PI/180) -
                   Math.Sin(lat1*Math.PI/180)*Math.Cos(lat2*Math.PI/180)*Math.Cos((lon2-lon1)*Math.PI/180);
        double b = Math.Atan2(y, x) * 180.0 / Math.PI;
        return (b + 360.0) % 360.0;
    }
    public static double BearingDiffMod180(double a, double b)
    {
        double d = Math.Abs(a - b) % 180.0;
        return d > 90.0 ? 180.0 - d : d;
    }
    /// <summary>Wrap a longitude DELTA into [-180, 180] so distance/midpoint math is correct across the antimeridian.</summary>
    public static double WrapDeltaDeg(double d)
    {
        d %= 360.0;
        if (d > 180.0) d -= 360.0;
        else if (d < -180.0) d += 360.0;
        return d;
    }

    /// <summary>Midpoint longitude that is correct across the antimeridian (mid of 179.9 and -179.9 = 180, not 0).</summary>
    public static double MidpointLon(double lon1, double lon2)
    {
        double m = lon1 + WrapDeltaDeg(lon2 - lon1) / 2.0;
        if (m > 180.0) m -= 360.0;
        else if (m < -180.0) m += 360.0;
        return m;
    }

    public static double PointToSegmentMeters(double pLat,double pLon,double aLat,double aLon,double bLat,double bLon)
    {
        double mPerDegLat = 111320.0, mPerDegLon = 111320.0 * Math.Cos(aLat*Math.PI/180);
        // Wrap the longitude deltas so a segment near ±180° doesn't blow up (raw subtraction would give ~360°).
        double px=WrapDeltaDeg(pLon-aLon)*mPerDegLon, py=(pLat-aLat)*mPerDegLat;
        double bx=WrapDeltaDeg(bLon-aLon)*mPerDegLon, by=(bLat-aLat)*mPerDegLat;
        double len2=bx*bx+by*by;
        double t = len2<=1e-9 ? 0 : Math.Max(0,Math.Min(1,(px*bx+py*by)/len2));
        double cx=t*bx, cy=t*by;
        return Math.Sqrt((px-cx)*(px-cx)+(py-cy)*(py-cy));
    }
}
