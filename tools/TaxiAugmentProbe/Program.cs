using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services;
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
// Bug-check pass fixes: TWY/TAXIWAY normalize, ambiguity epsilon at bestDist≈0,
// OSM name fallback (proper-name aliases), AssignParking 1:1.
// ──────────────────────────────────────────────────────────────────────
{
    // A1: "TWY"/"TAXIWAY" must NOT normalize to empty (would collapse distinct names → mis-name).
    Check(TaxiDataMerger.NormalizeTaxiwayName("TWY") == "TWY", "Norm: 'TWY' alone stays 'TWY' (not empty)");
    Check(TaxiDataMerger.NormalizeTaxiwayName("TAXIWAY") == "TAXIWAY", "Norm: 'TAXIWAY' alone stays (not empty)");
    Check(TaxiDataMerger.NormalizeTaxiwayName("TWY K2") == "K2", "Norm: 'TWY K2' -> 'K2'");
    Check(TaxiDataMerger.NormalizeTaxiwayName("Taxiway A") == "A", "Norm: 'Taxiway A' -> 'A'");

    var gopt2 = new MergeOptions();

    // A2: ambiguity guard must fire even when bestDist ≈ 0 (midpoint exactly on an online endpoint).
    var ambZ = new AirportTaxiData { Source = "osm" };
    ambZ.Taxiways.Add(new NamedTaxiSegment { Name = "X1", Lat1 = 50.0005, Lon1 = 8.0000,    Lat2 = 50.0015, Lon2 = 8.0000    }); // starts ON the midpoint → dist 0
    ambZ.Taxiways.Add(new NamedTaxiSegment { Name = "X2", Lat1 = 50.0005, Lon1 = 8.0000140, Lat2 = 50.0015, Lon2 = 8.0000140 }); // ~1 m away, different name
    var ambZNav = new List<NavSegment> { new NavSegment("", 50.0000, 8.0000, 50.0010, 8.0000) };
    var ambZM = TaxiDataMerger.MergeNamesOntoNavData(ambZNav, new List<AirportTaxiData> { ambZ }, gopt2, "TEST", out _);
    Check(string.IsNullOrEmpty(ambZM[0].Name), $"Guard: ambiguity fires at bestDist≈0 (~1 m competitor) → no name (got '{ambZM[0].Name}')");

    // P6: OSM taxiway with NO ref but a name → captured (enables proper-name aliases); ref still wins.
    var onName = OsmTaxiSource.Parse("{\"elements\":[{\"type\":\"way\",\"tags\":{\"aeroway\":\"taxiway\",\"name\":\"Neptune\"},\"geometry\":[{\"lat\":1.0,\"lon\":2.0},{\"lat\":1.001,\"lon\":2.0}]}]}");
    Check(onName.Taxiways.Any(t => t.Name == "Neptune"), "OSM: name-only taxiway 'Neptune' captured (name fallback)");
    var onRef = OsmTaxiSource.Parse("{\"elements\":[{\"type\":\"way\",\"tags\":{\"aeroway\":\"taxiway\",\"ref\":\"K\",\"name\":\"Kilo\"},\"geometry\":[{\"lat\":1.0,\"lon\":2.0},{\"lat\":1.001,\"lon\":2.0}]}]}");
    Check(onRef.Taxiways.All(t => t.Name == "K"), "OSM: ref wins over name when both present");
}

// Gate search must match BOTH the navdata identity AND the online alias — type EITHER name.
{
    var spot = new ParkingSpot { Name = "B", Number = 6, Aliases = new List<string> { "B04" } };
    Check(MSFSBlindAssist.Services.GateSearchFilter.Matches(spot, "B04"), "GateSearch: real-gate alias 'B04' matches");
    Check(MSFSBlindAssist.Services.GateSearchFilter.Matches(spot, "B 04"), "GateSearch: alias with space 'B 04' matches");
    Check(MSFSBlindAssist.Services.GateSearchFilter.Matches(spot, "B6"),  "GateSearch: navdata identity 'B6' matches");
    Check(MSFSBlindAssist.Services.GateSearchFilter.Matches(spot, "B"),   "GateSearch: concourse 'B' matches");
    Check(!MSFSBlindAssist.Services.GateSearchFilter.Matches(spot, "Z9"), "GateSearch: unrelated 'Z9' does NOT match");
    Check(MSFSBlindAssist.Services.GateSearchFilter.Filter(new List<ParkingSpot> { spot }, "B04").Count == 1,
          "GateSearch: Filter() returns the spot when searching its alias");
}

// NormalizeGateName: strip stand-type words so a redundant online name is NOT aliased (CYYZ).
{
    var ng = (System.Func<string, string>)MSFSBlindAssist.Services.GateSearchFilter.NormalizeGateName;
    Check(ng("Ramp H2") == "H2",      "GateName: 'Ramp H2' -> 'H2' (type word stripped)");
    Check(ng("Gate 5")  == "5",       "GateName: 'Gate 5' -> '5'");
    Check(ng("Stand 12 Apron") == "12", "GateName: 'Stand 12 Apron' -> '12' (multiple type words)");
    Check(ng("Ramp") == "",           "GateName: bare 'Ramp' -> '' (never aliased)");
    Check(ng("47") == "47",           "GateName: pure number passes through");
    Check(ng("GA 3") == "GA3",        "GateName: 'GA' concourse NOT stripped (GA3)");
    // The CYYZ bug: navdata "H 2" (identity H2) vs online "Ramp H2" must compare EQUAL → no alias.
    var hSpot = new ParkingSpot { Name = "H", Number = 2 };
    Check(MSFSBlindAssist.Services.GateSearchFilter.NormalizeIdentity(hSpot) == ng("Ramp H2"),
          "GateName: navdata 'H 2' identity == online 'Ramp H2' → redundant alias suppressed");
    // A genuinely different online name (e.g. "47" for navdata "GN 3") must still differ → kept.
    var gnSpot = new ParkingSpot { Name = "GN", Number = 3 };
    Check(MSFSBlindAssist.Services.GateSearchFilter.NormalizeIdentity(gnSpot) != ng("47"),
          "GateName: genuine alias '47' for 'GN 3' still differs → kept");
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

// ──────────────────────────────────────────────────────────────────────
// Task 1: StandId parser
// ──────────────────────────────────────────────────────────────────────
Check(StandId.Parse("Gate 11B") is { Letter: "", Number: 11, Suffix: "B", HasNumber: true }, "StandId: 'Gate 11B' -> (,11,B)");
Check(StandId.Parse("A51")      is { Letter: "A", Number: 51, HasNumber: true },              "StandId: 'A51' -> (A,51)");
Check(StandId.Parse("N 1")      is { Letter: "N", Number: 1,  HasNumber: true },              "StandId: 'N 1' -> (N,1)");
Check(StandId.Parse("51")       is { Letter: "", Number: 51, HasNumber: true },               "StandId: '51' -> (,51)");
Check(StandId.Parse("53A")      is { Letter: "", Number: 53, Suffix: "A", HasNumber: true },  "StandId: '53A' -> (,53,A)");
Check(StandId.Parse("F211")     is { Letter: "F", Number: 211, HasNumber: true },             "StandId: 'F211' -> (F,211)");
Check(StandId.Parse("P 209")    is { Letter: "P", Number: 209, HasNumber: true },             "StandId: 'P 209' -> (P,209)");
Check(StandId.Parse("").HasNumber == false,                                                   "StandId: '' -> no number");
Check(StandId.Parse("N") is { Letter: "N", HasNumber: false },                                "StandId: 'N' -> letter N, no number");
// Type-word stripping (shared with GateSearchFilter) — a 'Ramp'/'Tie Down' prefix is NOT a letter.
Check(StandId.Parse("Ramp 51")   is { Letter: "", Number: 51, HasNumber: true },              "StandId: 'Ramp 51' -> (,51) (RAMP stripped, not a letter)");
Check(StandId.Parse("Tie Down 5") is { Letter: "", Number: 5, HasNumber: true },              "StandId: 'Tie Down 5' -> (,5)");
Check(StandId.Parse("GA 5")      is { Letter: "GA", Number: 5, HasNumber: true },             "StandId: 'GA 5' -> (GA,5) ('GA' kept — real concourse)");
// And the resolver must NOT mint a junk 'RAMP51' alias for a 'Ramp 51' online stand on bare gate 51.
Check(GateAliasResolver.ResolveAliases(
        new ParkingSpot { Name = "", Number = 51, Type = 13, Latitude = 45, Longitude = -73 },
        new List<(string, double, double)> { ("Ramp 51", 45, -73) }, 0).Count == 0,
      "Resolver: 'Ramp 51' on bare gate 51 -> no junk alias (type-prefix restatement)");

// ──────────────────────────────────────────────────────────────────────
// Task 2: GateAliasResolver — identity-matched, alias-only, idempotent
// ──────────────────────────────────────────────────────────────────────
ParkingSpot Gate(string name, int num, int type = 11, string suffix = "")
    => new ParkingSpot { Name = name, Number = num, Type = type, Suffix = suffix, Latitude = 45.0, Longitude = -73.0 };

// Online stands (coords irrelevant here — distance check disabled with maxMeters: 0).
var onlineStands = new List<(string, double, double)>
{
    ("Gate 11B", 45.0, -73.0), ("Gate 15", 45.0, -73.0), ("51", 45.0, -73.0),
    ("A51", 45.0, -73.0), ("53A", 45.0, -73.0), ("S3", 45.0, -73.0), ("N3", 45.0, -73.0),
};

var a15 = GateAliasResolver.ResolveAliases(Gate("", 15), onlineStands, 0);
Check(a15.Count == 0, $"Resolver: gate 15 gets NO alias — '11B' rejected (number mismatch), '15' restatement (got [{string.Join(",", a15)}])");

var a51 = GateAliasResolver.ResolveAliases(Gate("", 51), onlineStands, 0);
Check(a51.Contains("A51"), $"Resolver: gate 51 aliases 'A51' (concourse prefix) (got [{string.Join(",", a51)}])");
Check(!a51.Contains("51"), "Resolver: gate 51 does NOT alias the bare '51' restatement");

var a53 = GateAliasResolver.ResolveAliases(Gate("", 53), onlineStands, 0);
Check(a53.Contains("53A"), $"Resolver: gate 53 aliases MARS '53A' (got [{string.Join(",", a53)}])");

var aN3 = GateAliasResolver.ResolveAliases(Gate("N", 3), onlineStands, 0);
Check(!aN3.Contains("S3"), "Resolver: gate 'N 3' never adopts 'S3' (letter disagreement)");
Check(aN3.Count == 0, $"Resolver: gate 'N 3' gets no alias ('N3' is a restatement) (got [{string.Join(",", aN3)}])");

var r1 = GateAliasResolver.ResolveAliases(Gate("", 51), onlineStands, 0);
var r2 = GateAliasResolver.ResolveAliases(Gate("", 51), onlineStands, 0);
Check(r1.SequenceEqual(r2), "Resolver: idempotent — two runs identical");

var farStands = new List<(string, double, double)> { ("A51", 46.0, -73.0) }; // ~111 km away
Check(GateAliasResolver.ResolveAliases(Gate("", 51), farStands, 150).Count == 0, "Resolver: same-number stand >150 m away rejected as data error");

// ──────────────────────────────────────────────────────────────────────
// Task 4: ParkingSpot display — 'Gate {n}' identity + '(online)' alias tag
// ──────────────────────────────────────────────────────────────────────
var disp51 = new ParkingSpot { Name = "", Number = 51, Type = 13 /*Gate Heavy*/, HasJetway = true };
Check(disp51.Describe().StartsWith("Gate 51"), $"Display: empty-name gate -> 'Gate 51' (got '{disp51.Describe()}')");
Check(!disp51.Describe().Contains("Spot"),      "Display: gate type never says 'Spot'");

var rampSpot = new ParkingSpot { Name = "", Number = 7, Type = 6 /*Ramp Cargo*/ };
Check(rampSpot.Describe().StartsWith("Spot 7"), $"Display: non-gate empty-name keeps 'Spot 7' (got '{rampSpot.Describe()}')");

disp51.Aliases.Add("A51");
Check(disp51.ToString().Contains("A51") && disp51.ToString().Contains("(online)"),
      $"Display: alias tagged '(online)' (got '{disp51.ToString()}')");

// ──────────────────────────────────────────────────────────────────────
// Task 5: GateSearchFilter — alias-aware
// ──────────────────────────────────────────────────────────────────────
var search51 = new ParkingSpot { Name = "", Number = 51, Type = 13 };
search51.Aliases.Add("A51");
Check(MSFSBlindAssist.Services.GateSearchFilter.Matches(search51, "A51"), "Search: 'A51' matches gate 51 via alias");
Check(MSFSBlindAssist.Services.GateSearchFilter.Matches(search51, "51"),  "Search: '51' matches gate 51 via identity");
Check(!MSFSBlindAssist.Services.GateSearchFilter.Matches(new ParkingSpot { Name = "", Number = 15, Type = 10 }, "11B"),
      "Search: '11B' does NOT match gate 15");

Console.WriteLine(failures==0 ? "ALL PASS" : $"{failures} FAILURES");
return failures==0 ? 0 : 1;
