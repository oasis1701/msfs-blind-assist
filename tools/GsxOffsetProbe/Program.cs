using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MSFSBlindAssist.Services.Gsx;

// GsxOffsetProbe — verifies the GSX .py per-aircraft stop-offset evaluator against the
// REAL installed GSX profiles. Run:
//   dotnet run --project tools/GsxOffsetProbe -p:Platform=x64        (golden + unit asserts)
//   dotnet run --project tools/GsxOffsetProbe -p:Platform=x64 -- --sweep   (all-profiles guard)

string gsxDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "Virtuali", "GSX", "MSFS");

int failures = 0;

void Assert(bool cond, string label)
{
    if (cond) { Console.WriteLine($"  PASS  {label}"); }
    else { Console.WriteLine($"  FAIL  {label}"); failures++; }
}

void AssertNear(double actual, double expected, double tol, string label)
    => Assert(Math.Abs(actual - expected) <= tol,
        $"{label}  (got {actual.ToString("0.###", CultureInfo.InvariantCulture)}, " +
        $"expected {expected.ToString("0.###", CultureInfo.InvariantCulture)})");

bool sweep = args.Contains("--sweep");

if (sweep)
{
    RunSweep(gsxDir);
    return failures == 0 ? 0 : 1;
}

// ---------------------------------------------------------------------------
// 1) GOLDEN: EDDF gate 66 (no suffix), B77W (777/300) -> longitudinal 5.3 m.
// ---------------------------------------------------------------------------
Console.WriteLine("== GOLDEN: EDDF A66 / B77W (777-300) ==");
string eddfPath = Path.Combine(gsxDir, "eddf-Aerosoft.py");
Assert(File.Exists(eddfPath), $"EDDF profile present at {eddfPath}");

var eddf = GsxPyProfileReader.Load(eddfPath);

Assert(GsxAircraftIdMap.TryResolve("B77W", out var b77w), "resolve B77W");
Assert(b77w.IdMajor == 777 && b77w.IdMinor == 300, $"B77W -> idMajor=777 idMinor=300 (got {b77w.IdMajor}/{b77w.IdMinor})");

Assert(eddf.TryGetOffsetFunctionName(66, "", out string f66), "gate 66 has a function");
Assert(f66 == "customOffsetA54A58A62A66", $"gate 66 -> customOffsetA54A58A62A66 (got '{f66}')");

var off66 = GsxPyOffsetEvaluator.Evaluate(eddf, 66, "", b77w);
AssertNear(off66.LongitudinalMetres, 5.3, 0.01, "EDDF 66 B77W longitudinal == 5.3 m");
AssertNear(off66.LateralMetres, 0.0, 0.01, "EDDF 66 B77W lateral == 0 m");

// ---------------------------------------------------------------------------
// 2) Suffix dispatch: 66 vs 66A vs 66B map to different functions.
// ---------------------------------------------------------------------------
Console.WriteLine("== EDDF gate 66 suffix dispatch ==");
Assert(eddf.TryGetOffsetFunctionName(66, "A", out string f66a), "gate 66A has a function");
Assert(f66a == "customOffsetA54586266A", $"66A -> customOffsetA54586266A (got '{f66a}')");
Assert(f66a != f66, "66A function differs from 66");

Assert(eddf.TryGetOffsetFunctionName(66, "B", out string f66b), "gate 66B has a function");
Assert(f66b == "customOffsetA66B", $"66B -> customOffsetA66B (got '{f66b}')");
Assert(f66b != f66 && f66b != f66a, "66B function differs from 66 and 66A");

// ---------------------------------------------------------------------------
// 3) Table parsing internals for customOffsetA54A58A62A66.
//    table777 = {0:1.65, 300:5.3}; generic table has 380:6.3.
// ---------------------------------------------------------------------------
Console.WriteLine("== EDDF customOffsetA54A58A62A66 table parsing ==");
var fnA66 = eddf.GetFunction("customOffsetA54A58A62A66");
Assert(fnA66.Kind == GsxPyProfileReader.IdiomKind.HandleAircraftOffsets,
    $"idiom == HandleAircraftOffsets (got {fnA66.Kind})");
Assert(fnA66.SpecificTables.TryGetValue(777, out var spec777), "specificTables has key 777");
if (fnA66.SpecificTables.TryGetValue(777, out spec777))
{
    AssertNear(spec777.SubTable[300].Longitudinal, 5.3, 0.001, "table777[300] == 5.3");
    AssertNear(spec777.SubTable[0].Longitudinal, 1.65, 0.001, "table777[0] == 1.65");
}
Assert(fnA66.GenericTable.ContainsKey(380), "generic table has key 380");
if (fnA66.GenericTable.TryGetValue(380, out var g380))
    AssertNear(g380.Longitudinal, 6.3, 0.001, "generic table[380] == 6.3");

// Cross-check: A388 (380) -> generic table 380 -> 6.3 (not in specificTables).
Assert(GsxAircraftIdMap.TryResolve("A388", out var a388), "resolve A388");
var offA388 = GsxPyOffsetEvaluator.Evaluate(eddf, 66, "", a388);
AssertNear(offA388.LongitudinalMetres, 6.3, 0.01, "EDDF 66 A388 -> 6.3 m (generic 380)");

// ---------------------------------------------------------------------------
// 3b) FULL FLEET at EDDF A66 — the 6 aircraft the user actually flies. Pure-math,
//     no sim load needed. Expected values read straight from customOffsetA54A58A62A66:
//       generic table {0:0, 380:6.3}; table777 {0:1.65, 300:5.3}; table787 {0:0, 9:1.65, 10:5.3}.
// ---------------------------------------------------------------------------
Console.WriteLine("== EDDF A66: user's fleet (per-aircraft, no sim needed) ==");
(string Icao, double Exp, string Note)[] fleet =
{
    ("B77W", 5.3,  "PMDG 777-300ER  (777/300 -> table777[300])"),
    ("B77L", 1.65, "PMDG 777F       (777/200 -> table777 fallback[0])"),
    ("B789", 1.65, "HS787-9         (787/9   -> table787[9])"),
    ("A388", 6.3,  "FBW A380X       (380     -> generic[380])"),
    ("A20N", 0.0,  "FBW A32NX       (320     -> base, not in table)"),
    ("A320", 0.0,  "Fenix A320      (320     -> base, not in table)"),
};
foreach (var (icao, exp, note) in fleet)
{
    GsxAircraftIdMap.TryResolve(icao, out var ac);
    var o = GsxPyOffsetEvaluator.Evaluate(eddf, 66, "", ac);
    AssertNear(o.LongitudinalMetres, exp, 0.01, $"EDDF 66 {icao,-4} -> {exp} m  {note}");
}

// ---------------------------------------------------------------------------
// 3c) UNIVERSAL DERIVER — never-seen designators still resolve sanely (Part 1).
//     The exception table is a thin list; the PRIMARY path is derivation from the
//     ICAO pattern + wingspan, so aircraft MSFSBA has never heard of still work.
// ---------------------------------------------------------------------------
Console.WriteLine("== Universal aircraft-id derivation (no hardcoding) ==");

// B77W derives to 777/300 even though it's NOT in the (now-thin) exception table.
GsxAircraftIdMap.TryResolve("B77W", 0, out var dB77W);
Assert(dB77W.IdMajor == 777 && dB77W.IdMinor == 300, $"B77W derives 777/300 (got {dB77W.IdMajor}/{dB77W.IdMinor})");

// A359 widebody: idMajor 350, idMinor 900-ish family (exception pins 1000 for GSX; assert >=900).
GsxAircraftIdMap.TryResolve("A359", 64.75, out var dA359);
Assert(dA359.IdMajor == 350, $"A359 -> idMajor 350 (got {dA359.IdMajor})");
Assert(dA359.IdMinor >= 900, $"A359 -> idMinor >= 900 (got {dA359.IdMinor})");

// Invented B79X: never-seen, but the B7X family pattern must derive idMajor 797 (707+9*10).
Assert(GsxAircraftIdMap.TryDeriveFromIcao("B79X", out int b79xMajor, out _), "B79X derives (family pattern)");
Assert(b79xMajor == 797, $"B79X -> idMajor 797 (707+9*10) (got {b79xMajor})");

// Invented widebody A37X: A3YZ widebody rule -> 300 + 7*10 = 370 (Y=7 is in the widebody set? no:
// our set is {3,4,5,6,8}; 7 is unknown -> idMajor 0). Assert the KNOWN families derive non-zero.
Assert(GsxAircraftIdMap.TryDeriveFromIcao("A332", out int a332Major, out int a332Minor), "A332 derives");
Assert(a332Major == 330 && a332Minor == 200, $"A332 -> 330/200 (got {a332Major}/{a332Minor})");
Assert(GsxAircraftIdMap.TryDeriveFromIcao("A320", out int a320Major, out _), "A320 derives");
Assert(a320Major == 320, $"A320 -> idMajor 320 (got {a320Major})");
Assert(GsxAircraftIdMap.TryDeriveFromIcao("E190", out int e190Major, out _), "E190 derives");
Assert(e190Major == 190, $"E190 -> idMajor 190 (got {e190Major})");

// Wingspan -> ARC code boundaries (metres).
Assert(GsxAircraftIdMap.ArcFromWingspanMetres(64.8) == "ARC-E", $"64.8 m -> ARC-E (got '{GsxAircraftIdMap.ArcFromWingspanMetres(64.8)}')");
Assert(GsxAircraftIdMap.ArcFromWingspanMetres(79.75) == "ARC-F", $"79.75 m -> ARC-F (got '{GsxAircraftIdMap.ArcFromWingspanMetres(79.75)}')");
Assert(GsxAircraftIdMap.ArcFromWingspanMetres(34.0) == "ARC-C", $"34 m -> ARC-C (got '{GsxAircraftIdMap.ArcFromWingspanMetres(34.0)}')");
Assert(GsxAircraftIdMap.ArcFromWingspanMetres(80.5) == "ARC-F", $"80.5 m (>=80) -> ARC-F (got '{GsxAircraftIdMap.ArcFromWingspanMetres(80.5)}')");

// Resolve with wingspan populates ArcCode + Group; resolve without leaves them empty.
GsxAircraftIdMap.TryResolve("B77W", 64.8, out var b77wWs);
Assert(b77wWs.ArcCode == "ARC-E", $"B77W @64.8m -> ARC-E (got '{b77wWs.ArcCode}')");
Assert(!string.IsNullOrEmpty(b77wWs.Group), "B77W @64.8m -> non-empty group");
GsxAircraftIdMap.TryResolve("B77W", 0, out var b77wNoWs);
Assert(b77wNoWs.ArcCode.Length == 0, "B77W @0 wingspan -> empty ARC (group is last-resort only)");

// ---------------------------------------------------------------------------
// 4) ICAO-style profile (SKBO): real gates, sane evaluation.
// ---------------------------------------------------------------------------
Console.WriteLine("== SKBO ICAOAircraftOffsets gates ==");
string skboPath = Directory.Exists(gsxDir)
    ? Directory.GetFiles(gsxDir, "SKBO*.py").FirstOrDefault() ?? ""
    : "";
if (File.Exists(skboPath))
{
    var skbo = GsxPyProfileReader.Load(skboPath);

    // Gate 29: TableIcao["B77W"] = 8.1 (ICAO hit precedes idMajor/group).
    Assert(skbo.TryGetOffsetFunctionName(29, "", out string s29), "SKBO gate 29 has a function");
    Assert(s29 == "Gate_29", $"SKBO 29 -> Gate_29 (got '{s29}')");
    var sfn29 = skbo.GetFunction("Gate_29");
    Assert(sfn29.Kind == GsxPyProfileReader.IdiomKind.IcaoAircraftOffsets,
        $"SKBO Gate_29 idiom == IcaoAircraftOffsets (got {sfn29.Kind})");
    var s29off = GsxPyOffsetEvaluator.Evaluate(skbo, 29, "", b77w);
    AssertNear(s29off.LongitudinalMetres, 8.1, 0.01, "SKBO 29 B77W -> 8.1 m (TableIcao B77W)");

    // Gate 29 with A320 (320/0): no ICAO/aircraftValues entry for 320 except 0:0; group "Medium"
    // isn't in ARC-* TableGroup, so falls to base 0.
    Assert(GsxAircraftIdMap.TryResolve("A320", out var a320), "resolve A320");
    var s29a320 = GsxPyOffsetEvaluator.Evaluate(skbo, 29, "", a320);
    AssertNear(s29a320.LongitudinalMetres, 0.0, 0.01, "SKBO 29 A320 -> 0 m (base)");

    // Gate 34/35: tuple value. TableIcao["B77W"] = (13.6, -3.5).
    Assert(skbo.TryGetOffsetFunctionName(34, "", out string s34), "SKBO gate 34 has a function");
    var s34off = GsxPyOffsetEvaluator.Evaluate(skbo, 34, "", b77w);
    AssertNear(s34off.LongitudinalMetres, 13.6, 0.01, "SKBO 34 B77W longitudinal == 13.6 m");
    AssertNear(s34off.LateralMetres, -3.5, 0.01, "SKBO 34 B77W lateral == -3.5 m");
}
else
{
    Console.WriteLine("  SKIP  SKBO profile not found");
}

// ---------------------------------------------------------------------------
// 5) Plain ICAO-only table idiom: TableIcao.get(icaoTypeDesignator, 0).
// ---------------------------------------------------------------------------
Console.WriteLine("== ICAO-only TableIcao.get idiom (ELLX) ==");
string ellxPath = Directory.Exists(gsxDir)
    ? Directory.GetFiles(gsxDir, "ellx*.py").FirstOrDefault() ?? ""
    : "";
if (File.Exists(ellxPath))
{
    var ellx = GsxPyProfileReader.Load(ellxPath);
    var fnT1 = ellx.GetFunction("customOffsetType1");
    Assert(fnT1.Kind == GsxPyProfileReader.IdiomKind.IcaoAircraftOffsets,
        $"ELLX customOffsetType1 idiom == IcaoAircraftOffsets (got {fnT1.Kind})");
    var t1 = GsxPyOffsetEvaluator.Evaluate(fnT1, b77w);
    AssertNear(t1.LongitudinalMetres, 9.6, 0.01, "ELLX customOffsetType1 B77W -> 9.6 m (ICAO table)");
}
else
{
    Console.WriteLine("  SKIP  ELLX profile not found");
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;

// ===========================================================================
void RunSweep(string dir)
{
    Console.WriteLine("== SWEEP: every installed .py profile ==");
    if (!Directory.Exists(dir))
    {
        Console.WriteLine($"  GSX dir not found: {dir}");
        failures++;
        return;
    }

    string[] testIcaos = { "B77W", "A320", "B738" };
    var acs = testIcaos.Select(i => { GsxAircraftIdMap.TryResolve(i, out var a); return a; }).ToArray();

    int profilesScanned = 0;
    int gatesEvaluated = 0;
    int unclassified = 0;
    int outOfBand = 0;
    var idiomCounts = new Dictionary<GsxPyProfileReader.IdiomKind, int>();
    var outOfBandSamples = new List<string>();
    var throwingProfiles = new List<string>();

    foreach (string path in Directory.GetFiles(dir, "*.py"))
    {
        GsxPyProfileReader profile;
        try { profile = GsxPyProfileReader.Load(path); }
        catch (Exception ex) { throwingProfiles.Add($"{Path.GetFileName(path)}: load {ex.GetType().Name}"); failures++; continue; }
        profilesScanned++;

        foreach (var kv in profile.GateMap)
        {
            // Split normalized key "66A" -> number 66, suffix "A".
            string key = kv.Key;
            int i = 0;
            while (i < key.Length && char.IsDigit(key[i])) i++;
            if (i == 0) continue;
            int number = int.Parse(key.Substring(0, i), CultureInfo.InvariantCulture);
            string suffix = key.Substring(i);

            // Classify once per function (the gate map points at it).
            var fn = profile.GetFunction(kv.Value);
            if (fn.Kind == GsxPyProfileReader.IdiomKind.Unclassified)
                unclassified++;
            idiomCounts[fn.Kind] = idiomCounts.GetValueOrDefault(fn.Kind) + 1;

            foreach (var ac in acs)
            {
                GsxOffset off;
                try
                {
                    off = GsxPyOffsetEvaluator.Evaluate(profile, number, suffix, ac);
                }
                catch (Exception ex)
                {
                    throwingProfiles.Add($"{Path.GetFileName(path)} gate {key} {ac.Icao}: {ex.GetType().Name}");
                    failures++;
                    continue;
                }
                gatesEvaluated++;

                if (Math.Abs(off.LongitudinalMetres) > 30 || Math.Abs(off.LateralMetres) > 30)
                {
                    outOfBand++;
                    if (outOfBandSamples.Count < 20)
                        outOfBandSamples.Add(
                            $"{Path.GetFileName(path)} gate {key} {ac.Icao} -> " +
                            $"({off.LongitudinalMetres:0.##}, {off.LateralMetres:0.##}) via {kv.Value}");
                }
            }
        }
    }

    Assert(throwingProfiles.Count == 0, "no profile/gate threw during evaluation");
    Assert(outOfBand == 0, "no offset exceeded |30| m");

    Console.WriteLine();
    Console.WriteLine("---- SWEEP SUMMARY ----");
    Console.WriteLine($"Profiles scanned : {profilesScanned}");
    Console.WriteLine($"Gates evaluated  : {gatesEvaluated}  (x{testIcaos.Length} aircraft)");
    Console.WriteLine($"Distinct gate-funcs unclassified (fell back to 0): {unclassified}");
    Console.WriteLine("Idiom distribution (per gate-map entry):");
    foreach (var kv in idiomCounts.OrderByDescending(k => k.Value))
        Console.WriteLine($"   {kv.Key,-24} {kv.Value}");

    if (outOfBandSamples.Count > 0)
    {
        Console.WriteLine($"Out-of-band (>|30| m) samples ({outOfBand} total):");
        foreach (var s in outOfBandSamples) Console.WriteLine($"   {s}");
    }
    if (throwingProfiles.Count > 0)
    {
        Console.WriteLine("Throwing profiles/gates:");
        foreach (var s in throwingProfiles.Take(20)) Console.WriteLine($"   {s}");
    }

    Console.WriteLine();
    Console.WriteLine(failures == 0 ? "SWEEP ALL PASS" : $"SWEEP {failures} FAILURE(S)");
}
