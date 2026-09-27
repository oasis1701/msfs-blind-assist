// KMEM runway 36L, fs2024 navdata (real coordinates), as the landing of 2026-09-26 saw it after the
// online taxiway-name augmentation: M5's 36L arm named, M6's 18R arm named (its 36L arm unnamed),
// M7 named from its centerline junction, only the last segment of M8's 36L arm named.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

internal static class KmemRunway36LFixture
{
    public const double RunwayHeadingTrue = 359.0028076171875;

    public static Runway Runway36L() => new()
    {
        RunwayID = "36L",
        StartLat = 35.023929595947266, StartLon = -89.98689270019531,
        EndLat = 35.04945755004883, EndLon = -89.98744201660156,
        Heading = RunwayHeadingTrue, Length = 9310.0, Width = 164.0, ThresholdOffset = 0.0,
    };

    public static Runway Runway18R() => new()
    {
        RunwayID = "18R",
        StartLat = 35.04945755004883, StartLon = -89.98744201660156,
        EndLat = 35.023929595947266, EndLon = -89.98689270019531,
        Heading = 179.0028076171875, Length = 9310.0, Width = 164.0, ThresholdOffset = 0.0,
    };

    public static List<StartPosition> Starts() => new()
    {
        new StartPosition { RunwayName = "18R", Type = "R", Heading = 179.0028076171875, Latitude = 35.047142028808594, Longitude = -89.98738861083984 },
        new StartPosition { RunwayName = "36L", Type = "R", Heading = 359.0028076171875, Latitude = 35.02582931518555, Longitude = -89.98693084716797 },
    };

    private static TaxiPath P(double sLat, double sLon, double eLat, double eLon, string name, string sType, string eType, double widthFt)
        => new() { Type = "T", StartLat = sLat, StartLon = sLon, EndLat = eLat, EndLon = eLon, Name = name, StartType = sType, EndType = eType, Width = widthFt };

    public static List<TaxiPath> Paths() => new()
    {
        // M5 36L branch (augmentation-named)
        P(35.0360069, -89.9871826, 35.0361710, -89.9871368, "M5", "N", "N", 164),
        P(35.0363655, -89.9870224, 35.0361710, -89.9871368, "M5", "N", "N", 98),
        P(35.0365257, -89.9868240, 35.0363655, -89.9870224, "M5", "N", "N", 98),
        P(35.0365906, -89.9865799, 35.0365257, -89.9868240, "M5", "N", "N", 98),
        P(35.0365944, -89.9862442, 35.0365906, -89.9865799, "M5", "HSND", "N", 98),
        // M5 18R branch (unnamed)
        P(35.0365906, -89.9865799, 35.0366478, -89.9868164, "", "N", "N", 98),
        P(35.0366478, -89.9868164, 35.0367661, -89.9870071, "", "N", "N", 98),
        P(35.0367661, -89.9870071, 35.0369415, -89.9871445, "", "N", "N", 98),
        P(35.0371780, -89.9872055, 35.0369415, -89.9871445, "", "N", "N", 164),
        // M6 36L branch (unnamed in navdata and augmentation)
        P(35.0416336, -89.9871368, 35.0411072, -89.9872894, "", "N", "N", 98),
        P(35.0417938, -89.9869156, 35.0416336, -89.9871368, "", "N", "N", 98),
        P(35.0418549, -89.9866867, 35.0417938, -89.9869156, "", "N", "N", 98),
        // M6 18R branch (augmentation-named M6)
        P(35.0418549, -89.9866867, 35.0419159, -89.9869308, "M6", "N", "N", 98),
        P(35.0419159, -89.9869308, 35.0420189, -89.9870987, "M6", "N", "N", 98),
        P(35.0420189, -89.9870987, 35.0422211, -89.9872589, "M6", "N", "N", 98),
        P(35.0422211, -89.9872589, 35.0425072, -89.9873123, "M6", "N", "N", 98),
        P(35.0418663, -89.9863434, 35.0418549, -89.9866867, "M6", "HSND", "N", 98),
        // M7 high-speed exit
        P(35.0440407, -89.9873505, 35.0442581, -89.9872818, "M7", "N", "N", 164),
        P(35.0445824, -89.9872360, 35.0442581, -89.9872818, "M7", "N", "N", 98),
        P(35.0448570, -89.9871750, 35.0445824, -89.9872360, "M7", "N", "N", 98),
        P(35.0451965, -89.9870682, 35.0448570, -89.9871750, "M7", "N", "N", 98),
        P(35.0454102, -89.9869919, 35.0451965, -89.9870682, "M7", "N", "N", 98),
        P(35.0456619, -89.9868698, 35.0454102, -89.9869919, "M7", "N", "N", 98),
        P(35.0458870, -89.9867401, 35.0456619, -89.9868698, "M7", "N", "N", 98),
        P(35.0460968, -89.9865952, 35.0458870, -89.9867401, "M7", "N", "N", 98),
        P(35.0462532, -89.9864502, 35.0460968, -89.9865952, "M7", "HSND", "N", 98),
        // M8 36L branch (only the last segment named)
        P(35.0476952, -89.9874268, 35.0478935, -89.9873505, "", "N", "N", 164),
        P(35.0480576, -89.9872131, 35.0478935, -89.9873505, "", "N", "N", 98),
        P(35.0481758, -89.9870224, 35.0480576, -89.9872131, "", "N", "N", 98),
        P(35.0482292, -89.9868393, 35.0481758, -89.9870224, "M8", "N", "N", 98),
        P(35.0482292, -89.9864807, 35.0482292, -89.9868393, "M8", "HSND", "N", 98),
        // M8 18R branch (unnamed)
        P(35.0484123, -89.9872589, 35.0482292, -89.9868393, "", "N", "N", 98),
        P(35.0488167, -89.9874420, 35.0484123, -89.9872589, "", "N", "N", 98),
    };

    public static TaxiGraph BuildGraph()
        => TaxiGraph.Build(Paths(), new List<ParkingSpot>(), Starts(), new[] { Runway36L(), Runway18R() });

    /// <summary>A point <paramref name="alongFeet"/> down 36L from its threshold, <paramref name="lateralMetres"/> right of the centerline.</summary>
    public static (double Lat, double Lon) PointAt(double alongFeet, double lateralMetres)
    {
        const double M_PER_DEG = 111132.0;
        var rwy = Runway36L();
        double h = rwy.Heading * Math.PI / 180.0;
        double along = alongFeet * 0.3048;
        double dN = along * Math.Cos(h) - lateralMetres * Math.Sin(h);
        double dE = along * Math.Sin(h) + lateralMetres * Math.Cos(h);
        double lat = rwy.StartLat + dN / M_PER_DEG;
        double lon = rwy.StartLon + dE / (M_PER_DEG * Math.Cos((rwy.StartLat + lat) * 0.5 * Math.PI / 180.0));
        return (lat, lon);
    }
}
