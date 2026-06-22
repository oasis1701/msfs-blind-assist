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

Console.WriteLine(failures==0 ? "ALL PASS" : $"{failures} FAILURES");
return failures==0 ? 0 : 1;
