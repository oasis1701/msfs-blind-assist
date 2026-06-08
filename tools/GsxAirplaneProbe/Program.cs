using MSFSBlindAssist.Services.Gsx;

// ─── 1. Unit-check ParseDoorOffset on a literal 777-style snippet ───────────
Console.WriteLine("=== Unit check: ParseDoorOffset (777-style snippet) ===");
var snippet = new[]
{
    "[aircraft]",
    "icaotype = B77W",
    "preferredexit =  1",
    "[exit1]",
    "pos = -2.72 25.93 1.01 11.00",
    "name = L Entry 1",
};
var (unitIcao, unitOffset) = GsxAirplaneProfile.ParseDoorOffset(snippet);
bool unitPass = unitIcao == "B77W" && unitOffset.HasValue && Math.Abs(unitOffset.Value - 25.93) < 0.01;
Console.WriteLine($"  icao={unitIcao}  offset={unitOffset}  => {(unitPass ? "PASS" : "FAIL")}");

// ─── 2. BuildMap from real folders ──────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("=== BuildMap (scanning real %APPDATA%\\Virtuali\\Airplanes + MSFS packages) ===");
var sw = System.Diagnostics.Stopwatch.StartNew();
var profile = new GsxAirplaneProfile();
var map = profile.BuildMap();
sw.Stop();
Console.WriteLine($"Scan completed in {sw.ElapsedMilliseconds} ms. Found {map.Count} ICAO entries.");
Console.WriteLine();
Console.WriteLine("ICAO -> door longitudinal offset (metres), sorted:");
foreach (var kv in map.OrderBy(x => x.Key))
    Console.WriteLine($"  {kv.Key,-10} -> {kv.Value:F2}");

// ─── 3. Known-good assertions ────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("=== Known-good assertions (tolerance 0.01 m) ===");
int pass = 0, fail = 0;

void Assert(string icao, double expected)
{
    if (map.TryGetValue(icao, out double actual) && Math.Abs(actual - expected) <= 0.01)
    {
        Console.WriteLine($"  PASS  {icao}: expected {expected:F2}, got {actual:F2}");
        pass++;
    }
    else
    {
        string got = map.TryGetValue(icao, out double v) ? $"{v:F2}" : "(not found)";
        Console.WriteLine($"  FAIL  {icao}: expected {expected:F2}, got {got}");
        fail++;
    }
}

Assert("B77W", 25.93);   // World Traffic 777W (APPDATA profile)
Assert("A20N", 8.51);    // FBW A320 NEO
Assert("A388", 28.90);   // FBW A380

Console.WriteLine();
Console.WriteLine($"Result: {pass} PASS, {fail} FAIL  (unit check was {(unitPass ? "PASS" : "FAIL")})");
if (!unitPass || fail > 0)
{
    Console.Error.WriteLine("One or more assertions failed.");
    Environment.Exit(1);
}
