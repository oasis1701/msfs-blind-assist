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
        // titleLower -> the immediate Community/Official child folder name that contained the
        // matching aircraft.cfg (e.g. "pmdg-aircraft-738"). Built in the same scan as _byTitle.
        private Dictionary<string, string>? _byTitlePackageFolder;
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
            Dictionary<string, string>? map;
            lock (_lock) { map = _byTitle; }
            return TryLookupByTitle(map, title, out icao);
        }

        /// <summary>
        /// Looks up the installed Community/Official package folder name (e.g.
        /// <c>pmdg-aircraft-738</c>) of the AIRFRAME a loaded aircraft TITLE belongs to — the
        /// immediate child of Community/Official that contains the airframe's own aircraft.cfg.
        /// A title that lives in a separate livery package (an aircraft.cfg carrying
        /// <c>[VARIATION] base_container</c>) resolves to the airframe package it layers on, not
        /// the livery's own folder — that airframe folder name is what MSFS keys the package's
        /// per-package "work" storage by, which is how PMDG-variant SDK/options-file lookups
        /// resolve the right file without guessing a title->folder mapping. Safe to call before
        /// the build completes (returns false until ready). Never throws.
        /// </summary>
        public bool TryGetPackageFolderByTitle(string? title, out string packageFolder)
        {
            Dictionary<string, string>? map;
            lock (_lock) { map = _byTitlePackageFolder; }
            return TryLookupByTitle(map, title, out packageFolder);
        }

        private static bool TryLookupByTitle(Dictionary<string, string>? map, string? title, out string value)
        {
            value = string.Empty;
            if (map == null || string.IsNullOrWhiteSpace(title)) return false;
            if (map.TryGetValue(title.Trim().ToLowerInvariant(), out var hit))
            {
                value = hit;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Blocks until the background scan has finished, or <paramref name="timeout"/> elapses.
        /// Starts the scan if nothing has yet. Returns <see cref="IsReady"/>. Never throws.
        /// </summary>
        public bool WaitUntilReady(TimeSpan timeout)
        {
            BeginBuild();
            Thread? t;
            lock (_lock) { t = _buildThread; }
            try { t?.Join(timeout); }
            catch { /* never propagate */ }
            return _isReady;
        }

        /// <summary>
        /// Returns every (Title, Icao) pair discovered on this machine. Forces the build and
        /// WAITS for it to finish (for the probe / diagnostics). Returns empty on any failure.
        /// </summary>
        public IReadOnlyList<(string Title, string Icao)> EnumerateInstalled()
        {
            // Bounded so a pathological scan can't hang the probe forever; in practice the scan
            // completes in well under a second.
            WaitUntilReady(TimeSpan.FromSeconds(60));

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
            Dictionary<string, string> byTitle;
            Dictionary<string, string> byTitlePackageFolder;
            try
            {
                (byTitle, byTitlePackageFolder) = BuildMaps();
            }
            catch
            {
                byTitle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                byTitlePackageFolder = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            lock (_lock) { _byTitle = byTitle; _byTitlePackageFolder = byTitlePackageFolder; }
            _isReady = true;
        }

        /// <summary>
        /// Scans all installed aircraft.cfg files and builds the title->ICAO map. Pure (no
        /// instance state) so the probe can also call it directly; never throws.
        /// </summary>
        public static Dictionary<string, string> BuildMap() => BuildMaps().ByTitleIcao;

        /// <summary>
        /// Scans all installed aircraft.cfg files ONCE and builds both the title->ICAO map and
        /// the title->package-folder map from the same pass. Pure (no instance state); never
        /// throws.
        /// </summary>
        private static (Dictionary<string, string> ByTitleIcao, Dictionary<string, string> ByTitlePackageFolder) BuildMaps()
        {
            var byTitle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var byPackageFolder = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // Airframe SimObject folder name (e.g. "PMDG 737-800") -> the package that owns it,
            // recorded only from aircraft.cfg files WITHOUT a base_container (i.e. the airframe
            // itself, never a livery). A livery package's titles are attributed to the airframe
            // package through this map in the second pass below — its own folder is not where
            // MSFS keeps the airframe's per-package storage.
            var airframePackageBySimObject = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var titlePackages = new List<(string TitleKey, string PackageFolder, string? BaseContainer)>();
            try
            {
                foreach (var (cfg, packageFolder, simObjectFolder) in EnumerateAircraftCfgFiles())
                {
                    try
                    {
                        string[] lines = File.ReadAllLines(cfg);
                        var (icao, titles) = Parse(lines);
                        string? baseContainer = ParseBaseContainer(lines);
                        if (baseContainer == null && !airframePackageBySimObject.ContainsKey(simObjectFolder))
                            airframePackageBySimObject[simObjectFolder] = packageFolder;
                        if (titles.Count == 0) continue;

                        // A livery cfg carries titles but usually no icao_type_designator of its
                        // own — it still needs a package-folder entry, just not an ICAO one.
                        string? icaoUpper = string.IsNullOrWhiteSpace(icao) ? null : icao.Trim().ToUpperInvariant();
                        foreach (var title in titles)
                        {
                            string key = title.Trim().ToLowerInvariant();
                            if (key.Length == 0) continue;
                            // First found wins (stable across rebuilds; ties are extremely rare).
                            if (icaoUpper != null && !byTitle.ContainsKey(key)) byTitle[key] = icaoUpper;
                            titlePackages.Add((key, packageFolder, baseContainer));
                        }
                    }
                    catch { /* skip unreadable / locked / malformed cfg */ }
                }
            }
            catch { /* swallow — return whatever we gathered */ }

            foreach (var (key, packageFolder, baseContainer) in titlePackages)
            {
                if (byPackageFolder.ContainsKey(key)) continue;
                byPackageFolder[key] = ResolveAirframePackageFolder(packageFolder, baseContainer, airframePackageBySimObject);
            }
            return (byTitle, byPackageFolder);
        }

        /// <summary>
        /// The package folder a title's airframe lives in: the cfg's own package when it has no
        /// <c>base_container</c>, otherwise the package that owns the SimObject folder the
        /// base_container names (its leaf segment — <c>"..\PMDG 737-800"</c> names
        /// <c>PMDG 737-800</c>), falling back to the cfg's own package when that airframe was
        /// not found. Pure; public so it can be characterization-tested without a disk.
        /// </summary>
        public static string ResolveAirframePackageFolder(
            string ownPackageFolder, string? baseContainer, IReadOnlyDictionary<string, string> airframePackageBySimObject)
        {
            if (string.IsNullOrWhiteSpace(baseContainer)) return ownPackageFolder;
            string leaf = baseContainer.Replace('/', '\\').TrimEnd('\\');
            int slash = leaf.LastIndexOf('\\');
            if (slash >= 0) leaf = leaf.Substring(slash + 1);
            leaf = leaf.Trim();
            if (leaf.Length == 0) return ownPackageFolder;
            return airframePackageBySimObject.TryGetValue(leaf, out var airframePackage) ? airframePackage : ownPackageFolder;
        }

        /// <summary>
        /// The <c>[VARIATION] base_container</c> value of a livery aircraft.cfg (quotes stripped,
        /// as written — e.g. <c>..\PMDG 737-800</c>), or null when the cfg has none (an airframe
        /// cfg). Public so the probe/tests can check it against literal text.
        /// </summary>
        public static string? ParseBaseContainer(IReadOnlyList<string> lines)
        {
            foreach (var raw in lines)
            {
                if (raw == null) continue;
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                if (!string.Equals(line.Substring(0, eq).Trim(), "base_container", StringComparison.OrdinalIgnoreCase)) continue;
                string val = line.Substring(eq + 1).Trim();
                int semi = val.IndexOf(';');
                if (semi >= 0) val = val.Substring(0, semi).Trim();
                if (val.Length >= 2 && val[0] == '"' && val[val.Length - 1] == '"')
                    val = val.Substring(1, val.Length - 2).Trim();
                return val.Length == 0 ? null : val;
            }
            return null;
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

        /// <summary>
        /// Yields every installed aircraft.cfg together with the immediate Community/Official
        /// child folder name that contains it (e.g. <c>pmdg-aircraft-738</c>) — the same name
        /// MSFS keys that package's per-package "work" storage folder by — and the SimObject
        /// folder (the immediate child of <c>SimObjects\Airplanes</c>, e.g. <c>PMDG 737-800</c>)
        /// the cfg sits under, which is what a livery's <c>base_container</c> names.
        /// </summary>
        private static IEnumerable<(string CfgPath, string PackageFolder, string SimObjectFolder)> EnumerateAircraftCfgFiles()
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

                    string packageFolder = new DirectoryInfo(pkg).Name;
                    foreach (var cfg in EnumerateCfgBounded(airplanes, "aircraft.cfg", 0))
                    {
                        string simObjectFolder;
                        try
                        {
                            string rel = Path.GetRelativePath(airplanes, cfg);
                            int sep = rel.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
                            simObjectFolder = sep > 0 ? rel.Substring(0, sep) : string.Empty;
                        }
                        catch { simObjectFolder = string.Empty; }
                        yield return (cfg, packageFolder, simObjectFolder);
                    }
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
