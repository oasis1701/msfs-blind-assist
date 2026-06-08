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
        var matches = Directory.GetFiles(_profileDir, $"{icao}*.ini");
        if (matches.Length == 0) return false;

        var preferred = matches.Where(m => !LooksLikeCacheHash(icao, m)).ToList();
        var pool = preferred.Count > 0 ? preferred : matches.ToList();

        path = pool.OrderByDescending(File.GetLastWriteTimeUtc).First();
        return true;
    }

    // Cache names look like ICAO-<6..10 lowercase alnum> (e.g. KATL-ooh17umq.ini).
    // Downloaded profiles look like omdb-24-iniBuilds.ini / EDDF-Aerosoft.ini (extra
    // dashes or uppercase letters), which do not match this pattern.
    private static bool LooksLikeCacheHash(string icao, string fullPath)
    {
        string name = Path.GetFileNameWithoutExtension(fullPath);
        if (name.Length <= icao.Length) return false;
        string rest = name.Substring(icao.Length);
        return Regex.IsMatch(rest, "^-[a-z0-9]{6,10}$");
    }
}
