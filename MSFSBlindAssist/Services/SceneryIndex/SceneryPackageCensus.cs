using System.Diagnostics;
using System.Text.Json;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.SceneryIndex;

/// <summary>
/// Which installed package models THIS airport — answered from where each package's objects
/// stand, because an MSFS 2024 navdata database records no package paths at all
/// (airport.scenery_local_path is NULL on every row, so <see cref="SceneryPackageLocator"/> has
/// nothing to hand the indexer and the whole scenery tier went dark there). HEADER-ONLY: per BGL
/// it reads the section table and the placement subsections, never a model library's bulk
/// (measured over this machine's Community folder: 40 scenery packages, 2,443 BGL files, 21 MB).
/// Cached on disk per package, keyed on layout.json's length and mtime; the user's own local
/// files, so a disk cache is fine. Community only — Official/OneStore content is not scanned.
///
/// What is cached is a COUNT PER CELL, not the placements: a package occupies a few hundred cells
/// at most (measured: 594 for the largest, 3,266 across all 40) where it holds tens of thousands
/// of placements, and nothing downstream needs the individual points — the indexer re-reads the
/// package it is handed. A cell that only PARTLY overlaps the airport box contributes all of its
/// count, so a score is an over-estimate of up to one cell's worth at each edge; measured against
/// the exact point test on 13 airports it changed no verdict, because a real airport package
/// scores in the thousands and the bar is <see cref="MinPlacementsInBox"/>.
///
/// Call from a background thread: the first pass reads every scenery package in the folder. One
/// lock for the whole census, so two pool threads asking at once serialize rather than both scan.
/// </summary>
public sealed class SceneryPackageCensus
{
    /// <summary>What it takes to call a package "the one that models this airport". Below this is
    /// a livery's hangar, a city pack's edge, or one static aircraft parked on the ramp.</summary>
    public const int MinPlacementsInBox = 20, MaxPackages = 3;

    /// <summary>Buildings stand beside the pavement and the navdata box is the exact hull of the
    /// airport's own records, so a placement is judged against the box grown by this — the same
    /// reasoning as <see cref="SceneryPackageIndexer.BoxMarginMetres"/>, smaller because this is
    /// about IDENTIFYING the package rather than keeping every building it models.</summary>
    public const double BoxMarginMetres = 300.0, CellDegrees = 0.005;

    private const int CurrentSchemaVersion = 1;
    private const string CacheFileName = "census.json";

    private readonly string _cacheDir;
    private readonly object _lock = new();
    private CacheFile? _cache;

    /// <summary>Every *.bgl under one package. Same options as
    /// <see cref="SceneryPackageIndexer"/>'s (IgnoreInaccessible because the enumerator itself
    /// throws on a folder the user cannot read, CaseInsensitive because packages ship both
    /// "modelLib.BGL" and "objects.bgl", AttributesToSkip 0 so hidden and system files are read)
    /// plus a depth bound this one needs and that one does not: the indexer opens the ONE package
    /// navdata named, while this walks whatever is in Community, where a package is routinely a
    /// junction to a library on another drive. Reparse points must be followed — an add-on linker
    /// puts every package behind one, and a census that skipped them would find nothing for
    /// exactly the pilots with the most scenery — so the depth bound is what ends a link cycle.
    /// Packages are shallow (the deepest real BGL here sits 4 levels down), so 12 loses nothing.</summary>
    private static readonly EnumerationOptions BglFiles = new()
    {
        RecurseSubdirectories = true, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive,
        AttributesToSkip = 0, MaxRecursionDepth = 12,
    };

    /// <summary>The immediate children of Community. Same reasons for IgnoreInaccessible and
    /// AttributesToSkip; no recursion, because a package is a top-level folder.</summary>
    private static readonly EnumerationOptions PackageFolders = new()
    {
        RecurseSubdirectories = false, IgnoreInaccessible = true, AttributesToSkip = 0,
    };

    private sealed class CacheFile
    {
        public int SchemaVersion { get; set; }
        /// <summary>Nullable with NO initializer, so a document carrying no Packages key at all
        /// deserialises to null and is rebuilt rather than read as "Community is empty".</summary>
        public List<PackageCells>? Packages { get; set; }
    }

    private sealed class PackageCells
    {
        public string Path { get; set; } = "";
        public long LayoutLength { get; set; }
        public long LayoutTicks { get; set; }
        /// <summary>[latCell, lonCell, count] per occupied cell. Nullable for the same reason as
        /// Packages above: an EMPTY list says the package models nothing anywhere, which is a real
        /// answer worth caching; a missing key says nothing at all.</summary>
        public List<int[]>? Cells { get; set; }
    }

    public SceneryPackageCensus(string cacheDir) { _cacheDir = cacheDir; }

    /// <summary>
    /// The installed packages whose objects stand on <paramref name="box"/>'s airport, the one
    /// with the most first, at most <see cref="MaxPackages"/>. Empty when nothing reaches
    /// <see cref="MinPlacementsInBox"/> — including when there is no Community folder at all.
    /// </summary>
    public IReadOnlyList<string> Locate(string communityDir, AirportFacilities box)
    {
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

            foreach (string dir in ScenerylikePackages(communityDir))
            {
                var layout = new FileInfo(Path.Combine(dir, "layout.json"));
                long len = layout.Exists ? layout.Length : 0, ticks = layout.Exists ? layout.LastWriteTimeUtc.Ticks : 0;
                if (known.TryGetValue(dir, out var hit) && hit.Cells != null && hit.LayoutLength == len && hit.LayoutTicks == ticks)
                {
                    seen.Add(hit);
                    continue;
                }
                var fresh = new PackageCells { Path = dir, LayoutLength = len, LayoutTicks = ticks, Cells = Scan(dir) };
                known[dir] = fresh;
                seen.Add(fresh);
                rescanned++;
                changed = true;
            }

            // A package that was uninstalled must leave, or the cache grows for the life of the
            // install and keeps answering for a folder that is no longer there. Judged on the
            // package's own layout.json rather than on "was it seen this pass", so a second
            // Community folder's entries (the other simulator's) are not thrown away each time.
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

            return Score(seen, box);
        }
    }

    /// <summary>
    /// Every immediate child of Community that is a package (it has a layout.json) and could be
    /// scenery. A manifest naming another content_type — AIRCRAFT, LIVERY, MISC, TOOL — is taken
    /// at its word and skipped; a missing or unreadable manifest is NOT a reason to skip, because
    /// the cost of reading a package that models nothing is a handful of header seeks while the
    /// cost of skipping the airport's own package is the whole feature.
    /// </summary>
    private static List<string> ScenerylikePackages(string communityDir)
    {
        var result = new List<string>();
        try
        {
            foreach (string dir in Directory.EnumerateDirectories(communityDir, "*", PackageFolders))
            {
                if (!File.Exists(Path.Combine(dir, "layout.json"))) continue;
                if (!CouldBeScenery(Path.Combine(dir, "manifest.json"))) continue;
                result.Add(dir);
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
            // ReadAllText, not ReadAllBytes: a manifest.json may carry a UTF-8 BOM (none of the 82
            // in this install does, but nothing stops one), which JsonDocument rejects outright as
            // a byte sequence and ReadAllText strips.
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return true;
            if (!doc.RootElement.TryGetProperty("content_type", out var type) || type.ValueKind != JsonValueKind.String) return true;
            return string.Equals(type.GetString(), "SCENERY", StringComparison.OrdinalIgnoreCase);
        }
        catch { return true; }
    }

    /// <summary>How many placements the package has in each 0.005° cell. One try/catch per file:
    /// one bad BGL costs its own placements, never the package's.</summary>
    private static List<int[]> Scan(string dir)
    {
        var cells = new Dictionary<(int Lat, int Lon), int>();
        string leaf = Path.GetFileName(dir.TrimEnd('\\', '/'));
        try
        {
            foreach (string bgl in Directory.EnumerateFiles(dir, "*.bgl", BglFiles))
            {
                try
                {
                    // Shared for write and delete: the simulator may hold this very file open.
                    using var stream = new FileStream(bgl, FileMode.Open, FileAccess.Read,
                                                      FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
                    foreach (var p in BglPlacementReader.Read(stream))
                    {
                        var key = ((int)Math.Floor(p.Lat / CellDegrees), (int)Math.Floor(p.Lon / CellDegrees));
                        cells[key] = cells.GetValueOrDefault(key) + 1;
                    }
                }
                catch (Exception ex) { Log.Warn("SceneryIndex", $"census: {leaf}: {Path.GetFileName(bgl)}: {ex.Message}"); }
            }
        }
        catch (Exception ex) { Log.Warn("SceneryIndex", $"census: {leaf}: {ex.Message}"); }

        var result = new List<int[]>(cells.Count);
        foreach (var ((lat, lon), count) in cells) result.Add(new[] { lat, lon, count });
        return result;
    }

    /// <summary>The packages with at least <see cref="MinPlacementsInBox"/> placements in cells
    /// that reach the grown airport box, most first. Ties break on the path so the answer — and
    /// the status line built from it — does not depend on enumeration order.</summary>
    private static List<string> Score(List<PackageCells> packages, AirportFacilities box)
    {
        // Same margin conversion as AirportFacilities.ContainsPoint, over a RECTANGLE rather than
        // a point: a cell counts when its own rectangle reaches the grown box on both axes.
        double dLat = BoxMarginMetres / 111_320.0;
        double dLon = BoxMarginMetres / (111_320.0 * Math.Max(0.05, Math.Cos((box.TopLat + box.BottomLat) / 2.0 * Math.PI / 180.0)));
        double top = box.TopLat + dLat, bottom = box.BottomLat - dLat, left = box.LeftLon - dLon, right = box.RightLon + dLon;

        var scored = new List<(int Score, string Path)>();
        foreach (var p in packages)
        {
            int score = 0;
            foreach (var cell in p.Cells!)
            {
                if (cell is not { Length: >= 3 }) continue;      // only a hand-edited cache can be short: skip the cell, not the package
                double cellBottom = cell[0] * CellDegrees, cellLeft = cell[1] * CellDegrees;
                if (cellBottom <= top && cellBottom + CellDegrees >= bottom && cellLeft <= right && cellLeft + CellDegrees >= left)
                    score += cell[2];
            }
            if (score >= MinPlacementsInBox) scored.Add((score, p.Path));
        }
        return scored.OrderByDescending(s => s.Score).ThenBy(s => s.Path, StringComparer.Ordinal)
                     .Take(MaxPackages).Select(s => s.Path).ToList();
    }

    private CacheFile Load()
    {
        string path = Path.Combine(_cacheDir, CacheFileName);
        try
        {
            if (File.Exists(path))
            {
                var cached = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(path));
                // Another schema's fields mean nothing here, so the document is rebuilt rather
                // than partly believed.
                if (cached is { Packages: not null } && cached.SchemaVersion == CurrentSchemaVersion) return cached;
            }
        }
        catch (Exception ex) { Log.Warn("SceneryIndex", $"rebuilding {CacheFileName}: {ex.Message}"); }
        return new CacheFile { SchemaVersion = CurrentSchemaVersion, Packages = new List<PackageCells>() };
    }

    /// <summary>Whole file or nothing: a truncated census would read as packages that model
    /// fewer buildings here, which nothing downstream could tell from the truth.</summary>
    private void Persist(CacheFile cache)
    {
        string path = Path.Combine(_cacheDir, CacheFileName), tmp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(_cacheDir);
            File.WriteAllText(tmp, JsonSerializer.Serialize(cache));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            // A cache that cannot be written costs the NEXT call its shortcut, not this one its
            // answer.
            Log.Warn("SceneryIndex", $"could not write {CacheFileName}: {ex.Message}");
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
        }
    }
}
