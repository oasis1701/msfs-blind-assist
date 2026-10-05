using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ EVERY FAILURE THE AEROPLANE'S OWN RANDOM PICKER CAN RAISE HAS A ROW, OR A WRITTEN REASON.
///
/// COWS's Failures.xml carries the random-failure picker: one block per number the dice can
/// land on, each writing the failure it stands for. That list IS the aeroplane's failure set
/// — a failure the picker can raise and MSFSBA has no row for is one that happens to a blind
/// pilot in silence (the indication failures were exactly that after COWS 1.2.0 renamed
/// them FAILURES_SENS_*: the rows still wrote FAILURES_DISP_*, which the model now rewrites
/// every frame). So this reads the picker out of the INSTALLED package, both airframes, and
/// fails on any written failure no variable binds — and, the other way round, on any
/// exclusion that the picker no longer writes, so the reasons below cannot go stale.
/// Skips on a machine without the aircraft.
/// </summary>
public class CowsDA40FailureCoverageTests
{
    /// <summary>Picked by the dice but deliberately not offered, each with its reason.</summary>
    private static readonly Dictionary<string, string> NgNotOffered = new(StringComparer.Ordinal)
    {
        // Inert on the NG: written by the picker and read by nothing (NoBreakerTripRowIsInert).
        ["FAILURES_CB_ALT"] = "inert on the NG",
        ["FAILURES_CB_ESS_TIE"] = "inert on the NG",
        ["FAILURES_CB_MAIN_TIE"] = "inert on the NG",
        // ⚠️ A COWS INCONSISTENCY, NOT A GAP: the picker writes the INDEXED name, but the
        // electrical logic reads the bare FAILURES_ALT — which DA40_FAIL_ALT binds, so the
        // row works and the random pick does not.
        ["FAILURES_ALT:1"] = "COWS bug: picker writes :1, the alternator logic reads FAILURES_ALT",
    };

    private static readonly Dictionary<string, string> XlsNotOffered = new(StringComparer.Ordinal)
    {
        ["FAILURES_CB_ADF"] = "inert on the XLS",
    };

    private static readonly Regex PickBlock = new(@"\(L:FAILURES_RNG\)\s+(\d+)\s+==");

    private static IReadOnlyCollection<string> PickerWrites(string root, DA40Variant v)
    {
        string dir = Path.Combine(root, "SimObjects", "Airplanes", v == DA40Variant.NG ? "COWS_DA40NG" : "COWS_DA40XLS");
        string file = Directory.EnumerateFiles(dir, "*Failures.xml", SearchOption.AllDirectories).First();
        string xml = Regex.Replace(File.ReadAllText(file), "<!--.*?-->", "", RegexOptions.Singleline);

        var writes = new HashSet<string>(StringComparer.Ordinal);
        var parts = PickBlock.Split(xml);
        for (int i = 2; i < parts.Length; i += 2)
        {
            // The block's own writes, up to the next pick — the same slice a reader of the
            // file would take as "what this number does".
            foreach (Match m in Regex.Matches(parts[i], @"\(&gt;L:([A-Z0-9_:]+)\)"))
            {
                string n = m.Groups[1].Value;
                if (n.StartsWith("FAILURES_", StringComparison.Ordinal) || n.StartsWith("FADEC_ECU_FAIL_TIME", StringComparison.Ordinal))
                    if (n != "FAILURES_TIMER" && n != "FAILURES_RNG") writes.Add(n);
            }
        }
        return writes;
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryPickableFailureHasARowOrAReason(DA40Variant variant)
    {
        string? root = CowsDA40PackagePresenceTests.PackageRoot();
        if (root is null) return;

        var writes = PickerWrites(root, variant);
        Assert.NotEmpty(writes);

        var bound = new CowsDA40Definition(variant).GetVariables().Values
            .Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
        var excluded = variant == DA40Variant.NG ? NgNotOffered : XlsNotOffered;

        var missing = writes.Where(w => !bound.Contains(w) && !excluded.ContainsKey(w)).OrderBy(w => w).ToList();
        Assert.True(missing.Count == 0,
            variant + ": the aeroplane can raise these and nothing reports them — " + string.Join(", ", missing));

        var stale = excluded.Keys.Where(k => !writes.Contains(k) || bound.Contains(k)).ToList();
        Assert.True(stale.Count == 0,
            variant + ": exclusions the picker no longer writes, or that a row binds after all — " + string.Join(", ", stale));
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void NoFailureRowWritesTheDerivedIndicationCopy(DA40Variant variant)
    {
        // COWS 1.2.0: FAILURES_DISP_* is rewritten every frame from FAILURES_SENS_*, so a row
        // writing DISP is overwritten within a frame and does nothing.
        var defs = new CowsDA40Definition(variant).GetVariables();
        Assert.DoesNotContain(defs, kv => kv.Key.StartsWith("DA40_FAIL", StringComparison.Ordinal)
                                          && kv.Value.Name.StartsWith("FAILURES_DISP_", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryFailurePanelSaysWhetherFailuresAreLive(DA40Variant variant)
    {
        // With the MFD's Failures Mode Off, COWS 1.2.0 multiplies every failure by
        // FAILURES_ON = 0: a row can be set, reads back set, and does nothing.
        var def = new CowsDA40Definition(variant);
        var display = def.GetPanelDisplayVariables();
        foreach (string panel in def.GetPanelStructure()["Simulation"]
                     .Where(p => p != "Engine Variation" && p != "Aircraft Options" && p != "Reset"))
        {
            Assert.True(display.TryGetValue(panel, out var rows) && rows.Contains("DA40_SIM_FAILURES_ACTIVE"),
                variant + " " + panel + " does not say whether failures are live");
        }
        Assert.Equal("FAILURES_ON", def.GetVariables()["DA40_SIM_FAILURES_ACTIVE"].Name);
    }
}
