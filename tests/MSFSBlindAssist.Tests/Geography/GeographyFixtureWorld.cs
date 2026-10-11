using System.IO.Compression;
using System.Text;
using MSFSBlindAssist.Services.Geography;

namespace MSFSBlindAssist.Tests.Geography;

/// <summary>
/// A tiny world for build and download tests: a bay east of a Maryland shoreline, a lake, a
/// mountain range and a few places, as GeoNames and Natural Earth would publish them.
/// </summary>
internal static class GeographyFixtureWorld
{
    private static string Row(int id, string name, double lat, double lon, string cls, string code, string a1, int pop) =>
        FormattableString.Invariant($"{id}\t{name}\t{name}\t\t{lat}\t{lon}\t{cls}\t{code}\tUS\t\t{a1}\t\t\t\t{pop}\t\t\tAmerica/New_York\t2026-01-01");

    internal static readonly string[] Rows =
    {
        Row(1, "Bayside", 38.55, -76.48, "P", "PPL", "MD", 1238),
        Row(2, "Hamlet", 38.60, -76.70, "P", "PPL", "MD", 0),
        Row(3, "Bigtown", 39.29, -76.61, "P", "PPLA2", "MD", 600000),
        Row(4, "Bayside Airport", 38.40, -76.60, "S", "AIRP", "MD", 0),
        Row(5, "Pond", 38.70, -76.90, "H", "LK", "MD", 0),
        Row(6, "Sugarloaf Mountain", 38.90, -77.00, "T", "MT", "MD", 0),
        Row(7, "Bay Museum", 38.50, -76.50, "S", "MUS", "MD", 0),
        Row(8, "Some School", 38.51, -76.51, "S", "SCH", "MD", 0),
    };

    /// <summary>Places the catalog keeps out of <see cref="Rows"/> (the school is dropped).</summary>
    internal const int KeptPlaces = 7;

    internal const string CountryInfo = "#ISO\tISO3\tNum\tfips\tCountry\nUS\tUSA\t840\tUS\tUnited States\n";
    internal const string Admin1 = "US.MD\tMaryland\tMaryland\t4361885\n";

    internal static byte[] DumpZip()
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(archive.CreateEntry("allCountries.txt").Open(), new UTF8Encoding(false)))
            foreach (var row in Rows) writer.Write(row + "\n");
        return ms.ToArray();
    }

    private static string Collection(params string[] features) =>
        "{\"type\":\"FeatureCollection\",\"features\":[" + string.Join(",", features) + "]}";

    private static string Box(string props, double s, double w, double n, double e) => FormattableString.Invariant(
        $"{{\"type\":\"Feature\",\"properties\":{props},\"geometry\":{{\"type\":\"Polygon\",\"coordinates\":[[[{w},{s}],[{e},{s}],[{e},{n}],[{w},{n}],[{w},{s}]]]}}}}");

    internal static readonly IReadOnlyDictionary<NaturalEarthLayer, string> Shapes = new Dictionary<NaturalEarthLayer, string>
    {
        [NaturalEarthLayer.Marine] = Collection(Box("{\"name\":\"Test Bay\"}", 38.3, -76.6, 38.8, -76.2)),
        [NaturalEarthLayer.Coastline] = Collection(
            "{\"type\":\"Feature\",\"properties\":{},\"geometry\":{\"type\":\"LineString\",\"coordinates\":[[-76.6,38.3],[-76.6,38.8]]}}"),
        [NaturalEarthLayer.Lake] = Collection(Box("{\"name\":\"Test Lake\"}", 43, -87.5, 44, -86.5)),
        [NaturalEarthLayer.Region] = Collection(
            Box("{\"NAME\":\"TEST RANGE\",\"FEATURECLA\":\"Range/mtn\"}", 46, 9, 47, 11),
            Box("{\"NAME\":\"EUROPE\",\"FEATURECLA\":\"Continent\"}", 35, -10, 70, 40)),
        // Natural Earth spells it its own way; GeoNames' spelling must win (gn_a1_code US.MD).
        [NaturalEarthLayer.State] = Collection(
            Box("{\"name\":\"State of Maryland\",\"admin\":\"United States of America\",\"iso_a2\":\"US\",\"gn_a1_code\":\"US.MD\"}", 38.3, -77.5, 39.5, -76.6)),
    };

    /// <summary>Writes every input file into <paramref name="dir"/> for a direct build.</summary>
    internal static LocationBuildInputs WriteInputs(string dir, int minimumRows = 1)
    {
        string zip = Path.Combine(dir, "allCountries.zip");
        File.WriteAllBytes(zip, DumpZip());
        File.WriteAllText(Path.Combine(dir, "countryInfo.txt"), CountryInfo);
        File.WriteAllText(Path.Combine(dir, "admin1.txt"), Admin1);
        var files = Shapes.ToDictionary(kv => kv.Key, kv =>
        {
            string path = Path.Combine(dir, kv.Key + ".geojson");
            File.WriteAllText(path, kv.Value);
            return path;
        });
        return new LocationBuildInputs(zip, Path.Combine(dir, "countryInfo.txt"), Path.Combine(dir, "admin1.txt"), files, "2026-10-10")
        {
            MinimumPlaces = 1,
            MinimumRows = minimumRows,
        };
    }
}
