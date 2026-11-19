// KJFK runway 13L/31R, fs2024 navdata (real coordinates, navdata names only): every taxi path within 300 m of the
// strip's centreline and 200 m past its ends, which reproduces the whole airport's 13L and 31R exit lists exactly.
// The strip carries ONE hold-short node in its corridor, on V: it turns off forward for 13L and is a turnaround
// for 31R.

using System.Text.Json;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

internal static class KjfkRunway13LFixture
{
    private static readonly Lazy<(List<TaxiPath> Paths, List<Runway> Runways, List<StartPosition> Starts)> Data = new(Load);

    public static Runway Runway13L() => Data.Value.Runways.Single(r => r.RunwayID == "13L");
    public static Runway Runway31R() => Data.Value.Runways.Single(r => r.RunwayID == "31R");

    public static TaxiGraph BuildGraph()
        => TaxiGraph.Build(Data.Value.Paths, new List<ParkingSpot>(), Data.Value.Starts, Data.Value.Runways);

    // Paths are [type, widthFt, name, startType, startLat, startLon, endType, endLat, endLon], with the coordinates
    // as the single-precision floats navdatareader stores: read as float, they widen to the exact doubles the
    // database provider returns.
    private static (List<TaxiPath>, List<Runway>, List<StartPosition>) Load()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "kjfk-13l-31r-navdata.json")));
        var root = doc.RootElement;

        var runways = root.GetProperty("runways").EnumerateArray().Select(r => new Runway
        {
            RunwayID = r.GetProperty("RunwayID").GetString()!,
            StartLat = r.GetProperty("StartLat").GetDouble(), StartLon = r.GetProperty("StartLon").GetDouble(),
            EndLat = r.GetProperty("EndLat").GetDouble(), EndLon = r.GetProperty("EndLon").GetDouble(),
            Heading = r.GetProperty("Heading").GetDouble(), Length = r.GetProperty("Length").GetDouble(),
            Width = r.GetProperty("Width").GetDouble(), ThresholdOffset = r.GetProperty("ThresholdOffset").GetDouble(),
        }).ToList();

        var starts = root.GetProperty("starts").EnumerateArray().Select(s => new StartPosition
        {
            RunwayName = s.GetProperty("RunwayName").GetString()!, Type = s.GetProperty("Type").GetString()!,
            Heading = s.GetProperty("Heading").GetDouble(),
            Latitude = s.GetProperty("Latitude").GetDouble(), Longitude = s.GetProperty("Longitude").GetDouble(),
        }).ToList();

        var paths = root.GetProperty("paths").EnumerateArray().Select(p => new TaxiPath
        {
            Type = p[0].GetString()!, Width = p[1].GetSingle(), Name = p[2].GetString()!,
            StartType = p[3].GetString()!, StartLat = p[4].GetSingle(), StartLon = p[5].GetSingle(),
            EndType = p[6].GetString()!, EndLat = p[7].GetSingle(), EndLon = p[8].GetSingle(),
        }).ToList();

        return (paths, runways, starts);
    }
}
