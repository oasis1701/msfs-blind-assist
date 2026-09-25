using MSFSBlindAssist.Aircraft.Learjet35;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Structural checks for the Flysimware Learjet 35A panel tree.
///
/// The load-bearing one is <see cref="EveryPanelHasAControlsEntry"/>: MainForm's panel build
/// returns early for any panel missing from GetPanelControls(), so a panel named in
/// GetPanelStructure() but absent from BuildPanelControls() renders COMPLETELY BLANK with no
/// error anywhere. That trap cost the HS787 seven empty Flight Data panels, and it is silent —
/// only a test catches it.
/// </summary>
public class Lj35PanelStructureTests
{
    private static FlysimwareLearjet35ADefinition Def() => new();

    [Fact]
    public void EveryPanelHasAControlsEntry()
    {
        var def = Def();
        var controls = def.GetPanelControls();
        var missing = def.GetPanelStructure().SelectMany(s => s.Value)
            .Where(p => !controls.ContainsKey(p)).ToList();
        Assert.True(missing.Count == 0, "These panels would render blank: " + string.Join(", ", missing));
    }

    [Fact]
    public void PanelNamesAreUniqueAcrossSections()
    {
        // Panel names key a flat dictionary, so a duplicate in two sections silently collapses
        // into one entry and one of the two sections loses its panel.
        var all = Def().GetPanelStructure().SelectMany(s => s.Value).ToList();
        var dupes = all.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dupes.Count == 0, "Duplicate panel names: " + string.Join(", ", dupes));
    }

    [Fact]
    public void NoSectionIsEmpty()
    {
        var empty = Def().GetPanelStructure().Where(s => s.Value.Count == 0).Select(s => s.Key).ToList();
        Assert.True(empty.Count == 0, "Empty sections: " + string.Join(", ", empty));
    }

    [Fact]
    public void SectionsFollowTheVendorManual()
    {
        // The vendor manual's own cockpit map (LEARJET_35A_MSFS_MANUAL pages 1-2), in its order.
        var expected = new[]
        {
            "Glareshield", "Pilot Panel", "Copilot Panel", "Engine Panel", "Navigation Panel",
            "Pilot's Sidewall", "Copilot's Sidewall", "Audio", "Anti-Ice and Fuel Computer Panel",
            "Start Panel", "Test Panel", "Lower Center Panel", "Pressurization Panel",
            "Climate and Lights Panel", "Throttle Quadrant", "Fuel System", "Center Pedestal",
            "Yoke", "EFB Tablet", "Cabin and Ground", "Simulation"
        };
        Assert.Equal(expected, Def().GetPanelStructure().Keys.ToArray());
    }

    [Fact]
    public void IdentityIsReported()
    {
        Assert.Equal("FLYSIMWARE_LJ35A", Def().AircraftCode);
        Assert.Equal("Flysimware Learjet 35A", Def().AircraftName);
    }

    /// <summary>
    /// A panel is EITHER built OR on this list, never silently empty. A panel with no controls and
    /// no status rows renders blank, and a blank panel is indistinguishable from a broken one.
    /// Building a panel takes it off the list; the mirror assertion fails when a built panel is
    /// still listed, so the list cannot go stale in either direction.
    /// </summary>
    public static readonly string[] NotBuiltYet = Array.Empty<string>();

    [Fact]
    public void EveryPanelInTheStructureIsBuiltOrKnownUnbuilt()
    {
        var def = Def();
        var controls = def.GetPanelControls();
        var display = def.GetPanelDisplayVariables();
        var silentlyEmpty = def.GetPanelStructure().SelectMany(s => s.Value)
            .Where(p => (!controls.TryGetValue(p, out var c) || c.Count == 0)
                        && (!display.TryGetValue(p, out var d) || d.Count == 0)
                        && !NotBuiltYet.Contains(p))
            .ToList();
        Assert.True(silentlyEmpty.Count == 0,
            "Empty and not on NotBuiltYet: " + string.Join(", ", silentlyEmpty));
    }

    [Fact]
    public void NothingOnTheNotBuiltListIsActuallyBuilt()
    {
        var def = Def();
        var controls = def.GetPanelControls();
        var display = def.GetPanelDisplayVariables();
        var stale = NotBuiltYet.Where(p =>
            (controls.TryGetValue(p, out var c) && c.Count > 0) ||
            (display.TryGetValue(p, out var d) && d.Count > 0)).ToList();
        Assert.True(stale.Count == 0, "Built but still listed as not built: " + string.Join(", ", stale));
    }

    [Fact]
    public void EveryPanelKeyResolvesToARegisteredVariable()
    {
        // A control listed in a panel but missing from GetVariables() renders as a blank row.
        var def = Def();
        var vars = def.GetVariables();
        var missing = def.GetPanelControls().SelectMany(p => p.Value)
            .Concat(def.GetPanelDisplayVariables().SelectMany(p => p.Value))
            .Where(k => !vars.ContainsKey(k)).Distinct().ToList();
        Assert.True(missing.Count == 0, "Panel keys with no variable: " + string.Join(", ", missing));
    }

    [Fact]
    public void NoControlAppearsInTwoPanels()
    {
        var seen = new Dictionary<string, string>();
        var dupes = new List<string>();
        foreach (var (panel, keys) in Def().GetPanelControls())
            foreach (var k in keys)
            {
                if (seen.TryGetValue(k, out var other)) dupes.Add($"{k} ({other} and {panel})");
                else seen[k] = panel;
            }
        Assert.True(dupes.Count == 0, "Controls in two panels: " + string.Join(", ", dupes));
    }
}
