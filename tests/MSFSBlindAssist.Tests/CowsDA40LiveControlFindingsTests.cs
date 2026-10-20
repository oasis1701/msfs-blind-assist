using System.IO;
using System.Linq;
using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// What pressing every DA40 panel control through the real definition, live, turned up
/// (2026-10-05, both airframes, the control probe in docs/da40.md).
/// </summary>
public class CowsDA40LiveControlFindingsTests
{
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(System.AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new System.InvalidOperationException("MSFSBlindAssist.sln not found");
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void TheFmaIsAStatusTheModelWritesNotAnOption(DA40Variant variant)
    {
        // COWS_KILL_FMA is rewritten every frame from whether the autopilot computer is
        // alive; offered as a switch it was overwritten within a frame.
        var def = new CowsDA40Definition(variant);
        var v = def.GetVariables()["DA40_OPT_KILL_FMA"];
        Assert.True(v.RenderAsReadOnlyStatus);
        Assert.DoesNotContain("DA40_OPT_KILL_FMA", def.GetPanelControls()["Aircraft Options"]);
        Assert.Contains("DA40_OPT_KILL_FMA", def.GetPanelDisplayVariables()["GFC 700"]);
    }

    [Fact]
    public void EveryCacheLookupNamesACachedVariableKey()
    {
        // The cache is keyed by VARIABLE KEY and holds only Continuous + IsAnnounced
        // variables. The fuel valve looked up its wire by L:var name (never found, so the
        // valve always "wired to Main") and the doors read an OnRequest wind (always 0).
        var defs = new[] { new CowsDA40Definition(DA40Variant.NG), new CowsDA40Definition(DA40Variant.XLS) }
            .Select(d => d.GetVariables()).ToArray();
        var bad = new System.Collections.Generic.List<string>();
        foreach (string file in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Aircraft", "DA40"), "*.cs"))
        {
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                         File.ReadAllText(file), @"GetCachedVariableValue\(\s*""([^""]+)""\s*\)"))
            {
                string key = m.Groups[1].Value;
                var found = defs.Where(v => v.ContainsKey(key)).Select(v => v[key]).ToList();
                if (found.Count == 0) bad.Add($"{Path.GetFileName(file)}: {key} is not a variable key");
                else if (found.Any(d => d.UpdateFrequency != MSFSBlindAssist.SimConnect.UpdateFrequency.Continuous || !d.IsAnnounced))
                    bad.Add($"{Path.GetFileName(file)}: {key} is never cached");
            }
        }
        Assert.True(bad.Count == 0, string.Join("; ", bad));
    }

    [Fact]
    public void WithNoEcuRunningTheNgLoadRpmAndFlowReadAsTheG1000ShowsThem()
    {
        // The G1000 draws dashes for all three while neither ECU runs.
        var def = new CowsDA40Definition(DA40Variant.NG);
        var speech = new System.Collections.Generic.List<string>();
        def.ProcessSimVarUpdate("DA40_ECU_RUNNING_A", 0, null!);
        def.ProcessSimVarUpdate("DA40_ECU_RUNNING_B", 0, null!);
        Assert.True(def.TryGetDisplayOverride("DA40_POWER_LOAD", 0, out string dead));
        Assert.StartsWith("no reading", dead);

        def.ProcessSimVarUpdate("DA40_ECU_RUNNING_A", 1, null!);
        def.TryGetDisplayOverride("DA40_POWER_LOAD", 0, out string live);
        Assert.DoesNotContain("no reading", live);
    }

    [Fact]
    public void VerticalSpeedUsesTheEventsTheGfc700Intercepts()
    {
        // The Working Title GFC 700 state manager handles AP_VS_ON/OFF and has no case for
        // AP_VS_HOLD_ON/OFF, which left the autopilot in pitch hold.
        string src = File.ReadAllText(Path.Combine(RepoRoot(),
            "MSFSBlindAssist", "Aircraft", "DA40", "CowsDA40Definition.Autopilot.cs"));
        Assert.Contains("\"AP_VS_ON\" : \"AP_VS_OFF\"", src);
        Assert.DoesNotContain("\"AP_VS_HOLD_ON\"", src);
    }
}
