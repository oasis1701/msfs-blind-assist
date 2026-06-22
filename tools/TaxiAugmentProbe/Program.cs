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

Console.WriteLine(failures==0 ? "ALL PASS" : $"{failures} FAILURES");
return failures==0 ? 0 : 1;
