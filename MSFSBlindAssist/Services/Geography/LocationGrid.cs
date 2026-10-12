namespace MSFSBlindAssist.Services.Geography;

/// <summary>
/// The 0.25° grid that clusters the <c>place</c> table: rows run south to north, columns west
/// to east from -180°, and a cell id is <c>row * Cols + col</c>, so one row of cells is one
/// contiguous id range and a radius search is one range seek per row. A point is stored as its
/// cell plus its offset from the cell's south-west corner in 1e-5° (about a metre), which fits
/// in two bytes. Never change <see cref="CellDegrees"/> or <see cref="OffsetScale"/> without
/// bumping the database schema version: a built database encodes both.
/// </summary>
internal static class LocationGrid
{
    internal const double CellDegrees = 0.25;
    internal const int Rows = 720;  // 180 / 0.25
    internal const int Cols = 1440; // 360 / 0.25
    internal const double OffsetScale = 1e5;

    /// <summary>Kilometres per degree of latitude on the 6,371 km sphere the app's other
    /// distance code uses.</summary>
    internal const double KmPerDegree = 111.19492664455873;

    internal static int Row(double lat) =>
        Math.Clamp((int)Math.Floor((lat + 90.0) / CellDegrees), 0, Rows - 1);

    internal static int Col(double lon) =>
        Math.Clamp((int)Math.Floor((NormalizeLon(lon) + 180.0) / CellDegrees), 0, Cols - 1);

    internal static int Cell(double lat, double lon) => Row(lat) * Cols + Col(lon);

    /// <summary>The point's offset from its cell's south-west corner, in 1e-5°.</summary>
    internal static (int LatOffset, int LonOffset) Offsets(double lat, double lon)
    {
        int row = Row(lat), col = Col(lon);
        double south = -90.0 + row * CellDegrees;
        double west = -180.0 + col * CellDegrees;
        int latOff = (int)Math.Round((lat - south) * OffsetScale);
        int lonOff = (int)Math.Round((NormalizeLon(lon) - west) * OffsetScale);
        int max = (int)Math.Round(CellDegrees * OffsetScale);
        return (Math.Clamp(latOff, 0, max), Math.Clamp(lonOff, 0, max));
    }

    /// <summary>Back from cell + offsets to a position.</summary>
    internal static (double Lat, double Lon) Decode(int cell, int latOffset, int lonOffset)
    {
        int row = cell / Cols, col = cell % Cols;
        return (-90.0 + row * CellDegrees + latOffset / OffsetScale,
                -180.0 + col * CellDegrees + lonOffset / OffsetScale);
    }

    /// <summary>
    /// The cell-id ranges covering every point within <paramref name="radiusKm"/> of a position
    /// (a superset: callers still filter by true distance). One range per grid row, two where
    /// the box crosses ±180°. A box that reaches a pole, or is wider than the globe, takes
    /// every longitude.
    /// </summary>
    internal static List<(int From, int To)> RangesForRadius(double lat, double lon, double radiusKm)
    {
        var ranges = new List<(int From, int To)>();
        double dLat = Math.Max(0, radiusKm) / KmPerDegree;
        double south = lat - dLat, north = lat + dLat;
        bool allLongitudes = south <= -90.0 || north >= 90.0;
        south = Math.Max(-90.0, south);
        north = Math.Min(90.0, north);

        double dLon = 180.0;
        if (!allLongitudes)
        {
            // The widest longitude span inside the box is at the latitude farthest from the equator.
            double maxAbsLat = Math.Max(Math.Abs(south), Math.Abs(north));
            double cos = Math.Cos(maxAbsLat * Math.PI / 180.0);
            dLon = cos <= 1e-9 ? 180.0 : dLat / cos;
            if (dLon >= 180.0) allLongitudes = true;
        }

        int r0 = Row(south), r1 = Row(north);
        lon = NormalizeLon(lon);
        for (int r = r0; r <= r1; r++)
        {
            int rowBase = r * Cols;
            if (allLongitudes)
            {
                ranges.Add((rowBase, rowBase + Cols - 1));
                continue;
            }
            double west = lon - dLon, east = lon + dLon;
            if (west < -180.0)
            {
                ranges.Add((rowBase + Col(west + 360.0), rowBase + Cols - 1));
                ranges.Add((rowBase, rowBase + Col(east)));
            }
            else if (east >= 180.0)
            {
                ranges.Add((rowBase + Col(west), rowBase + Cols - 1));
                ranges.Add((rowBase, rowBase + Col(east - 360.0)));
            }
            else
            {
                ranges.Add((rowBase + Col(west), rowBase + Col(east)));
            }
        }
        return ranges;
    }

    /// <summary>Longitude folded into [-180, 180).</summary>
    internal static double NormalizeLon(double lon)
    {
        double x = (lon + 180.0) % 360.0;
        if (x < 0) x += 360.0;
        return x - 180.0;
    }
}
