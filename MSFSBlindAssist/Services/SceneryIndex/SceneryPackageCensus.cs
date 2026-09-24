using System.Diagnostics;
using System.Text.Json;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.SceneryIndex;

/// <summary>
/// Which installed Community package models this airport, found from where each package's objects
/// stand — an MSFS 2024 navdata build records no package paths, so <see cref="SceneryPackageLocator"/>
/// has nothing there. Header-only: per BGL the section table and placement subsections (40 packages,
/// 2,443 BGLs, 21 MB measured), plus each package's layout.json content list.
/// <para>Cached per package on layout.json's stamp, as a placement COUNT per 0.005° cell (a few
/// hundred cells against tens of thousands of placements). A cell partly overlapping the box counts
/// whole; against the exact point test on 13 airports that changed no verdict.</para>
/// <para>Call off the UI thread; one lock for the whole census.</para>
/// </summary>
public sealed class SceneryPackageCensus
{
    /// <summary>Below this is a livery's hangar, a city pack's edge, or one static aircraft.</summary>
    public const int MinPlacementsInBox = 20, MaxPackages = 3;

    /// <summary>Box margin: smaller than the indexer's, because this only identifies the package.</summary>
    public const double BoxMarginMetres = 300.0, CellDegrees = 0.005;

    // Bump when a document means something different from an older build's. 2: schema-1 rows could
    // hold a short count scanned mid-install and frozen under the final layout.json stamp.
    internal const int CurrentSchemaVersion = 2;
    private const string CacheFileName = "census.json";

    private readonly string _cacheDir;
    private readonly object _lock = new();
    private CacheFile? _cache;

    /// <summary>Each package's manifest verdict, memoised on its layout.json stamp. Under <c>_lock</c>.</summary>
    private readonly Dictionary<string, (SceneryPackageDisk.LayoutStamp Stamp, bool Scenery)> _sceneryVerdicts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The immediate children of Community: a package is a top-level folder.</summary>
    private static readonly EnumerationOptions PackageFolders = new()
    {
        RecurseSubdirectories = false, IgnoreInaccessible = true, AttributesToSkip = 0,
    };

    private sealed class CacheFile
    {
        public int SchemaVersion { get; set; }
        /// <summary>No initializer: a missing key reads as null and is rebuilt, not as "Community is empty".</summary>
        public List<PackageCells>? Packages { get; set; }
    }

    private sealed class PackageCells
    {
        public string Path { get; set; } = "";
        public long LayoutLength { get; set; }
        public long LayoutTicks { get; set; }
        /// <summary>[latCell, lonCell, count] per occupied cell. Empty is a real answer; missing is not.</summary>
        public List<int[]>? Cells { get; set; }
    }

    public SceneryPackageCensus(string cacheDir) { _cacheDir = cacheDir; }

    /// <summary>
    /// The packages whose objects stand on <paramref name="box"/>'s airport, most first, at most
    /// <see cref="MaxPackages"/>; empty below <see cref="MinPlacementsInBox"/> or with no Community folder.
    /// </summary>
    public IReadOnlyList<string> Locate(string communityDir, AirportFacilities box) => Locate(communityDir, box, out _);

    /// <summary>
    /// As above, and whether any scan came back short. A short scan is not cached, but the catalog
    /// built on it is, so the caller ORs this into the build's degraded bit.
    /// </summary>
    public IReadOnlyList<string> Locate(string communityDir, AirportFacilities box, out bool incomplete)
    {
        incomplete = false;
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(communityDir) || !Directory.Exists(communityDir)) return Array.Empty<string>();

            var cache = _cache ??= Load();
            var known = new Dictionary<string, PackageCells>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in cache.Packages!) known[p.Path] = p;

            var seen = new List<PackageCells>();
            int rescanned = 0;
            bool changed = false;
            var clock = Stopwatch.StartNew();

            foreach (var (dir, stamp) in ScenerylikePackages(communityDir))
            {
                long len = stamp.Length, ticks = stamp.Ticks;
                if (known.TryGetValue(dir, out var hit) && hit.Cells != null && hit.LayoutLength == len && hit.LayoutTicks == ticks)
                {
                    seen.Add(hit);
                    continue;
                }
                var (cells, complete) = Scan(dir);
                incomplete |= !complete;
                var fresh = new PackageCells { Path = dir, LayoutLength = len, LayoutTicks = ticks, Cells = cells };
                seen.Add(fresh);                                // what WAS read still counts for this call
                rescanned++;
                // Keep only a complete scan (a lock is a moment, not a property of the package), and
                // never let an out-of-date row survive the attempt.
                if (complete) { known[dir] = fresh; changed = true; }
                else if (known.Remove(dir)) changed = true;
            }

            // Drop uninstalled packages — judged on their own layout.json, not "seen this pass", so
            // the other simulator's Community entries survive.
            foreach (string path in known.Keys.ToList())
            {
                if (File.Exists(Path.Combine(path, "layout.json"))) continue;
                known.Remove(path);
                changed = true;
            }

            if (changed)
            {
                cache.Packages = known.Values.ToList();
                Persist(cache);
            }
            if (rescanned > 0)
                Log.Info("SceneryIndex", $"census: {seen.Count} packages, {rescanned} rescanned, {clock.ElapsedMilliseconds} ms");

            return Score(seen, box.Grown(BoxMarginMetres));
        }
    }

    /// <summary>
    /// Every Community package that could be scenery, with its layout.json stamp. A manifest naming
    /// another content_type is taken at its word; a missing or unreadable one is not a reason to skip
    /// (skipping the airport's own package costs the whole feature). Memoised on the stamp.
    /// </summary>
    private List<(string Dir, SceneryPackageDisk.LayoutStamp Stamp)> ScenerylikePackages(string communityDir)
    {
        var result = new List<(string Dir, SceneryPackageDisk.LayoutStamp Stamp)>();
        try
        {
            foreach (string dir in Directory.EnumerateDirectories(communityDir, "*", PackageFolders))
            {
                if (!SceneryPackageDisk.LayoutStamp.TryOf(dir, out var stamp)) continue;
                if (!_sceneryVerdicts.TryGetValue(dir, out var verdict) || verdict.Stamp != stamp)
                    _sceneryVerdicts[dir] = verdict = (stamp, CouldBeScenery(Path.Combine(dir, "manifest.json")));
                if (verdict.Scenery) result.Add((dir, stamp));
            }
        }
        catch (Exception ex) { Log.Warn("SceneryIndex", $"census: {communityDir}: {ex.Message}"); }
        return result;
    }

    private static bool CouldBeScenery(string manifestPath)
    {
        try
        {
            if (!File.Exists(manifestPath)) return true;
            // ReadAllText strips a UTF-8 BOM, which JsonDocument rejects as bytes.
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return true;
            if (!doc.RootElement.TryGetProperty("content_type", out var type) || type.ValueKind != JsonValueKind.String) return true;
            return string.Equals(type.GetString(), "SCENERY", StringComparison.OrdinalIgnoreCase);
        }
        catch { return true; }
    }

    /// <summary>
    /// Placements per 0.005° cell, and whether the scan saw the package whole: every file read to the
    /// end (a lock or an I/O error cut short counts; a malformed file does not) and nothing layout.json
    /// lists missing. Only a complete scan is cached.
    /// </summary>
    private static (List<int[]> Cells, bool Complete) Scan(string dir)
    {
        var cells = new Dictionary<(int Lat, int Lon), int>();
        string leaf = Path.GetFileName(dir.TrimEnd('\\', '/'));
        bool complete;
        try
        {
            var walk = SceneryPackageDisk.WalkBgls(dir, $"census: {leaf}", stream =>
            {
                var placements = BglPlacementReader.Read(stream, out bool readToTheEnd);
                foreach (var p in placements)
                {
                    var key = ((int)Math.Floor(p.Lat / CellDegrees), (int)Math.Floor(p.Lon / CellDegrees));
                    cells[key] = cells.GetValueOrDefault(key) + 1;
                }
                return readToTheEnd;
            });
            // Every file found is not every file the package has.
            int unfinished = SceneryPackageDisk.UnfinishedLayoutFiles(dir, walk);
            if (unfinished > 0)
                Log.Warn("SceneryIndex", $"census: {leaf}: {unfinished} listed BGL file{(unfinished == 1 ? "" : "s")} missing or incomplete");
            complete = walk.Unreadable == 0 && unfinished == 0;
        }
        catch (Exception ex)
        {
            // The enumerator gave up: whole files were never looked at.
            complete = false;
            Log.Warn("SceneryIndex", $"census: {leaf}: {ex.Message}");
        }

        var result = new List<int[]>(cells.Count);
        foreach (var ((lat, lon), count) in cells) result.Add(new[] { lat, lon, count });
        return (result, complete);
    }

    /// <summary>Packages with at least <see cref="MinPlacementsInBox"/> placements reaching the box,
    /// most first; ties break on the path so the answer never depends on enumeration order.</summary>
    private static List<string> Score(List<PackageCells> packages, GrownBox grown)
    {
        var scored = new List<(int Score, string Path)>();
        foreach (var p in packages)
        {
            int score = 0;
            foreach (var cell in p.Cells!)
            {
                if (cell is not { Length: >= 3 }) continue;      // only a hand-edited cache can be short: skip the cell, not the package
                if (CellReaches(cell, grown)) score += cell[2];
            }
            if (score >= MinPlacementsInBox) scored.Add((score, p.Path));
        }
        return scored.OrderByDescending(s => s.Score).ThenBy(s => s.Path, StringComparer.Ordinal)
                     .Take(MaxPackages).Select(s => s.Path).ToList();
    }

    /// <summary>Whether a cell's rectangle reaches the grown box on both axes.</summary>
    private static bool CellReaches(int[] cell, GrownBox grown)
    {
        double bottom = cell[0] * CellDegrees, left = cell[1] * CellDegrees;
        return grown.Reaches(bottom, bottom + CellDegrees, left, left + CellDegrees);
    }

    private CacheFile Load()
    {
        string path = Path.Combine(_cacheDir, CacheFileName);
        try
        {
            if (File.Exists(path))
            {
                var cached = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(path));
                // Another schema is rebuilt, never partly believed.
                if (cached is { Packages: not null } && cached.SchemaVersion == CurrentSchemaVersion)
                {
                    // Drop a row that names nothing here, at the trust boundary: one once threw out
                    // of Locate and cost the whole catalog.
                    cached.Packages.RemoveAll(p => p is null || string.IsNullOrEmpty(p.Path));
                    return cached;
                }
            }
        }
        catch (Exception ex) { Log.Warn("SceneryIndex", $"rebuilding {CacheFileName}: {ex.Message}"); }
        return new CacheFile { SchemaVersion = CurrentSchemaVersion, Packages = new List<PackageCells>() };
    }

    /// <summary>Whole file or nothing (<see cref="SceneryPackageDisk.PersistJson"/>).</summary>
    private void Persist(CacheFile cache)
        => SceneryPackageDisk.PersistJson(_cacheDir, Path.Combine(_cacheDir, CacheFileName), JsonSerializer.Serialize(cache));
}
