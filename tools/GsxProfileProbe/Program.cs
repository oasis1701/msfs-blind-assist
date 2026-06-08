using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.Gsx;

// "--all": sweep EVERY installed profile to prove parse+map never throws and to
// aggregate anomalies. (Merge isn't exercised here — it needs navdata; see the
// hand-built matcher check below and the live verification in Task 12.)
if (args.Length > 0 && args[0] == "--all")
{
    string dir = GsxProfileLocator.DefaultProfileDir();
    var files = Directory.GetFiles(dir, "*.ini");
    int totalSections = 0, positionless = 0, fileErrors = 0;
    var vdgs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var cats = new SortedDictionary<string, int>();
    foreach (var f in files)
    {
        try
        {
            var gg = GsxProfileParser.Parse(f);
            totalSections += gg.Count;
            foreach (var g in gg)
            {
                if (!g.HasParkingPos && !g.StopLatitude.HasValue) positionless++;
                if (!string.IsNullOrWhiteSpace(g.VdgsType)) vdgs.Add(g.VdgsType);
                cats[g.Category] = cats.TryGetValue(g.Category, out int c) ? c + 1 : 1;
            }
            string stem = Path.GetFileNameWithoutExtension(f);
            string icaoG = (stem.Length >= 4 ? stem[..4] : stem).ToUpperInvariant();
            _ = GsxGateMapper.ToParkingSpots(gg, icaoG); // must not throw
        }
        catch (Exception ex)
        {
            fileErrors++;
            Console.WriteLine($"  PARSE ERROR in {Path.GetFileName(f)}: {ex.Message}");
        }
    }
    Console.WriteLine($"Swept {files.Length} profiles: {totalSections} sections, {positionless} positionless, {vdgs.Count} distinct VDGS, {fileErrors} file errors.");
    Console.WriteLine("Section categories: " + string.Join(", ", cats.Select(kv => $"{kv.Key}={kv.Value}")));
    return;
}

string defaultIni = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "Virtuali", "GSX", "MSFS", "omdb-24-iniBuilds.ini");

string ini = args.Length > 0 ? args[0] : defaultIni;
string icao = args.Length > 1 ? args[1] : "OMDB";

Console.WriteLine($"Profile: {ini}");
Console.WriteLine($"Exists:  {File.Exists(ini)}");

// Locator check
var locator = new GsxProfileLocator();
bool found = locator.TryFindProfile(icao, out string located);
Console.WriteLine($"Locator TryFindProfile({icao}) => {found} : {located}");

var gates = GsxProfileParser.Parse(ini);
var spots = GsxGateMapper.ToParkingSpots(gates, icao);
Console.WriteLine($"\nParsed {gates.Count} sections, {spots.Count} mapped spots.\n");

Console.WriteLine("First 15 spots:");
foreach (var s in spots.Take(15))
    Console.WriteLine($"  {s}  | hdg={s.Heading:F1} wing={s.MaxWingspanMeters} stop=({s.StopLatitude},{s.StopLongitude}) src={s.Source}");

Console.WriteLine("\nConcourse counts:");
foreach (var grp in spots.GroupBy(s => s.Name).OrderBy(g => g.Key))
    Console.WriteLine($"  {grp.Key}: {grp.Count()}");

Console.WriteLine("\nFilter checks:");
foreach (var q in new[] { "C", "C18", "18", "1L", "ZZZ" })
    Console.WriteLine($"  '{q}': {GateSearchFilter.Filter(spots, q).Count} matches");

// --- Matcher unit-check (hand-built, validates the GSX<->navdata overlay across naming schemes) ---
Console.WriteLine("\nMatcher checks (GSX gate with NO position must borrow the RIGHT navdata position):");
var navFix = new List<ParkingSpot>
{
    new() { Name = "GC", Number = 18,  Latitude = 25.111, Longitude = 55.111, Heading = 31 },  // OMDB "GC 18"
    new() { Name = "GD", Number = 18,  Latitude = 25.222, Longitude = 55.222, Heading = 40 },  // OMDB "GD 18"
    new() { Name = "P",  Number = 209, Latitude = 51.400, Longitude = -0.45,  Heading = 90 },  // EGLL "P 209"
    new() { Name = "G",  Number = 1,   Latitude = 37.460, Longitude = 126.4,  Heading = 180 }, // RKSI "G 1"
};
var gsxFix = new List<GsxGate>
{
    new() { Category="gate", Concourse="C", Number=18,  HasParkingPos=false }, // -> GC 18 (NOT GD)
    new() { Category="gate", Concourse="",  Number=209, HasParkingPos=false }, // EGLL pure-numeric -> P 209
    new() { Category="gate", Concourse="",  Number=1,   HasParkingPos=false }, // RKSI -> G 1
};
foreach (var m in GsxNavdataMerger.Merge(navFix, gsxFix, "TEST"))
    Console.WriteLine($"  {m} | lat={m.Latitude} src={m.Source}");
// Expect: "C 18" borrows 25.111 (the GC stand, NOT GD's 25.222) with Source=Gsx;
//         "P 209" lat 51.4, "G 1" lat 37.46 (matched by number); and "GD 18" appears
//         once as a navdata-only leftover (Source=Navdata).
