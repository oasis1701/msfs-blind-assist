using MSFSBlindAssist.Services.Gsx;

// GsxGateSelectProbe — pins the gate-leaf identity parsing + leaf-vs-target matching that
// GsxGateSelector's DFS relies on, against REAL menu labels captured in
// %LOCALAPPDATA%\MSFSBlindAssist\logs\gsx-gate-select.log (EDDF 2026-06-08,
// KATL 2026-06-09/10). Run:
//   dotnet run --project tools/GsxGateSelectProbe -p:Platform=x64
//
// The KATL block is the regression that motivated this probe: KATL's GSX profile lists
// every stand LETTERLESS ("Gate 55") with the concourse letter only in the category title
// ("Concourse C (C1-C55)"). The pre-2701f50 strict-equality matcher could never match
// target "C55" and the DFS exhausted the whole airport announcing "not found".

int failures = 0;
void Check(string n, bool ok) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {n}"); if (!ok) failures++; }

// Parses a live menu label and matches it against a target identity, returning whether
// the DFS would choose it (the same LooksLikeGate → LeafMatchesTarget pipeline).
bool Matches(string menuLabel, string targetIdentity, out bool bare)
{
    bare = false;
    if (!GsxMenuClassifier.LooksLikeGate(menuLabel, out string leaf)) return false;
    return GsxMenuClassifier.LeafMatchesTarget(leaf, targetIdentity, out bare);
}

// ── Identity extraction from real labels ───────────────────────────────────
{
    Check("KATL letterless leaf parses to bare number",
        GsxMenuClassifier.LooksLikeGate(" Gate 55 - Medium - 1x  /J ", out string id) && id == "55");
    Check("KATL 'WALK IN' leaf parses",
        GsxMenuClassifier.LooksLikeGate(" Gate 4 - Medium  - WALK IN, No Bus", out string id2) && id2 == "4");
    Check("EDDF lettered leaf parses with concourse letter",
        GsxMenuClassifier.LooksLikeGate(" Gate A11 with Safedock© - Medium - 1x  /J  (too small)", out string id3) && id3 == "A11");
    Check("OMDB suffixed stand parses",
        GsxMenuClassifier.LooksLikeGate("Stand C53R with Safedock© - Large", out string id4) && id4 == "C53R");
    Check("parenthetical count is never mistaken for a gate number",
        !GsxMenuClassifier.LooksLikeGate("Concourse C (C1-C55) \t(34 suitable parkings)", out _)
        || GsxMenuClassifier.IsCategory("Concourse C (C1-C55) \t(34 suitable parkings)"));
}

// ── KATL regression: letterless leaves vs lettered navdata targets ─────────
{
    Check("KATL C55: 'Gate 55' matches target C55 via bare-number fallback",
        Matches(" Gate 55 - Medium - 1x  /J ", "C55", out bool bare) && bare);
    Check("KATL F14: 'Gate 14' matches target F14 via bare-number fallback",
        Matches(" Gate 14 - Medium - 1x  /J ", "F14", out bool bare2) && bare2);
    Check("KATL: 'Gate 5' does NOT match target C55",
        !Matches(" Gate 5 - Medium - 1x  /J ", "C55", out _));
    Check("KATL: 'Gate 55' does NOT match target C5",
        !Matches(" Gate 55 - Medium - 1x  /J ", "C5", out _));
}

// ── EGLL: navdata-borrowed letter vs GSX letterless 'Parking 209' ──────────
{
    Check("EGLL P209: 'Parking 209' matches target P209 via bare-number fallback",
        Matches("Parking 209", "P209", out bool bare) && bare);
}

// ── Lettered leaves stay strict — no cross-concourse bare matching ─────────
{
    Check("EDDF: 'Gate A11' matches target A11 exactly (no fallback flag)",
        Matches(" Gate A11 with Safedock© - Medium - 1x  /J  (too small)", "A11", out bool bare) && !bare);
    Check("lettered leaf B11 never matches target A11",
        !Matches("Gate B11 - Medium", "A11", out _));
    Check("lettered leaf A11 never bare-matches target 11",
        !Matches("Gate A11 - Medium", "11", out _));
}

// ── Target identity normalisation (KATL navdata spots) ─────────────────────
{
    Check("target C/55 normalises to C55", GsxMenuClassifier.NormalizeTargetIdentity("C", 55, "") == "C55");
    Check("target GC/55 strips the navdata 'G' prefix", GsxMenuClassifier.NormalizeTargetIdentity("GC", 55, "") == "C55");
}

// ── Category ranking: the target's concourse must be drilled FIRST at KATL ──
// The bare-number fallback is only safe because the DFS enters the correct concourse
// before any sibling (every KATL concourse has a "Gate 1"). Pin that ordering.
{
    string[] katlCats =
    {
        "Concourse T (T1-T21) \t(21 suitable parkings)",
        "Concourse A (A1-A34) \t(29 suitable parkings)",
        "Concourse B (B1-B36) \t(32 suitable parkings)",
        "Concourse C (C1-C55) \t(34 suitable parkings)",
        "Concourse D (D1-D46) \t(40 suitable parkings)",
        "Concourse E (E1-E36) \t(30 suitable parkings)",
        "Concourse F (F1-F14) \t(12 suitable parkings)",
        "North Cargo Ramp (N1-84) \t(20 suitable parkings)",
    };
    foreach ((string target, int number, string wantCat) in new[]
             { ("C", 55, "Concourse C"), ("F", 14, "Concourse F"), ("T", 5, "Concourse T") })
    {
        string best = ""; int bestScore = int.MinValue;
        foreach (string cat in katlCats)
        {
            int s = GsxMenuClassifier.RankCategoryRelevance(cat, target, number);
            if (s > bestScore) { bestScore = s; best = cat; }
        }
        Check($"KATL ranking drills {wantCat} first for target {target}{number}",
            best.StartsWith(wantCat, StringComparison.Ordinal));
    }
}

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;

// ── Minimal stand-in for GsxService so the linked classifier compiles ───────
// The probe exercises only the pure string surface; MenuOption mirrors the real
// record shape in MSFSBlindAssist/Services/GsxService.cs.
namespace MSFSBlindAssist.Services
{
    public class GsxService
    {
        public sealed record MenuOption(string Key, string Text, int Choice);
    }
}
