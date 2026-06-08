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
Console.WriteLine("ICAO -> door longitudinal offset (metres), door side, wingspan, sorted:");
foreach (var kv in map.OrderBy(x => x.Key))
{
    var geom = kv.Value;
    string spanStr = geom.WingspanMetres.HasValue ? $"{geom.WingspanMetres.Value:F2}m" : "n/a";
    Console.WriteLine($"  {kv.Key,-10} -> off {geom.DoorLongitudinalMetres:F2}m  side {geom.Side,-5}  span {spanStr}");
}

// ─── 3. Known-good assertions ────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("=== Known-good assertions (tolerance 0.01 m) ===");
int pass = 0, fail = 0;

void Assert(string icao, double expected)
{
    if (map.TryGetValue(icao, out var g) && Math.Abs(g.DoorLongitudinalMetres - expected) <= 0.01)
    {
        Console.WriteLine($"  PASS  {icao}: expected {expected:F2}, got {g.DoorLongitudinalMetres:F2}");
        pass++;
    }
    else
    {
        string got = map.TryGetValue(icao, out var v2) ? $"{v2.DoorLongitudinalMetres:F2}" : "(not found)";
        Console.WriteLine($"  FAIL  {icao}: expected {expected:F2}, got {got}");
        fail++;
    }
}

Assert("B77W", 25.93);   // World Traffic 777W (APPDATA profile)
Assert("A20N", 8.51);    // FBW A320 NEO
Assert("A388", 28.90);   // FBW A380

// ─── 4. Side + wingspan assertions ───────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("=== Side + wingspan assertions ===");

void AssertSide(string icao, DoorSide expectedSide)
{
    var geom = profile.GetGeometry(icao);
    if (geom.HasValue && geom.Value.Side == expectedSide)
    {
        Console.WriteLine($"  PASS  {icao} side: expected {expectedSide}, got {geom.Value.Side}");
        pass++;
    }
    else
    {
        string got = geom.HasValue ? geom.Value.Side.ToString() : "(not found)";
        Console.WriteLine($"  FAIL  {icao} side: expected {expectedSide}, got {got}");
        fail++;
    }
}

void AssertSpan(string icao, double expectedSpan, double tolerance)
{
    var geom = profile.GetGeometry(icao);
    if (geom.HasValue && geom.Value.WingspanMetres.HasValue && Math.Abs(geom.Value.WingspanMetres.Value - expectedSpan) <= tolerance)
    {
        Console.WriteLine($"  PASS  {icao} wingspan: expected ~{expectedSpan:F2}, got {geom.Value.WingspanMetres.Value:F2}");
        pass++;
    }
    else
    {
        string got = (geom.HasValue && geom.Value.WingspanMetres.HasValue) ? $"{geom.Value.WingspanMetres.Value:F2}" : "(not found or null)";
        Console.WriteLine($"  FAIL  {icao} wingspan: expected ~{expectedSpan:F2} ±{tolerance}, got {got}");
        fail++;
    }
}

AssertSide("B77W", DoorSide.Left);
AssertSpan("B77W", 64.48, 0.5);
AssertSpan("A20N", 31.40, 0.5);  // GSX wingtippos 15.70 * 2 = 31.40 m (real profile value)

Console.WriteLine();
Console.WriteLine($"Result: {pass} PASS, {fail} FAIL  (unit check was {(unitPass ? "PASS" : "FAIL")})");
if (!unitPass || fail > 0)
{
    Console.Error.WriteLine("One or more assertions failed.");
    Environment.Exit(1);
}
