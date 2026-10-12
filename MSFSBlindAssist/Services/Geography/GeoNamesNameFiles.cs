namespace MSFSBlindAssist.Services.Geography;

/// <summary>
/// The two small GeoNames name files the database needs beside the dump:
/// <c>countryInfo.txt</c> (ISO code → country name, column 4; comment lines start with '#') and
/// <c>admin1CodesASCII.txt</c> ("US.MD" → "Maryland").
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
}
