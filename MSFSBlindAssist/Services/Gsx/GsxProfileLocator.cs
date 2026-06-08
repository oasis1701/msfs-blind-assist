using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services.Gsx;

/// <summary>
/// Locates the GSX profile .ini for an ICAO under %APPDATA%\Virtuali\GSX\MSFS.
/// When multiple profiles match an ICAO, prefers a human-named (downloaded/RW)
/// profile over a GSX auto-cache hash, then the most-recently-modified.
/// </summary>
public sealed class GsxProfileLocator
{
    private readonly string _profileDir;

    public GsxProfileLocator(string? profileDir = null)
        => _profileDir = profileDir ?? DefaultProfileDir();

    public static string DefaultProfileDir()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Virtuali", "GSX", "MSFS");
    }

    public bool TryFindProfile(string icao, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(icao) || !Directory.Exists(_profileDir)) return false;

        // Windows file matching is case-insensitive, so "OMDB*.ini" matches "omdb-...ini".
        // Filter to stems that start with the ICAO (case-insensitive) AND satisfy at least
        // one of three rules:
        //   1. Stem equals the ICAO exactly (e.g. "EDDF.ini").
        //   2. The char immediately after the ICAO is '-' or '_' (separator variants:
        //      "omdb-24-iniBuilds.ini", "ebbr_aerosoft_v2.ini").
        //   3. Stem simply starts with the ICAO (covers bare-concatenation names like
        //      "rjttbasica7.ini"). GSX filenames are always ICAO-prefixed, so a stem
        //      that starts with a 4-letter ICAO cannot belong to a different airport
        //      (e.g. "OMDBX" would only match a query for ICAO "OMDBX", never "OMDB").
        var matches = Directory.GetFiles(_profileDir, $"{icao}*.ini")
            .Where(m =>
            {
                string stem = Path.GetFileNameWithoutExtension(m);
                if (!stem.StartsWith(icao, StringComparison.OrdinalIgnoreCase)) return false;
                if (stem.Length == icao.Length) return true;                   // rule 1: exact
                char sep = stem[icao.Length];
                if (sep == '-' || sep == '_') return true;                     // rule 2: separator
                return true;                                                   // rule 3: prefix fallback
            })
            .ToArray();
        if (matches.Length == 0) return false;

        var preferred = matches.Where(m => !LooksLikeCacheHash(icao, m)).ToList();
        var pool = preferred.Count > 0 ? preferred : matches.ToList();

        path = pool.OrderByDescending(File.GetLastWriteTimeUtc).First();
        return true;
    }

    // Cache names look like ICAO-<6..10 lowercase alnum> (e.g. KATL-ooh17umq.ini).
    // Downloaded profiles look like omdb-24-iniBuilds.ini / EDDF-Aerosoft.ini (extra
    // dashes or uppercase letters), or ebbr_aerosoft_v2.ini / rjttbasica7.ini, which
    // do not match this pattern.
    private static bool LooksLikeCacheHash(string icao, string fullPath)
    {
        string name = Path.GetFileNameWithoutExtension(fullPath);
        if (name.Length <= icao.Length) return false;
        string rest = name.Substring(icao.Length);
        return Regex.IsMatch(rest, "^-[a-z0-9]{6,10}$");
    }
}
