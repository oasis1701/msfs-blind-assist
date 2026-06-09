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
