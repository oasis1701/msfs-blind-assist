using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace MSFSBlindAssist.Services
{
    /// <summary>
    /// Universal, dependency-light catalog mapping a loaded aircraft's TITLE (the
    /// <c>[FLTSIM.N] title</c> value MSFS reports via the TITLE simvar) to its ICAO type
    /// designator (the package-level <c>icao_type_designator</c> in the same aircraft.cfg).
    ///
    /// <para>
    /// This is the runtime fallback for the rare add-on whose ATC MODEL simvar doesn't
    /// resolve to a clean ICAO (so <c>ExtractIcaoFromAtcModel</c> returns empty). Every
    /// installed aircraft.cfg carries both <c>icao_type_designator = &lt;ICAO&gt;</c> and a
    /// set of <c>[FLTSIM.N]</c> blocks each with a <c>title = "..."</c>, so scanning them
    /// lets us recover the ICAO from the title that the sim DID give us.
    /// </para>
    ///
    /// <para>
    /// Design contract: PURE / dependency-light (file IO + regex only — NO WinForms,
    /// SimConnect, or EFBModPackageManager dependency) so the probe can link this file
    /// directly. Resolves the FS2024/FS2020 packages root itself by reading
    /// <c>InstalledPackagesPath</c> from the known <c>UserCfg.opt</c> locations. The scan is
    /// background + lazy + cached + thread-safe and NEVER throws — any failure (missing
    /// directory, locked file, malformed cfg) degrades to an empty / no-hit result.
    /// </para>
    /// </summary>
    public sealed class AircraftCfgCatalog
    {
        // Only descend a bounded number of directory levels below SimObjects\Airplanes so a
        // texture/sound tree (or a maliciously deep folder) can never stall the scan. Real
        // aircraft.cfg files live at most a few levels down (e.g. PMDG common\config\aircraft.cfg,
        // Fenix attachments\...\config\aircraft.cfg) — 6 is comfortable headroom.
        private const int MaxScanDepth = 6;

        private readonly object _lock = new();
        private Dictionary<string, string>? _byTitle; // titleLower -> icaoUpper (null until built)
        private volatile bool _buildStarted;
        private volatile bool _isReady;
        private Thread? _buildThread;

        /// <summary>True once the background scan has completed (whether or not it found anything).</summary>
        public bool IsReady => _isReady;

        /// <summary>
        /// Kick off the background scan if it hasn't already started. Idempotent and cheap to
        /// call repeatedly (e.g. from a SimConnect callback) — only the first call spawns work.
        /// Never blocks the caller.
        /// </summary>
        public void BeginBuild()
        {
            lock (_lock)
            {
                if (_buildStarted) return;
                _buildStarted = true;
                _buildThread = new Thread(BuildSafely)
                {
                    IsBackground = true,
                    Name = "AircraftCfgCatalog.Build",
                };
            }
            // Start outside the lock so a slow OS thread-create can't hold other callers.
            _buildThread!.Start();
        }

        /// <summary>
        /// Looks up the ICAO for a loaded aircraft TITLE. Safe to call before the build
        /// completes (returns false until ready). Never throws.
        /// </summary>
        public bool TryGetIcaoByTitle(string? title, out string icao)
        {
            icao = string.Empty;
            if (string.IsNullOrWhiteSpace(title)) return false;
            string key = title.Trim().ToLowerInvariant();

            Dictionary<string, string>? map;
            lock (_lock) { map = _byTitle; }
            if (map == null) return false;

            if (map.TryGetValue(key, out var hit))
            {
                icao = hit;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Returns every (Title, Icao) pair discovered on this machine. Forces the build and
        /// WAITS for it to finish (for the probe / diagnostics). Returns empty on any failure.
        /// </summary>
        public IReadOnlyList<(string Title, string Icao)> EnumerateInstalled()
        {
            BeginBuild();

            // Wait for the background build to complete. Bounded so a pathological scan can't
            // hang the probe forever; in practice the scan completes in well under a second.
            Thread? t;
            lock (_lock) { t = _buildThread; }
            try { t?.Join(TimeSpan.FromSeconds(60)); }
            catch { /* never propagate */ }

            Dictionary<string, string>? map;
            lock (_lock) { map = _byTitle; }
            if (map == null) return Array.Empty<(string, string)>();

            var list = new List<(string, string)>(map.Count);
            foreach (var kv in map) list.Add((kv.Key, kv.Value));
            return list;
        }

        // --- background build ----------------------------------------------------------

        private void BuildSafely()
        {
            Dictionary<string, string> map;
            try { map = BuildMap(); }
            catch { map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }

            lock (_lock) { _byTitle = map; }
            _isReady = true;
        }

        /// <summary>
        /// Scans all installed aircraft.cfg files and builds the title->ICAO map. Pure (no
        /// instance state) so the probe can also call it directly; never throws.
        /// </summary>
        public static Dictionary<string, string> BuildMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var cfg in EnumerateAircraftCfgFiles())
                {
                    try
                    {
                        string[] lines = File.ReadAllLines(cfg);
                        var (icao, titles) = Parse(lines);
                        if (string.IsNullOrWhiteSpace(icao) || titles.Count == 0) continue;
                        string icaoUpper = icao.Trim().ToUpperInvariant();
                        foreach (var title in titles)
                        {
                            string key = title.Trim().ToLowerInvariant();
                            if (key.Length == 0) continue;
                            // First found wins (stable across rebuilds; ties are extremely rare).
                            if (!map.ContainsKey(key)) map[key] = icaoUpper;
                        }
                    }
                    catch { /* skip unreadable / locked / malformed cfg */ }
                }
            }
            catch { /* swallow — return whatever we gathered */ }
            return map;
        }

        /// <summary>
        /// Parses a single aircraft.cfg's lines into its package-level
        /// <c>icao_type_designator</c> and every <c>[FLTSIM.N] title = "..."</c>.
        /// Tolerant of <c>=</c>/space variations, quotes, and case. Public so the probe can
        /// unit-check parsing against literal text.
        /// </summary>
        public static (string? Icao, List<string> Titles) Parse(IReadOnlyList<string> lines)
        {
            string? icao = null;
            var titles = new List<string>();

            foreach (var raw in lines)
            {
                if (raw == null) continue;
                string line = raw.Trim();
                if (line.Length == 0) continue;
                // Strip inline comments (; or // ) — keep it simple; values never contain ';'.
                int semi = line.IndexOf(';');
                if (semi >= 0) line = line.Substring(0, semi).Trim();
                if (line.Length == 0) continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string val = line.Substring(eq + 1).Trim();

                // Strip surrounding quotes from the value.
                if (val.Length >= 2 && val[0] == '"' && val[val.Length - 1] == '"')
                    val = val.Substring(1, val.Length - 2).Trim();

                if (key == "icao_type_designator")
                {
                    if (icao == null && val.Length > 0) icao = val;
                }
                else if (key == "title")
                {
                    if (val.Length > 0) titles.Add(val);
                }
            }
            return (icao, titles);
        }

        // --- file discovery (mirrors EFBModPackageManager / GsxAirplaneProfile path logic) ----

        private static IEnumerable<string> EnumerateAircraftCfgFiles()
        {
            string? pkgRoot = FindInstalledPackagesPath();
            if (pkgRoot == null) yield break;

            // Scan Community + Official\OneStore + Official\Steam.
            var roots = new List<string>();
            string community = Path.Combine(pkgRoot, "Community");
            if (SafeDirExists(community)) roots.Add(community);

            string official = Path.Combine(pkgRoot, "Official");
            if (SafeDirExists(official))
                foreach (var sub in SafeDirs(official)) roots.Add(sub); // OneStore, Steam, ...

            foreach (var root in roots)
            {
                foreach (var pkg in SafeDirs(root))
                {
                    // Only aircraft packages have SimObjects\Airplanes — this is the bound that
                    // keeps us out of scenery/texture trees entirely.
                    string airplanes = Path.Combine(pkg, "SimObjects", "Airplanes");
                    if (!SafeDirExists(airplanes)) continue;

                    foreach (var cfg in EnumerateCfgBounded(airplanes, "aircraft.cfg", 0))
                        yield return cfg;
                }
            }
        }

        /// <summary>
        /// Depth-bounded recursive search for files named <paramref name="fileName"/> under
        /// <paramref name="dir"/>. Caps recursion at <see cref="MaxScanDepth"/> so a deeply
        /// nested texture tree can't cause a UI stall or runaway scan. Never throws.
        /// </summary>
        private static IEnumerable<string> EnumerateCfgBounded(string dir, string fileName, int depth)
        {
            // Files in this directory.
            foreach (var f in SafeFiles(dir, fileName))
                yield return f;

            if (depth >= MaxScanDepth) yield break;

            foreach (var sub in SafeDirs(dir))
                foreach (var f in EnumerateCfgBounded(sub, fileName, depth + 1))
                    yield return f;
        }

        /// <summary>
        /// Resolves the FS2024 / FS2020 packages root by reading <c>InstalledPackagesPath</c>
        /// from the known <c>UserCfg.opt</c> locations (MS-Store Limitless, Steam/standalone,
        /// and the FS2020 equivalents). Returns the first existing root, or null.
        /// </summary>
        public static string? FindInstalledPackagesPath()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string[] candidates =
            {
                // FS2024 MS-Store (Limitless) and Steam/standalone.
                Path.Combine(local, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt"),
                Path.Combine(appdata, "Microsoft Flight Simulator 2024", "UserCfg.opt"),
                // FS2020 MS-Store and Steam/standalone.
                Path.Combine(local, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt"),
                Path.Combine(appdata, "Microsoft Flight Simulator", "UserCfg.opt"),
            };

            foreach (var cfg in candidates)
            {
                try
                {
                    if (!File.Exists(cfg)) continue;
                    foreach (var l in File.ReadLines(cfg))
                    {
                        string t = l.TrimStart();
                        if (!t.StartsWith("InstalledPackagesPath", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (t.StartsWith("InstalledPackagesPathNextBoot", StringComparison.OrdinalIgnoreCase))
                            continue;
                        int q1 = l.IndexOf('"');
                        int q2 = l.LastIndexOf('"');
                        if (q1 >= 0 && q2 > q1)
                        {
                            string p = l.Substring(q1 + 1, q2 - q1 - 1);
                            if (SafeDirExists(p)) return p;
                        }
                    }
                }
                catch { /* try the next candidate */ }
            }
            return null;
        }

        // --- safe IO helpers (never throw) ---------------------------------------------

        private static bool SafeDirExists(string path)
        { try { return Directory.Exists(path); } catch { return false; } }

        private static string[] SafeDirs(string path)
        { try { return Directory.GetDirectories(path); } catch { return Array.Empty<string>(); } }

        private static string[] SafeFiles(string path, string pattern)
        { try { return Directory.GetFiles(path, pattern); } catch { return Array.Empty<string>(); } }
    }
}
