using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.SceneryIndex;

/// <summary>
/// Tier 3: what the installed scenery actually models. Per package folder every *.bgl is opened
/// ONCE — model names streamed out of its ModelInfo tags, placements read by seeking its section
/// table, neither pulling the file into memory — and what is cached is the RAW result: each model
/// name that could name a feature, with every point it was placed at. Nothing in the cache is
/// classified, so the airport that ASKS decides the names ("KPWT_Hangar_07" is Hangar 7 at KPWT
/// and somebody else's building at KTIW), and a change to how a name classifies reaches a pilot
/// whose cache is already warm. One axis is NOT free that way: which names are cached is
/// MightBeFeature's verdict at build time, so ADDING a kind keyword needs a schema bump (see
/// CurrentSchemaVersion). Cached per package as JSON under cacheDir — the user's OWN local files, so a
/// disk cache is fine (unlike OSM data) — keyed on the full package path and stamped with
/// layout.json's length + mtime; written to a .tmp and moved into place, so a crash never leaves
/// a truncated cache for a later run to read, and a cache of another schema is rebuilt, not read.
///
/// <see cref="GetFeatures"/> then, for the asking airport: classifies each model name, drops every
/// placement outside that airport's own box grown by <see cref="BoxMarginMetres"/> (one package
/// ships BIKF and BIRK together, 18 km apart), and emits one feature per spatial CLUSTER of
/// same-named placements — never one package-wide average, which put MK Studios BIKF's seven
/// "DS Hangar" buildings, 2.3 km apart, at a single phantom point between them.
///
/// The cluster/placement caps are the SECOND clutter net, after the classifier's word lists:
/// no word list can tell a baggage dolly named after the cargo ramp it serves from a building,
/// but a building stands in one place or a few while ground equipment is scattered over dozens.
/// Terminals and concourses answer to the cluster half only — see <see cref="PlacementCapApplies"/>.
///
/// Call from a background thread: the first index of a large package reads its model library once
/// (ten Community libraries exceed 600 MB); every later call is a memo hit. Safe to call from two
/// pool threads at once — one lock per package, taken around the memo, the cache file and the scan.
/// </summary>
public sealed class SceneryPackageIndexer
{
    private readonly string _cacheDir;
    /// <summary>One lock per cache file: two airports indexing at once must not serialize on each
    /// other, and two threads indexing ONE package must not both scan it and race the cache write.</summary>
    private readonly ConcurrentDictionary<string, object> _locks = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The parsed cache, so a catalog rebuild re-reads neither the BGLs nor the JSON.</summary>
    private readonly ConcurrentDictionary<string, CacheFile> _memo = new(StringComparer.OrdinalIgnoreCase);
    private string _lastStatus = "";

    /// <summary>The last <see cref="GetFeatures"/> call's whole sentence, swapped in one write:
    /// the settings dialog reads it from the UI thread while a pool thread builds, and must see
    /// the old sentence or the new one, never a half-composed one.</summary>
    public string LastStatus => Volatile.Read(ref _lastStatus);

    /// <summary>Buildings stand beside the pavement, not on it, and the navdata box is the exact
    /// hull of the airport's own records — so a placement is judged against the box grown by this.</summary>
    public const double BoxMarginMetres = 500.0;

    /// <summary>Every *.bgl under the package. IgnoreInaccessible because the SearchOption overload
    /// throws from the ENUMERATOR — outside the per-file catch below — so one folder the user
    /// cannot read cost the whole package; CaseInsensitive because packages ship both "modelLib.BGL"
    /// and "objects.bgl"; AttributesToSkip 0 to keep the SearchOption overload's behaviour, which
    /// reads hidden and system files (EnumerationOptions would skip them by default).</summary>
    private static readonly EnumerationOptions BglFiles = new()
    {
        RecurseSubdirectories = true, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive, AttributesToSkip = 0,
    };

    // A named building stands in one place, or a few (an author splits it into parts, or a second
    // one really exists). A generic name is this app's own label ("Terminal", "Fuel"), shared by
    // unrelated models, so it is allowed more of both. Hangars are the exception a real field needs.
    // Accepted residual on the cluster cap: EGSS's real "Inflite Jet Centre" stands in 4 places and
    // is dropped by it — a name standing in more than three separate places is not somewhere a pilot
    // can be sent (YSSY's six "Ils Tower" masts are the shape it exists for), and OSM carries an FBO
    // like that one anyway. Measured once, recorded so it is not re-derived.
    private const int MaxClustersProper = 3, MaxPlacementsProper = 12, MaxClustersGeneric = 8, MaxPlacementsGeneric = 40,
                      MaxClustersHangar = 40, MaxPlacementsHangar = 200;

    /// <summary>
    /// Whether the PLACEMENT cap applies to this kind at all — the cluster cap always does.
    /// Terminals and concourses are EXEMPT: an author routinely models one of them as dozens of
    /// separate parts standing in ONE place, and <see cref="SceneryModelNameClassifier"/>
    /// deliberately collapses those parts onto one name, so counting them as "too many placements"
    /// threw away the building a pilot most wants named. Measured across 34 Community packages:
    /// EDDB's "Terminal A"/"B"/"C" are 46/49/49 parts, one cluster each, 350–714 m across, and its
    /// generic "Terminal" is 175 parts in one 764 m cluster; KPHX "Terminal L" is 21 parts at a
    /// single coordinate; KATL "Terminal E" is 14 parts in 2 clusters — all dropped by the cap,
    /// while NO terminal or concourse group in those packages was clutter. Every group the cap
    /// legitimately removed was ground equipment: EIDW's 41 containers in one 252 m blob, KPDX's
    /// 130 "Ramp Cargo Fedex", KMEM's 40 "Trailer UPS", ENGM's five "Ground Fuel N" fleets.
    /// The exemption is by KIND and must stay so: a 46-part terminal and a 41-container blob are
    /// both ONE dense cluster, so no cluster-count rule can tell them apart.
    /// The trade it accepts: an exempt kind's group is no longer bounded before
    /// SurroundingsGeometry.SingleLinkage, which is O(n²) and re-runs on every GetFeatures call.
    /// Measured, that is a non-event — the largest real group is EDDB's 175 parts (~15k haversines)
    /// and a warm pass over all 34 packages takes 6-7 ms — so it is recorded, not guarded against.
    /// </summary>
    private static bool PlacementCapApplies(FeatureKind kind) => kind is not (FeatureKind.Terminal or FeatureKind.Concourse);

    // BUMP THIS when a change would make an existing cache wrong. A change to how a name
    // CLASSIFIES needs no bump (the cache holds raw names), but WIDENING the classifier's kind
    // keywords does: the names that reach the cache are the ones MightBeFeature accepted at build
    // time, so a name a new keyword would now recognise was filtered out and is not in there.
    private const int CurrentSchemaVersion = 2;
    // Schema 2 carries no enum, but a cache must never come to hold a bare enum NUMBER if one is added.
    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };

    private sealed class CacheFile
    {
        public int SchemaVersion { get; set; }
        public long LayoutLength { get; set; }
        public long LayoutTicks { get; set; }
        public int Placements { get; set; }
        public int Unresolved { get; set; }
        /// <summary>Deliberately nullable with NO property initializer, so a document that carries
        /// no Models key at all deserialises to null and is REBUILT. With an initializer it came
        /// back as an empty list, indistinguishable from a package that really models nothing —
        /// the guard below read as a check and was inert.</summary>
        public List<Model>? Models { get; set; }
    }
    private sealed class Model { public string Name { get; set; } = ""; public List<double[]> Points { get; set; } = new(); }   // [lat, lon]

    public SceneryPackageIndexer(string cacheDir) { _cacheDir = cacheDir; }

    /// <summary>
    /// Every feature the given packages model at <paramref name="icao"/>. <paramref name="box"/> is
    /// that airport's navdata bounding box (null = keep every placement, for a caller that has none).
    /// <paramref name="locatedByCensus"/> only colours the status line: it says the packages were
    /// found by scanning Community rather than named by navdata.
    /// </summary>
    public IReadOnlyList<AirportFeature> GetFeatures(string icao, IEnumerable<string> packageDirs, AirportFacilities? box, bool locatedByCensus = false)
    {
        var all = new List<AirportFeature>(); var status = new List<string>();
        foreach (var dir in packageDirs)
        {
            string leaf = Path.GetFileName(dir.TrimEnd('\\', '/'));
            try
            {
                var cf = LoadOrBuild(dir);
                var features = FeaturesOf(cf, icao, box);
                all.AddRange(features);
                status.Add($"{features.Count} features from {leaf} ({cf.Placements} placements, {cf.Unresolved} without a model name)");
            }
            catch (Exception ex)
            {
                Log.Warn("SceneryIndex", $"{icao}: {leaf}: {ex.Message}");
                status.Add($"{leaf}: unreadable");
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
        foreach (var m in cf.Models!)      // LoadOrBuild returns a cache whose Models it either validated or just built
        {
            var c = SceneryModelNameClassifier.Classify(m.Name, icao);      // once per distinct model, at READ time, for the asking airport
            if (c == null) continue;
            // A missing or short point array can only come from a hand-edited or truncated cache:
            // skip the point rather than throw, which would report a package we read fine as unreadable.
            var inside = (m.Points ?? new List<double[]>())
                .Where(p => p is { Length: >= 2 })
                .Select(p => new LatLon(p[0], p[1]))
                .Where(p => box == null || box.ContainsPoint(p.Lat, p.Lon, BoxMarginMetres))
                .ToList();
            if (inside.Count == 0) continue;
            if (!groups.TryGetValue((c.Kind, c.Name), out var g)) groups[(c.Kind, c.Name)] = g = (c.NameIsGeneric, new List<LatLon>());
            g.Points.AddRange(inside);
        }

        var result = new List<AirportFeature>();
        foreach (var ((kind, name), g) in groups)
        {
            // Second net after the classifier's word lists: a BUILDING stands in one place (or a few);
            // ground equipment is scattered. Hangars are the exception — a field can have dozens.
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
        string layout = Path.Combine(dir, "layout.json");
        var info = new FileInfo(layout);
        long len = info.Exists ? info.Length : 0, ticks = info.Exists ? info.LastWriteTimeUtc.Ticks : 0;

        // Cache file name: the package's leaf folder (so a human can read the folder) plus a hash of
        // its FULL path, so two installs sharing a leaf name never share a cache file.
        string fullPath = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        string hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fullPath))).Substring(0, 8);
        string leafName = Path.GetFileName(dir.TrimEnd('\\', '/'));
        string cachePath = Path.Combine(_cacheDir, $"{leafName}-{hash}.json");

        lock (_locks.GetOrAdd(cachePath, _ => new object()))
        {
            if (_memo.TryGetValue(cachePath, out var memo) && memo.LayoutLength == len && memo.LayoutTicks == ticks) return memo;

            if (File.Exists(cachePath))
            {
                try
                {
                    var cached = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(cachePath), JsonOptions);
                    // Another schema's fields mean nothing here — schema 1 cached CLASSIFIED features
                    // under whichever ICAO asked first — so it is rebuilt, never partly believed.
                    if (cached is { Models: not null } && cached.SchemaVersion == CurrentSchemaVersion
                        && cached.LayoutLength == len && cached.LayoutTicks == ticks)
                    {
                        _memo[cachePath] = cached;
                        return cached;
                    }
                }
                catch (Exception ex) { Log.Warn("SceneryIndex", $"rebuilding {Path.GetFileName(cachePath)}: {ex.Message}"); }
            }

            var names = new Dictionary<Guid, string>();
            var placements = new List<ScenePlacement>();
            foreach (var bgl in Directory.EnumerateFiles(dir, "*.bgl", BglFiles))
            {
                // One bad file costs its own names and placements, never the package's. No size cap:
                // both readers are streamed/seeking, and the 600 MB one used to drop the model
                // library — every name — of ten real airport packages.
                try
                {
                    // Shared for write and delete: the simulator may hold this very file open.
                    using var stream = new FileStream(bgl, FileMode.Open, FileAccess.Read,
                                                      FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
                    foreach (var kv in ModelLibNameReader.Read(stream)) names[kv.Key] = kv.Value;
                    stream.Position = 0;
                    placements.AddRange(BglPlacementReader.Read(stream));
                }
                catch (Exception ex) { Log.Warn("SceneryIndex", $"{leafName}: {Path.GetFileName(bgl)}: {ex.Message}"); }
            }

            // Names are resolved only once every file has been read: a package is free to define a
            // model in one BGL and place it from another, in whatever order the folder lists them.
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
                                     Placements = placements.Count, Unresolved = unresolved, Models = models };
            Persist(cachePath, cf);
            _memo[cachePath] = cf;
            Log.Info("SceneryIndex", $"indexed {leafName}: {models.Count} models, {placements.Count} placements, {unresolved} without a model name");
            return cf;
        }
    }

    /// <summary>Whole file or nothing: a truncated cache would be read as a package with fewer
    /// buildings, which nothing downstream could tell from a package that models fewer.</summary>
    private void Persist(string cachePath, CacheFile cf)
    {
        string tmp = cachePath + ".tmp";
        try
        {
            Directory.CreateDirectory(_cacheDir);
            File.WriteAllText(tmp, JsonSerializer.Serialize(cf, JsonOptions));
            File.Move(tmp, cachePath, overwrite: true);
        }
        catch (Exception ex)
        {
            // A cache that cannot be written costs the NEXT call its shortcut, not this one its
            // features — reporting the package unreadable when it was read fine would be a lie.
            Log.Warn("SceneryIndex", $"could not write {Path.GetFileName(cachePath)}: {ex.Message}");
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
        }
    }
}
