using System.Linq;

namespace MSFSBlindAssist.Services.Gsx;

/// <summary>
/// Reads GSX per-aircraft gsx.cfg files (package folders + %APPDATA%\Virtuali\Airplanes)
/// and exposes the preferred passenger door's geometry (lateral side + longitudinal
/// position) and wingspan, keyed by ICAO type — nothing hardcoded. Docking consumes
/// ONLY the door SIDE (the spoken "jetway on your left/right" cue): the stop math
/// aligns the aircraft DATUM to the stop position, NOT the door — subtracting the
/// door's longitudinal offset parked a B777 ~26 m short (see DockingGuidanceManager).
/// The longitudinal value is still parsed for diagnostics/wingspan fallback only.
/// </summary>

public enum DoorSide { Unknown, Left, Right }

/// <summary>
/// Per-aircraft geometry derived from a GSX gsx.cfg profile.
/// DoorLongitudinalMetres: preferred exit's longitudinal (2nd) column value, forward-positive.
/// DoorLateralMetres: preferred exit's lateral (1st) column value, negative = left.
/// WingspanMetres: abs(wingtippos 1st column) * 2, or null when not present.
/// </summary>
public readonly record struct GsxAircraftGeometry(double DoorLongitudinalMetres, double DoorLateralMetres, double? WingspanMetres)
{
    public DoorSide Side => DoorLateralMetres < 0 ? DoorSide.Left : (DoorLateralMetres > 0 ? DoorSide.Right : DoorSide.Unknown);
}

public sealed class GsxAirplaneProfile
{
    // Bound the recursive gsx.cfg search below each package's SimObjects tree so a deep
    // texture/sound tree can never stall the scan. Real gsx.cfg files live a few levels down;
    // 6 is comfortable headroom (mirrors AircraftCfgCatalog.MaxScanDepth).
    private const int MaxScanDepth = 6;

    private readonly object _lock = new();

    // SINGLE-FLIGHT build: the map lives behind a Lazy with ExecutionAndPublication, so the
    // multi-second gsx.cfg scan runs EXACTLY ONCE even when two callers race (e.g. MainForm's
    // startup warm task and the first AircraftIcaoTypeDetected handler) — the loser BLOCKS on
    // and reuses the winner's result rather than launching a second concurrent scan. Refresh()
    // swaps in a fresh Lazy under the lock so a later rescan picks up new profiles.
    private Lazy<Dictionary<string, GsxAircraftGeometry>> _lazyMap;

    public GsxAirplaneProfile()
    {
        _lazyMap = NewLazyMap();
    }

    private Lazy<Dictionary<string, GsxAircraftGeometry>> NewLazyMap()
        => new Lazy<Dictionary<string, GsxAircraftGeometry>>(
            BuildMap, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

    private Dictionary<string, GsxAircraftGeometry> Map
    {
        get { Lazy<Dictionary<string, GsxAircraftGeometry>> lazy; lock (_lock) { lazy = _lazyMap; } return lazy.Value; }
    }

    /// <summary>Door longitudinal offset (metres, forward-positive) for the given ICAO type, or null if unknown.</summary>
    public double? GetDoorOffsetMetres(string? icaoType)
    {
        if (string.IsNullOrWhiteSpace(icaoType)) return null;
        return Map.TryGetValue(icaoType.Trim().ToUpperInvariant(), out var v) ? v.DoorLongitudinalMetres : (double?)null;
    }

    /// <summary>Full geometry struct for the given ICAO type, or null if unknown.</summary>
    public GsxAircraftGeometry? GetGeometry(string? icaoType)
    {
        if (string.IsNullOrWhiteSpace(icaoType)) return null;
        return Map.TryGetValue(icaoType.Trim().ToUpperInvariant(), out var v) ? v : (GsxAircraftGeometry?)null;
    }

    /// <summary>Force a rescan (e.g. after a new aircraft profile is created).</summary>
    public void Refresh()
    {
        Lazy<Dictionary<string, GsxAircraftGeometry>> fresh = NewLazyMap();
        lock (_lock) { _lazyMap = fresh; }
        _ = fresh.Value; // trigger the single rebuild now (still single-flight via the Lazy)
    }

    /// <summary>Build the ICAO->geometry map by scanning all known gsx.cfg locations. Public for the probe.</summary>
    public Dictionary<string, GsxAircraftGeometry> BuildMap()
    {
        var map = new Dictionary<string, GsxAircraftGeometry>(StringComparer.OrdinalIgnoreCase);
        foreach (var cfg in EnumerateGsxCfgFiles())
        {
            try
            {
                var (icao, geom) = ParseGeometry(System.IO.File.ReadAllLines(cfg));
                if (!string.IsNullOrWhiteSpace(icao) && geom.HasValue)
                {
                    var key = icao.Trim().ToUpperInvariant();
                    if (!map.ContainsKey(key)) map[key] = geom.Value; // first found wins
                }
            }
            catch { /* skip unreadable/garbage cfg */ }
        }
        return map;
    }

    // Parse icaotype + the preferred exit's lateral (1st) and longitudinal (2nd) values,
    // plus the [aircraft] wingtippos 1st column for wingspan. Returns full geometry.
    // PUBLIC static so the probe can unit-check parsing on literal text.
    public static (string? icao, GsxAircraftGeometry? geom) ParseGeometry(IReadOnlyList<string> lines)
    {
        string? icao = null; int preferred = 0; bool haveAircraft = false;
        double? wingspan = null;
        string curSection = "";
        // collect exit sections: name -> (lateral, longitudinal)
        var exitByName = new Dictionary<string, (double lat, double lon)>(StringComparer.OrdinalIgnoreCase);
        var firstExitName = (string?)null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("[") && line.EndsWith("]")) { curSection = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant(); if (curSection == "aircraft") haveAircraft = true; continue; }
            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            string key = line.Substring(0, eq).Trim().ToLowerInvariant();
            string val = line.Substring(eq + 1).Trim();
            if (curSection == "aircraft")
            {
                if (key == "icaotype") icao = val;
                else if (key == "preferredexit") int.TryParse(val.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), out preferred);
                else if (key == "wingtippos")
                {
                    var parts = val.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 1 && double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var wt))
                        wingspan = Math.Abs(wt) * 2.0;
                }
            }
            else if (curSection.StartsWith("exit") && key == "pos")
            {
                var parts = val.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2
                    && double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat)
                    && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lon))
                {
                    if (!exitByName.ContainsKey(curSection)) exitByName[curSection] = (lat, lon);
                    firstExitName ??= curSection;
                }
            }
        }
        if (!haveAircraft) return (null, null);
        // choose preferred exit section
        string? chosen = preferred >= 1 ? $"exit{preferred}" : firstExitName;
        if (chosen != null && exitByName.TryGetValue(chosen, out var pos))
            return (icao, new GsxAircraftGeometry(pos.lon, pos.lat, wingspan));
        if (firstExitName != null && exitByName.TryGetValue(firstExitName, out var pos2))
            return (icao, new GsxAircraftGeometry(pos2.lon, pos2.lat, wingspan));
        return (icao, null);
    }

    /// <summary>
    /// Parse icaotype + preferred exit's longitudinal offset only.
    /// Kept for backward compatibility with the probe's unit-check.
    /// Delegates to ParseGeometry.
    /// </summary>
    public static (string? icao, double? offsetMetres) ParseDoorOffset(IReadOnlyList<string> lines)
    {
        var (icao, geom) = ParseGeometry(lines);
        return (icao, geom?.DoorLongitudinalMetres);
    }

    private static IEnumerable<string> EnumerateGsxCfgFiles()
    {
        // 1) %APPDATA%\Virtuali\Airplanes\*\gsx.cfg
        string appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string virt = System.IO.Path.Combine(appdata, "Virtuali", "Airplanes");
        if (System.IO.Directory.Exists(virt))
            foreach (var d in SafeDirs(virt))
                foreach (var f in SafeFiles(d, "gsx.cfg"))
                    yield return f;
        // 2) MSFS package folders
        string? pkgRoot = FindInstalledPackagesPath();
        if (pkgRoot != null)
        {
            var roots = new List<string>();
            string community = System.IO.Path.Combine(pkgRoot, "Community");
            if (System.IO.Directory.Exists(community)) roots.Add(community);
            string official = System.IO.Path.Combine(pkgRoot, "Official");
            if (System.IO.Directory.Exists(official)) roots.AddRange(SafeDirs(official)); // Official\OneStore, Official\Steam, ...
            foreach (var root in roots)
                foreach (var pkg in SafeDirs(root))
                {
                    string simobj = System.IO.Path.Combine(pkg, "SimObjects");
                    if (!System.IO.Directory.Exists(simobj)) continue; // only aircraft packages have SimObjects
                    // Depth-bounded walk (NOT narrowed to Airplanes — gsx.cfg locations vary).
                    // Mirrors AircraftCfgCatalog's MaxScanDepth approach so texture/sound trees are
                    // never crawled: real gsx.cfg files sit a few levels under SimObjects, so the
                    // bound caps a deep texture tree from stalling the scan.
                    foreach (var f in EnumerateCfgBounded(simobj, "gsx.cfg", 0))
                        yield return f;
                }
        }
    }

    private static string? FindInstalledPackagesPath()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string[] candidates = {
            System.IO.Path.Combine(local, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt"),
            System.IO.Path.Combine(appdata, "Microsoft Flight Simulator 2024", "UserCfg.opt"),
            System.IO.Path.Combine(local, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt"),
            System.IO.Path.Combine(appdata, "Microsoft Flight Simulator", "UserCfg.opt"),
        };
        foreach (var c in candidates)
        {
            try
            {
                if (!System.IO.File.Exists(c)) continue;
                foreach (var l in System.IO.File.ReadAllLines(c))
                {
                    var t = l.Trim();
                    // Match the ACTIVE packages path only. "InstalledPackagesPathNextBoot" also
                    // StartsWith "InstalledPackagesPath" but points at a NOT-YET-ACTIVE location
                    // (set after a user relocates packages) — resolving it would scan the wrong
                    // tree. Mirror AircraftCfgCatalog's two-check pattern: accept the prefix, then
                    // explicitly reject the NextBoot variant.
                    if (!t.StartsWith("InstalledPackagesPath", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (t.StartsWith("InstalledPackagesPathNextBoot", StringComparison.OrdinalIgnoreCase))
                        continue;
                    int q1 = t.IndexOf('"'); int q2 = t.LastIndexOf('"');
                    if (q2 > q1 && q1 >= 0) { var p = t.Substring(q1 + 1, q2 - q1 - 1); if (System.IO.Directory.Exists(p)) return p; }
                }
            }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// Depth-bounded recursive search for files named <paramref name="pattern"/> under
    /// <paramref name="dir"/>. Caps recursion at <see cref="MaxScanDepth"/> so a deeply
    /// nested texture tree can't stall the scan. Never throws (per-directory access is
    /// wrapped in the Safe* helpers).
    /// </summary>
    private static IEnumerable<string> EnumerateCfgBounded(string dir, string pattern, int depth)
    {
        foreach (var f in SafeFiles(dir, pattern))
            yield return f;

        if (depth >= MaxScanDepth) yield break;

        foreach (var sub in SafeDirs(dir))
            foreach (var f in EnumerateCfgBounded(sub, pattern, depth + 1))
                yield return f;
    }

    private static IEnumerable<string> SafeDirs(string path)
    { string[] r; try { r = System.IO.Directory.GetDirectories(path); } catch { return Array.Empty<string>(); } return r; }
    private static IEnumerable<string> SafeFiles(string path, string pattern)
    { string[] r; try { r = System.IO.Directory.GetFiles(path, pattern); } catch { return Array.Empty<string>(); } return r; }
}
