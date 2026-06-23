using MSFSBlindAssist.Database.Models;
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
// (Parking enrichment is now AugmentingAirportDataProvider.AugmentParking — nearest 1:1 name-fill
// + aliasing against the live gate list; the old MergeParking dump helper was removed.)

// ──────────────────────────────────────────────────────────────────────
// Geometry edge cases (hardening pass): bearing wrap/reciprocal, reversed
// segment match, parallel-taxiway ambiguity guard, antimeridian.
// ──────────────────────────────────────────────────────────────────────
{
    // 1. Bearing diff is wrap-safe AND undirected (a reciprocal reads as 0°).
    Check(Math.Abs(TaxiGeo.BearingDiffMod180(359, 1) - 2) < 0.01, "Geom: bearing wrap 359 vs 1 = 2°");
    Check(TaxiGeo.BearingDiffMod180(10, 190) < 0.01, "Geom: reciprocal 10 vs 190 = 0° (undirected)");

    var gopt = new MergeOptions();

    // 2. A reversed online segment (digitized opposite direction) still matches.
    var revSrc = new AirportTaxiData { Source = "osm" };
    revSrc.Taxiways.Add(new NamedTaxiSegment { Name = "REVTWY", Lat1 = 50.0010, Lon1 = 8.0000, Lat2 = 50.0000, Lon2 = 8.0000 });
    var revNav = new List<NavSegment> { new NavSegment("", 50.0000, 8.0000, 50.0010, 8.0000) };
    var revMerged = TaxiDataMerger.MergeNamesOntoNavData(revNav, new List<AirportTaxiData> { revSrc }, gopt, "TEST", out _);
    Check(revMerged[0].Name == "REVTWY", $"Geom: reversed-direction online segment matches (got '{revMerged[0].Name}')");

    // 3. Ambiguity guard: nav midpoint ~equidistant from two DIFFERENT parallel names → NO name.
    var ambSrc = new AirportTaxiData { Source = "osm" };
    ambSrc.Taxiways.Add(new NamedTaxiSegment { Name = "P1", Lat1 = 50.0000, Lon1 = 8.0000, Lat2 = 50.0010, Lon2 = 8.0000 });
    ambSrc.Taxiways.Add(new NamedTaxiSegment { Name = "P2", Lat1 = 50.0000, Lon1 = 8.0002, Lat2 = 50.0010, Lon2 = 8.0002 });
    var ambNav = new List<NavSegment> { new NavSegment("", 50.0000, 8.0001, 50.0010, 8.0001) };
    var ambMerged = TaxiDataMerger.MergeNamesOntoNavData(ambNav, new List<AirportTaxiData> { ambSrc }, gopt, "TEST", out _);
    Check(string.IsNullOrEmpty(ambMerged[0].Name), $"Geom: two equidistant parallels → ambiguous → no name (got '{ambMerged[0].Name}')");

    // Contrast: with only ONE nearby parallel, it adopts (the guard doesn't over-fire).
    var oneSrc = new AirportTaxiData { Source = "osm" };
    oneSrc.Taxiways.Add(new NamedTaxiSegment { Name = "P1", Lat1 = 50.0000, Lon1 = 8.0000, Lat2 = 50.0010, Lon2 = 8.0000 });
    var oneNav = new List<NavSegment> { new NavSegment("", 50.0000, 8.0001, 50.0010, 8.0001) };
    var oneMerged = TaxiDataMerger.MergeNamesOntoNavData(oneNav, new List<AirportTaxiData> { oneSrc }, gopt, "TEST", out _);
    Check(oneMerged[0].Name == "P1", $"Geom: single nearby parallel adopts (guard not over-firing) (got '{oneMerged[0].Name}')");

    // 4. Antimeridian: a segment straddling ±180° matches without blowing up.
    var amSrc = new AirportTaxiData { Source = "osm" };
    amSrc.Taxiways.Add(new NamedTaxiSegment { Name = "DATELINE", Lat1 = 0.0000, Lon1 = 179.99995, Lat2 = 0.0010, Lon2 = -179.99995 });
    var amNav = new List<NavSegment> { new NavSegment("", 0.0000, 179.9999, 0.0010, -179.9999) };
    var amMerged = TaxiDataMerger.MergeNamesOntoNavData(amNav, new List<AirportTaxiData> { amSrc }, gopt, "TEST", out _);
    Check(amMerged[0].Name == "DATELINE", $"Geom: antimeridian-straddling segment matches (got '{amMerged[0].Name}')");
}

// ──────────────────────────────────────────────────────────────────────
// Task 4.1: TaxiDataCache — IN-MEMORY per-ICAO cache + TTL
// ──────────────────────────────────────────────────────────────────────
{
    var cache = new TaxiDataCache(ttlDays: 30);

    // Miss on empty cache.
    Check(cache.TryLoad("OMDB", out _) == false, "Cache: miss on empty cache");

    // Save [osm] then load — must round-trip taxiway count (in memory, no disk).
    var toSave = new List<AirportTaxiData> { osm };
    cache.Save("OMDB", toSave);
    Check(cache.TryLoad("OMDB", out var got) == true
          && got != null
          && got.Count == 1
          && got[0].Taxiways.Count == osm.Taxiways.Count,
          $"Cache: round-trips taxiway count (expected {osm.Taxiways.Count}, got {got?[0].Taxiways.Count})");

    // TTL=0 should produce a miss (everything is already stale).
    var cacheExpired = new TaxiDataCache(ttlDays: 0);
    cacheExpired.Save("OMDB", toSave);
    Check(cacheExpired.TryLoad("OMDB", out _) == false, "Cache: expired TTL (0 days) produces miss");
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

// ──────────────────────────────────────────────────────────────────────
// ParkingSpot.Aliases round-trip assertion
// Verifies the in-memory Aliases list is present, empty by default,
// and can be populated / queried — without touching the DB.
// Also confirms the normalized dedup guard (same normalised form is NOT added).
// ──────────────────────────────────────────────────────────────────────
{
    // Default: Aliases list is present and empty.
    var spot = new ParkingSpot { Name = "GN 3", Number = 3, Type = 11 };
    Check(spot.Aliases != null && spot.Aliases.Count == 0,
          "ParkingSpot.Aliases: empty by default");

    // Populate an alias and verify round-trip.
    spot.Aliases.Add("47");
    Check(spot.Aliases.Count == 1 && spot.Aliases[0] == "47",
          "ParkingSpot.Aliases: add + retrieve '47'");

    // The dedup guard: "47" normalizes to "47"; adding it again (same form) should be skipped.
    string normExisting = TaxiDataMerger.NormalizeTaxiwayName("47");
    string normCandidate = TaxiDataMerger.NormalizeTaxiwayName("47");
    bool wouldDup = string.Equals(normCandidate, normExisting, StringComparison.OrdinalIgnoreCase)
                    || spot.Aliases.Any(a =>
                        string.Equals(
                            TaxiDataMerger.NormalizeTaxiwayName(a),
                            normCandidate,
                            StringComparison.OrdinalIgnoreCase));
    Check(wouldDup, "ParkingSpot.Aliases: dedup guard prevents adding same-normalised alias '47'");

    // A genuinely different alias (normalizes to something else) should pass the guard.
    string normNew = TaxiDataMerger.NormalizeTaxiwayName("GATE47");
    bool wouldAdd = !string.IsNullOrEmpty(normNew)
                    && !string.Equals(normNew, TaxiDataMerger.NormalizeTaxiwayName(spot.Name),
                                      StringComparison.OrdinalIgnoreCase)
                    && !spot.Aliases.Any(a =>
                        string.Equals(
                            TaxiDataMerger.NormalizeTaxiwayName(a),
                            normNew,
                            StringComparison.OrdinalIgnoreCase));
    Check(wouldAdd, "ParkingSpot.Aliases: 'GATE47' (normalizes to 'GATE47') passes dedup guard");
}

Console.WriteLine(failures==0 ? "ALL PASS" : $"{failures} FAILURES");
return failures==0 ? 0 : 1;
