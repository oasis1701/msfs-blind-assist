namespace MSFSBlindAssist.Services.Geography;

/// <summary>
/// The small GeoNames name files the database needs beside the dump:
/// <c>countryInfo.txt</c> (ISO code → country name, column 4; comment lines start with '#'),
/// <c>admin1CodesASCII.txt</c> ("US.MD" → "Maryland") and <c>featureCodes_en.txt</c>
/// ("T.ISL" → "island").
/// </summary>
internal static class GeoNamesNameFiles
{
    internal static Dictionary<string, string> ParseCountryInfo(IEnumerable<string> lines)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var f = line.Split('\t');
            if (f.Length > 4 && f[0].Length == 2 && f[4].Trim().Length > 0)
                names[f[0]] = f[4].Trim();
        }
        return names;
    }

    /// <summary>Keyed "CC.code", as the file writes it.</summary>
    internal static Dictionary<string, string> ParseAdmin1(IEnumerable<string> lines)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var f = line.Split('\t');
            if (f.Length < 2 || f[0].IndexOf('.') <= 0) continue;
            string name = f[1].Trim();
            if (name.Length == 0 && f.Length > 2) name = f[2].Trim();
            if (name.Length > 0) names[f[0]] = name;
        }
        return names;
    }

    internal static string Admin1Key(string countryCode, string admin1Code) => countryCode + "." + admin1Code;

    /// <summary>
    /// <c>featureCodes_en.txt</c>: "T.ISL", tab, "island", tab, a description. Keyed by the code
    /// after the class letter ("ISL"), as the dump's feature-code column writes it. The name is
    /// what the Location Info window shows as a place's type. "null" (the file's placeholder row)
    /// and empty names are skipped; the first row for a code wins.
    /// </summary>
    internal static Dictionary<string, string> ParseFeatureCodes(IEnumerable<string> lines)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var f = line.Split('\t');
            if (f.Length < 2) continue;
            int dot = f[0].IndexOf('.');
            if (dot <= 0 || dot == f[0].Length - 1) continue;
            string name = f[1].Trim();
            if (name.Length > 0) names.TryAdd(f[0][(dot + 1)..].Trim(), name);
        }
        return names;
    }
}
