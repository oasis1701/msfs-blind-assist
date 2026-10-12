using System.Text.Json;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The two KSAN captures behind the "only 4 stands after touchdown" fix, taken live on the ground
/// at KSAN on 2026-10-07: GSX's <c>handlerData.airport.parkings</c> trimmed to the fields
/// <c>GsxRemoteParkingReader</c> reads, and the same airport's fs2024 navdata parking rows.
/// 79 selectable GSX stands; GSX published a heading and a type number for only the 4 its
/// installed profile (LatinVFR's, written for a different scenery) covers.
/// <para>
/// They describe the KSAN scenery installed where they were captured, not stock KSAN: the stock
/// fs2024 navdata has 106 KSAN stands with different names and positions (measured 2026-10-09).
/// </para>
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
    /// FEET, Source Navdata, and <see cref="ParkingSpot.Name"/> mapped by the provider's own
    /// <c>MapParkingName</c> ("GN" -> "N", "EP" -> "East"), because the concourse-letter filler
    /// reads it.
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
                Name = LittleNavMapProvider.MapParkingName(StringOrEmpty(r, "name")),
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
