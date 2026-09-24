using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.SceneryIndex;

/// <summary>
/// Tier 3: what the installed scenery models. Each package's BGLs are streamed once (model names
/// from ModelInfo tags, placements by seeking the section table) and the RAW result is cached per
/// package — model name plus every placement — so the asking airport classifies the names
/// ("KPWT_Hangar_07" is Hangar 7 only at KPWT) and a classifier change reaches a warm cache. Which
/// names get cached is <c>MightBeFeature</c>'s verdict at build time, so widening the kind keywords
/// needs a <c>CurrentSchemaVersion</c> bump. Only a complete scan is persisted (see
/// <see cref="IncompleteMemoLifetime"/>).
/// <para><see cref="GetFeatures"/> keeps placements inside the airport box grown
/// <see cref="BoxMarginMetres"/> and emits one feature per spatial cluster of a name, never a
/// package-wide average (BIKF's seven "DS Hangar" buildings once became one phantom point). The
/// cluster and placement caps are the second clutter net after the classifier's word lists: a
/// building stands in one place or a few, ground equipment is scattered.</para>
/// <para>Call off the UI thread; one lock per package makes it safe from several pool threads.</para>
/// </summary>
public sealed class SceneryPackageIndexer
{
    private readonly string _cacheDir;
    /// <summary>One lock per cache file, so different packages never serialize on each other.</summary>
    private readonly ConcurrentDictionary<string, object> _locks = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The parsed cache, so a catalog rebuild re-reads neither the BGLs nor the JSON.</summary>
    private readonly ConcurrentDictionary<string, CacheFile> _memo = new(StringComparer.OrdinalIgnoreCase);
    private string _lastStatus = "";

    /// <summary>The last <see cref="GetFeatures"/> call's status sentence, swapped in one write so the
    /// settings dialog never sees a half-composed one.</summary>
    public string LastStatus => Volatile.Read(ref _lastStatus);

    /// <summary>Buildings stand beside the pavement, and the navdata box is the exact hull of the
    /// airport's own records.</summary>
    public const double BoxMarginMetres = 500.0;

    // A proper name stands in a few places at most; a generic label ("Terminal") is shared by
    // unrelated models and gets more; a real field can have dozens of hangars. Accepted residual:
    // EGSS's "Inflite Jet Centre" stands in 4 places and is dropped (OSM carries it anyway).
    private const int MaxClustersProper = 3, MaxPlacementsProper = 12, MaxClustersGeneric = 8, MaxPlacementsGeneric = 40,
                      MaxClustersHangar = 40, MaxPlacementsHangar = 200;

    /// <summary>
    /// Terminals and concourses are exempt from the placement cap (never the cluster cap): authors
    /// model one as dozens of parts in one place (EDDB Terminal A: 46 parts), and across 34 packages
    /// no terminal/concourse group was clutter. Exempt by KIND, because a 46-part terminal and a
    /// 41-container blob are both one dense cluster that no count can tell apart.
    /// </summary>
    private static bool PlacementCapApplies(FeatureKind kind) => kind is not (FeatureKind.Terminal or FeatureKind.Concourse);

    // Bump when an existing cache would be wrong — including when the classifier's kind keywords
    // WIDEN, because the cache only holds names MightBeFeature accepted at build time. 3 added
    // "flugsteig"; 4 added sheltair, tac air, clay lacy, "millionair" and the deice spellings.
    private const int CurrentSchemaVersion = 4;
    // So a future enum field is stored by name, never as a bare number.
    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };

    private sealed class CacheFile
    {
        public int SchemaVersion { get; set; }
        public long LayoutLength { get; set; }
        public long LayoutTicks { get; set; }
        public int Placements { get; set; }
        public int Unresolved { get; set; }
        /// <summary>No initializer on purpose: a document with no Models key reads as null and is
        /// rebuilt, where an empty list would pass for a package that models nothing.</summary>
        public List<Model>? Models { get; set; }

        /// <summary>BGLs this scan could not read to the end. Session only, never persisted.</summary>
        [JsonIgnore] public int Unreadable { get; set; }
        /// <summary>BGLs layout.json lists that were not found whole — an install in progress.</summary>
        [JsonIgnore] public int Unfinished { get; set; }
        /// <summary>The only scan worth keeping; a document read from disk is complete by construction.</summary>
        [JsonIgnore] public bool IsComplete => Unreadable == 0 && Unfinished == 0;
        /// <summary>When this scan ran, for <see cref="IncompleteMemoLifetime"/>.</summary>
        [JsonIgnore] public DateTime ScannedUtc { get; set; }
    }
    private sealed class Model { public string Name { get; set; } = ""; public List<double[]> Points { get; set; } = new(); }   // [lat, lon]

    /// <summary>How long a short scan is served from the memo before the package is read again: long
    /// enough not to re-read a 600 MB library every call, short enough not to outlive a file lock.</summary>
    internal static readonly TimeSpan IncompleteMemoLifetime = TimeSpan.FromMinutes(5);

    private readonly Func<DateTime> _utcNow;

    public SceneryPackageIndexer(string cacheDir) : this(cacheDir, () => DateTime.UtcNow) { }

    /// <summary>Test seam: the clock <see cref="IncompleteMemoLifetime"/> is measured against.</summary>
    internal SceneryPackageIndexer(string cacheDir, Func<DateTime> utcNow) { _cacheDir = cacheDir; _utcNow = utcNow; }

    /// <summary>
    /// Every feature the given packages model at <paramref name="icao"/>, within <paramref name="box"/>
    /// (null keeps every placement). <paramref name="locatedByCensus"/> only colours the status line.
    /// </summary>
    public IReadOnlyList<AirportFeature> GetFeatures(string icao, IEnumerable<string> packageDirs, AirportFacilities? box, bool locatedByCensus = false)
        => GetFeatures(icao, packageDirs, box, locatedByCensus, out _);

    /// <summary>
    /// As above, and whether the answer was short (a scan that missed files, or a package that threw).
    /// The caller ORs it into the build's degraded bit, since the catalog built on it is cached too.
    /// </summary>
    public IReadOnlyList<AirportFeature> GetFeatures(string icao, IEnumerable<string> packageDirs, AirportFacilities? box,
                                                     bool locatedByCensus, out bool incomplete)
    {
        var all = new List<AirportFeature>(); var status = new List<string>();
        incomplete = false;
        foreach (var dir in packageDirs)
        {
            string leaf = Path.GetFileName(dir.TrimEnd('\\', '/'));
            try
            {
                var cf = LoadOrBuild(dir);
                var features = FeaturesOf(cf, icao, box);
                all.AddRange(features);
                incomplete |= !cf.IsComplete;
                // The only sign a pilot gets that the answer is short: otherwise it reads exactly like
                // a package that models nothing.
                string unread = cf.Unreadable == 0 ? "" : $", {cf.Unreadable} file{(cf.Unreadable == 1 ? "" : "s")} unreadable";
                string unfinished = cf.Unfinished == 0 ? "" : $", {cf.Unfinished} file{(cf.Unfinished == 1 ? "" : "s")} missing or incomplete";
                status.Add($"{features.Count} features from {leaf} ({cf.Placements} placements, {cf.Unresolved} without a model name{unread}{unfinished})");
            }
            catch (Exception ex)
            {
                Log.Warn("SceneryIndex", $"{icao}: {leaf}: {ex.Message}");
                status.Add($"{leaf}: unreadable");
                incomplete = true;
            }
        }
        string text = status.Count == 0 ? $"{icao}: no installed scenery package found" : $"{icao}: " + string.Join("; ", status);
        if (status.Count > 0 && locatedByCensus) text += " (located by Community scan)";
        Volatile.Write(ref _lastStatus, text);
        return all;
    }

    private static List<AirportFeature> FeaturesOf(CacheFile cf, string icao, AirportFacilities? box)
    {
        var groups = new Dictionary<(FeatureKind, string), (bool Generic, List<LatLon> Points)>();
        // Grown once for every placement of every model, not once per point.
        GrownBox? grown = box?.Grown(BoxMarginMetres);
        foreach (var m in cf.Models!)      // LoadOrBuild returns a cache whose Models it either validated or just built
        {
            var c = SceneryModelNameClassifier.Classify(m.Name, icao);      // once per distinct model, at READ time, for the asking airport
            if (c == null) continue;
            // A short point array means a hand-edited cache: skip the point, never throw.
            var inside = (m.Points ?? new List<double[]>())
                .Where(p => p is { Length: >= 2 })
                .Select(p => new LatLon(p[0], p[1]))
                .Where(p => grown == null || grown.Value.Contains(p.Lat, p.Lon))
                .ToList();
            if (inside.Count == 0) continue;
            if (!groups.TryGetValue((c.Kind, c.Name), out var g)) groups[(c.Kind, c.Name)] = g = (c.NameIsGeneric, new List<LatLon>());
            g.Points.AddRange(inside);
        }

        var result = new List<AirportFeature>();
        foreach (var ((kind, name), g) in groups)
        {
            // The second clutter net: a building stands in one place or a few; equipment is scattered.
            int maxClusters = kind == FeatureKind.Hangar ? MaxClustersHangar : g.Generic ? MaxClustersGeneric : MaxClustersProper;
            int maxPlacements = kind == FeatureKind.Hangar ? MaxPlacementsHangar : g.Generic ? MaxPlacementsGeneric : MaxPlacementsProper;
            if (PlacementCapApplies(kind) && g.Points.Count > maxPlacements) continue;
            var clusters = SurroundingsGeometry.SingleLinkage(g.Points, p => p, AirportFeatureCatalog.SameNameRadiusMetres(kind));
            if (clusters.Count > maxClusters) continue;
            foreach (var cluster in clusters)
            {
                var cen = SurroundingsGeometry.Centroid(cluster);
                result.Add(new AirportFeature { Kind = kind, Name = name, NameIsGeneric = g.Generic, Lat = cen.Lat, Lon = cen.Lon,
                                                Members = cluster, Source = FeatureSource.Scenery });
            }
        }
        return result;
    }

    private CacheFile LoadOrBuild(string dir)
    {
        var stamp = SceneryPackageDisk.LayoutStamp.Of(dir);
        long len = stamp.Length, ticks = stamp.Ticks;

        // Leaf name for humans, plus a hash of the full path so two installs never share a file.
        string fullPath = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        string hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fullPath))).Substring(0, 8);
        string leafName = Path.GetFileName(dir.TrimEnd('\\', '/'));
        string cachePath = Path.Combine(_cacheDir, $"{leafName}-{hash}.json");

        lock (_locks.GetOrAdd(cachePath, _ => new object()))
        {
            // An incomplete memo is served for IncompleteMemoLifetime, then given up on.
            if (_memo.TryGetValue(cachePath, out var memo) && memo.LayoutLength == len && memo.LayoutTicks == ticks
                && (memo.IsComplete || _utcNow() - memo.ScannedUtc < IncompleteMemoLifetime)) return memo;

            if (File.Exists(cachePath))
            {
                try
                {
                    var cached = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(cachePath), JsonOptions);
                    // Another schema is rebuilt, never partly believed.
                    if (cached is { Models: not null } && cached.SchemaVersion == CurrentSchemaVersion
                        && cached.LayoutLength == len && cached.LayoutTicks == ticks)
                    {
                        // Drop a row that names nothing here, at the trust boundary: a null row
                        // once threw out of FeaturesOf on every call of the memoised document.
                        cached.Models.RemoveAll(m => m is null || string.IsNullOrWhiteSpace(m.Name));
                        _memo[cachePath] = cached;
                        return cached;
                    }
                }
                catch (Exception ex) { Log.Warn("SceneryIndex", $"rebuilding {Path.GetFileName(cachePath)}: {ex.Message}"); }
            }

            var names = new Dictionary<Guid, string>();
            var placements = new List<ScenePlacement>();
            // An enumerator failure throws to GetFeatures (package unreadable, nothing cached); one bad
            // file costs only its own data and the scan's right to be cached. No size cap: the old
            // 600 MB cap dropped the model library of ten real packages.
            var walk = SceneryPackageDisk.WalkBgls(dir, leafName, stream =>
            {
                foreach (var kv in ModelLibNameReader.Read(stream)) names[kv.Key] = kv.Value;
                stream.Position = 0;
                // A read that died halfway returns what parsed; only it can say it did not finish.
                placements.AddRange(BglPlacementReader.Read(stream, out bool readToTheEnd));
                return readToTheEnd;
            });
            int unreadable = walk.Unreadable;
            // An installer writes layout.json first and the BGLs after it, so check the list too.
            int unfinished = SceneryPackageDisk.UnfinishedLayoutFiles(dir, walk);

            // Resolved after every file is read: a model can be defined in one BGL and placed in another.
            var byName = new Dictionary<string, List<double[]>>(StringComparer.Ordinal);
            int unresolved = 0;
            foreach (var p in placements)
            {
                if (!names.TryGetValue(p.ModelGuid, out var model)) { unresolved++; continue; }
                if (!SceneryModelNameClassifier.MightBeFeature(model)) continue;   // never a feature at any airport: not worth caching
                if (!byName.TryGetValue(model, out var pts)) byName[model] = pts = new List<double[]>();
                pts.Add(new[] { p.Lat, p.Lon });
            }

            var models = new List<Model>(byName.Count);
            foreach (var (model, pts) in byName) models.Add(new Model { Name = model, Points = pts });
            var cf = new CacheFile { SchemaVersion = CurrentSchemaVersion, LayoutLength = len, LayoutTicks = ticks,
                                     Placements = placements.Count, Unresolved = unresolved, Models = models,
                                     Unreadable = unreadable, Unfinished = unfinished, ScannedUtc = _utcNow() };
            // Persist only a complete scan: a lock, an I/O error or an install in progress is a moment,
            // and persisted it would freeze a short answer until the package next updates.
            if (cf.IsComplete) SceneryPackageDisk.PersistJson(_cacheDir, cachePath, JsonSerializer.Serialize(cf, JsonOptions));
            _memo[cachePath] = cf;
            Log.Info("SceneryIndex", $"indexed {leafName}: {models.Count} models, {placements.Count} placements, " +
                                     $"{unresolved} without a model name, {unreadable} unreadable, {unfinished} missing or incomplete" +
                                     (cf.IsComplete ? "" : " (not cached)"));
            return cf;
        }
    }
}
