using System.Globalization;

namespace MSFSBlindAssist.Services.Geography;

/// <summary>One kept row of GeoNames' <c>allCountries.txt</c>.</summary>
internal readonly record struct GeoNamesRow(
    int Id, string Name, double Latitude, double Longitude,
    LocationCategory Category, string FeatureCode, string CountryCode, string Admin1, int Population);

/// <summary>
/// Parses one tab-separated line of GeoNames' dump (columns: 0 geonameid, 1 name, 2 asciiname,
/// 3 alternatenames, 4 latitude, 5 longitude, 6 feature class, 7 feature code, 8 country code,
/// 9 cc2, 10 admin1 code, 11-13 admin2-4, 14 population, 15-18 elevation, dem, timezone, date).
/// The feature class and code are checked first, on spans, so the ~5 million rows the database
/// does not keep are rejected without allocating a string. Numbers always parse with the
/// invariant culture: the dump writes "51.5" whatever the pilot's Windows region says.
/// </summary>
internal static class GeoNamesDumpLine
{
    private const int FieldsNeeded = 15; // through population

    internal static bool TryParse(ReadOnlySpan<char> line, out GeoNamesRow row)
    {
        row = default;
        Span<int> starts = stackalloc int[FieldsNeeded + 1];
        int field = 0;
        starts[0] = 0;
        for (int i = 0; i < line.Length && field < FieldsNeeded; i++)
        {
            if (line[i] == '\t')
            {
                field++;
                starts[field] = i + 1;
            }
        }
        if (field < FieldsNeeded - 1) return false; // fewer than 15 columns
        if (field == FieldsNeeded - 1) starts[FieldsNeeded] = line.Length + 1; // population is the last column present

        if (!LocationFeatureCatalog.TryClassify(Field(line, starts, 6), Field(line, starts, 7), out var category, out bool populated))
            return false;

        if (!int.TryParse(Field(line, starts, 0), NumberStyles.None, CultureInfo.InvariantCulture, out int id)) return false;
        if (!double.TryParse(Field(line, starts, 4), NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)
            || lat < -90 || lat > 90) return false;
        if (!double.TryParse(Field(line, starts, 5), NumberStyles.Float, CultureInfo.InvariantCulture, out double lon)
            || lon < -180 || lon > 180) return false;

        long population = 0;
        var popField = Field(line, starts, 14);
        if (!popField.IsEmpty && !long.TryParse(popField, NumberStyles.None, CultureInfo.InvariantCulture, out population))
            population = 0;

        var code = Field(line, starts, 7);
        if (populated) category = LocationFeatureCatalog.PopulatedCategory(code, population);

        var name = Field(line, starts, 1);
        if (name.IsWhiteSpace()) name = Field(line, starts, 2);
        if (name.IsWhiteSpace()) return false;

        row = new GeoNamesRow(
            id, name.Trim().ToString(), lat, lon, category, code.ToString(),
            Field(line, starts, 8).ToString(), Field(line, starts, 10).ToString(),
            (int)Math.Min(population, int.MaxValue));
        return true;
    }

    /// <summary>Field <paramref name="n"/>: from its start to the tab before field n+1.</summary>
    private static ReadOnlySpan<char> Field(ReadOnlySpan<char> line, ReadOnlySpan<int> starts, int n) =>
        line.Slice(starts[n], Math.Max(0, starts[n + 1] - 1 - starts[n]));
}
