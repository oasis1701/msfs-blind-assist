using System.Text.Json;
using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The two KSAN captures behind the "only 4 stands after touchdown" fix, taken live on the ground
/// at KSAN on 2026-10-07: GSX's <c>handlerData.airport.parkings</c> trimmed to the fields
/// <c>GsxRemoteParkingReader</c> reads, and the same airport's fs2024 navdata parking rows.
/// 79 selectable GSX stands; GSX published a heading and a type number for only the 4 its
/// installed profile (LatinVFR's, written for a different scenery) covers.
/// </summary>
internal static class GsxKsanFixtures
{
    public const string Ksan = "KSAN";

    /// <summary>The <c>handlerData.airport</c> object, the granularity the reader takes.</summary>
    public static JsonElement GsxAirport()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gsx-handlerdata-parkings-ksan.json"));
        return JsonDocument.Parse(json).RootElement.GetProperty("airport").Clone();
    }

    /// <summary>
    /// KSAN's navdata stands, shaped the way <c>LittleNavMapProvider</c> builds them: Radius in
    /// FEET, Source Navdata. <see cref="ParkingSpot.Name"/> is the raw navdata name column (null
    /// becomes ""), not the provider's mapped letter: nothing these tests check reads it.
    /// </summary>
    public static List<ParkingSpot> Navdata()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "navdata-parking-ksan.json"));
        using var doc = JsonDocument.Parse(json);
        var spots = new List<ParkingSpot>();
        foreach (var r in doc.RootElement.GetProperty("parking").EnumerateArray())
        {
            spots.Add(new ParkingSpot
            {
                AirportICAO = Ksan,
                Name = StringOrEmpty(r, "name"),
                Number = r.GetProperty("number").GetInt32(),
                Suffix = StringOrEmpty(r, "suffix"),
                Heading = r.GetProperty("heading").GetDouble(),
                Latitude = r.GetProperty("laty").GetDouble(),
                Longitude = r.GetProperty("lonx").GetDouble(),
                Radius = r.GetProperty("radius").GetDouble(),
                HasJetway = r.GetProperty("has_jetway").GetInt32() == 1,
                Source = GateSource.Navdata,
            });
        }
        return spots;
    }

    private static string StringOrEmpty(JsonElement row, string name)
        => row.GetProperty(name).ValueKind == JsonValueKind.String ? row.GetProperty(name).GetString()! : string.Empty;
}
