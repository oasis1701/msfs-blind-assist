using MSFSBlindAssist.Services.TaxiAugment;
int failures = 0;
void Check(bool ok, string label){ Console.WriteLine((ok?"PASS ":"FAIL ")+label); if(!ok) failures++; }
// Resolve fixtures relative to the BINARY (CopyToOutputDirectory puts them there), so the probe
// runs from any cwd — not just the project dir.
string Fix(string n) => Path.Combine(AppContext.BaseDirectory, "fixtures", n);

// Task 1.2: OSM parser assertions
var osmJson = File.ReadAllText(Fix("omdb_osm.json"));
var osm = OsmTaxiSource.Parse(osmJson);
Check(osm.Taxiways.Count > 100, "OMDB OSM: >100 taxiway segments parsed");
Check(osm.Taxiways.Any(t => t.Name == "K"), "OMDB OSM: taxiway K present");
Check(osm.Taxiways.All(t => t.Lat1 != 0 && t.Lat2 != 0), "OMDB OSM: every segment has geometry");

// Task 2.1: apt.dat parser assertions
var apt = AptDatParser.Parse(File.ReadAllText(Fix("omdb_apt.dat")));
Check(apt.Taxiways.Count > 50, "OMDB apt.dat: >50 named taxiway edges");
Check(apt.Taxiways.Any(t => t.Name == "K"), "OMDB apt.dat: taxiway K present");

// ──────────────────────────────────────────────────────────────────────
// Task 3.1: TaxiDataMerger — name-overlay + cross-check + parking fill
// ──────────────────────────────────────────────────────────────────────

// Real OMDB "K" geometry from the OSM fixture (first two nodes of a K way).
// An UNNAMED navdata segment at these coordinates should adopt the name "K".
var nav = new List<NavSegment>
{
    // K segment — navdata-unnamed, geometry from a known OMDB K segment
    new NavSegment("",  25.2354034, 55.3921787, 25.2352965, 55.3923703),
    // L segment — navdata already named "L" — must NOT be overwritten
    new NavSegment("L", 25.2639483, 55.3453943, 25.2638712, 55.3455257),
};

// Use both sources (osm + apt) for the merge
var sources = new List<AirportTaxiData> { osm, apt };
var opt = new MergeOptions();   // defaults: 30 m / 25°

var merged = TaxiDataMerger.MergeNamesOntoNavData(nav, sources, opt, "OMDB", out var cov);

Check(merged.Count == 2, "Merger: output has same segment count as input");
Check(!string.IsNullOrWhiteSpace(merged[0].Name) && merged[0].Name == "K",
      $"Merger: unnamed nav segment adopts name K (got '{merged[0].Name}')");
Check(merged[1].Name == "L",
      $"Merger: navdata-named 'L' keeps its own name (got '{merged[1].Name}')");
Check(cov.NamesAdoptedFromOsm + cov.NamesAdoptedFromAptDat >= 1,
      $"Merger: coverage counts ≥1 adoption (osm={cov.NamesAdoptedFromOsm} apt={cov.NamesAdoptedFromAptDat})");
Check(cov.NavUnnamedSegments == 1, $"Merger: 1 unnamed segment counted (got {cov.NavUnnamedSegments})");
Check(cov.NavNamedTaxiways == 1,   $"Merger: 1 already-named segment counted (got {cov.NavNamedTaxiways})");

// Parking fill: zero navdata parking → should fill from sources
int parkFilled;
var filledParking = TaxiDataMerger.MergeParking(navParkingCount: 0, sources, out parkFilled);
Check(filledParking != null && filledParking.Count > 0,
      $"Merger: parking filled when navdata has zero spots (filled {parkFilled})");

// With navdata parking → should NOT fill
var notFilled = TaxiDataMerger.MergeParking(navParkingCount: 5, sources, out int pf2);
Check(notFilled == null && pf2 == 0,
      "Merger: parking NOT filled when navdata already has spots");

// ──────────────────────────────────────────────────────────────────────
// Task 4.1: TaxiDataCache — per-ICAO JSON cache + TTL
// ──────────────────────────────────────────────────────────────────────
{
    var cacheDir = Path.Combine(Path.GetTempPath(), "taxiaug_test_" + Guid.NewGuid());
    var cache = new TaxiDataCache(cacheDir, ttlDays: 30);

    // Miss on empty cache (dir doesn't exist yet).
    Check(cache.TryLoad("OMDB", out _) == false, "Cache: miss on empty cache");

    // Save [osm] then load — must round-trip taxiway count.
    var toSave = new List<AirportTaxiData> { osm };
    cache.Save("OMDB", toSave);
    Check(cache.TryLoad("OMDB", out var got) == true
          && got != null
          && got.Count == 1
          && got[0].Taxiways.Count == osm.Taxiways.Count,
          $"Cache: round-trips taxiway count (expected {osm.Taxiways.Count}, got {got?[0].Taxiways.Count})");

    // TTL=0 should produce a miss (everything is already stale).
    var cacheDir2 = Path.Combine(Path.GetTempPath(), "taxiaug_test_" + Guid.NewGuid());
    var cacheExpired = new TaxiDataCache(cacheDir2, ttlDays: 0);
    cacheExpired.Save("OMDB", toSave);
    Check(cacheExpired.TryLoad("OMDB", out _) == false, "Cache: expired TTL (0 days) produces miss");

    // Clean up temp dirs.
    try { Directory.Delete(cacheDir, recursive: true); } catch { }
    try { Directory.Delete(cacheDir2, recursive: true); } catch { }
}

// ──────────────────────────────────────────────────────────────────────
// NormalizeTaxiwayName assertions (Feature A)
// ──────────────────────────────────────────────────────────────────────
Check(TaxiDataMerger.NormalizeTaxiwayName("TWY K2") == "K2",
      "Normalize: 'TWY K2' → 'K2'");
Check(TaxiDataMerger.NormalizeTaxiwayName("k 2") == "K2",
      "Normalize: 'k 2' → 'K2'");
Check(TaxiDataMerger.NormalizeTaxiwayName("TAXIWAY K2") == "K2",
      "Normalize: 'TAXIWAY K2' → 'K2'");
Check(TaxiDataMerger.NormalizeTaxiwayName("K2") == "K2",
      "Normalize: 'K2' → 'K2' (no change)");
Check(TaxiDataMerger.NormalizeTaxiwayName("") == "",
      "Normalize: empty → empty");

// ──────────────────────────────────────────────────────────────────────
// Alias resolution assertions (Feature B, merger layer)
// Synthetic: navdata segment named "HAWKER" at OMDB K-taxiway coords.
// OSM and apt.dat both name the same geometry "K".
// Expected: "K" lands in Aliases; navdata name "HAWKER" is not overwritten.
// ──────────────────────────────────────────────────────────────────────
{
    var hawkerNav = new List<NavSegment>
    {
        // Named navdata segment at the real OMDB K-taxiway coordinates.
        // Navdata calls it "HAWKER"; OSM/apt.dat call it "K".
        new NavSegment("HAWKER", 25.2354034, 55.3921787, 25.2352965, 55.3923703),
    };

    var hawkerOpt = new MergeOptions(); // defaults: 30 m / 25°
    var hawkerSources = new List<AirportTaxiData> { osm, apt };
    var hawkerMerged = TaxiDataMerger.MergeNamesOntoNavData(
        hawkerNav, hawkerSources, hawkerOpt, "OMDB-HAWKER", out _);

    Check(hawkerMerged.Count == 1,
          "Alias: merged output has 1 segment");
    Check(hawkerMerged[0].Name == "HAWKER",
          $"Alias: navdata name 'HAWKER' is not overwritten (got '{hawkerMerged[0].Name}')");
    Check(hawkerMerged[0].Aliases.Contains("K"),
          $"Alias: 'K' is in Aliases (got [{string.Join(", ", hawkerMerged[0].Aliases)}])");
    // The normalized alias "K" differs from the normalized canonical "HAWKER"
    // — that is why it qualifies as an alias at all.
    Check(TaxiDataMerger.NormalizeTaxiwayName("K")
              != TaxiDataMerger.NormalizeTaxiwayName("HAWKER"),
          "Alias: normalized 'K' != normalized 'HAWKER' (alias is meaningful)");
}

Console.WriteLine(failures==0 ? "ALL PASS" : $"{failures} FAILURES");
return failures==0 ? 0 : 1;
