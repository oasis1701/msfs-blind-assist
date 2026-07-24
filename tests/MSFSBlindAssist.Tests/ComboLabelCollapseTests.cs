// Characterization tests for combo-label collapsing (MainForm.PanelBuilder generic
// combo path + Aircraft/IFly737MAXDefinition.ForwardPedestal.cs).
//
// A composite-encoded SDK field can fold a redundant indicator bit onto a switch
// position (the iFly ACP receiver switches: raw 0-2 deselected × lamp, 3-5
// selected × lamp, where the lamp is the key's own backlight — lit exactly when
// selected, brightness slaved to the master LIGHTS TEST/BRT/DIM switch, so it
// carries no independent information; live-verified 2026-07-24). Such a field
// collapses to fewer combo positions by giving several dictionary keys the SAME
// label: the panel builder adds each distinct label once and resolves a pick to
// its FIRST key in display order, while every raw key stays registered so the
// value→label read-back always resolves.
//
// These tests pin (a) the iFly receiver dictionaries' collapsed shape, and
// (b) a fleet-wide inventory: label collapse is DELIBERATE, so any combo-rendered
// panel var whose dictionary duplicates a label must be listed here. If a new
// duplicate appears unintentionally, this test fails — before the collapse
// support existed, a duplicate label silently corrupted the combo's write mapping
// (items indexed positionally into the sorted dictionary), so an accidental one
// is a real bug, not a cosmetic issue.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class ComboLabelCollapseTests
{
    private static readonly string[] RvcChannels =
    {
        "VHF1", "VHF2", "VHF3", "HF1", "HF2", "INT", "CABIN", "PA",
        "NAV1", "NAV2", "ADF1", "ADF2", "MKR", "SAT1", "SAT2", "SPKR",
    };

    [Fact]
    public void IFly_acp_receiver_switches_collapse_to_two_positions_over_all_six_raw_keys()
    {
        var vars = new IFly737MAXDefinition().GetVariables();
        foreach (var channel in RvcChannels)
        {
            for (int unit = 0; unit < 3; unit++)
            {
                string key = $"ACP_RVC_{channel}_Switch_Status_{unit}";
                Assert.True(vars.ContainsKey(key), $"missing receiver var {key}");
                var d = vars[key].ValueDescriptions;

                // All six raw encodings must stay registered — the combo read-back
                // does an exact key lookup, and a selected key reads 3/4/5 depending
                // on the master panel-light switch.
                Assert.Equal(new double[] { 0, 1, 2, 3, 4, 5 }, d.Keys.OrderBy(k => k));

                // Raw 0-2 (deselected × lamp) collapse to one label, 3-5 to the other.
                foreach (var raw in new double[] { 0, 1, 2 }) Assert.Equal("Deselected", d[raw]);
                foreach (var raw in new double[] { 3, 4, 5 }) Assert.Equal("Selected", d[raw]);
            }
        }
    }

    // Every var that PanelBuilder would render as an interactive combo, fleet-wide,
    // whose ValueDescriptions contain a duplicated label. Keyed "AircraftCode/varKey".
    private static readonly HashSet<string> DeliberateCollapses = new(
        RvcChannels.SelectMany(c => Enumerable.Range(0, 3)
            .Select(u => $"IFLY_737MAX8/ACP_RVC_{c}_Switch_Status_{u}")));

    public static IEnumerable<object[]> AllAircraft()
    {
        yield return new object[] { new FenixA320Definition() };
        yield return new object[] { new FlyByWireA320Definition() };
        yield return new object[] { new FlyByWireA380Definition() };
        yield return new object[] { new HeadwindA330Definition() };
        yield return new object[] { new HorizonSim787Definition() };
        yield return new object[] { new IFly737MAXDefinition() };
        yield return new object[] { new PMDG737Definition() };
        yield return new object[] { new PMDG777Definition() };
    }

    [Theory]
    [MemberData(nameof(AllAircraft))]
    public void Duplicate_combo_labels_are_deliberate_collapses_only(IAircraftDefinition aircraft)
    {
        var vars = aircraft.GetVariables();
        var panelKeys = aircraft.GetPanelControls().Values.SelectMany(k => k).ToHashSet();

        foreach (var key in panelKeys)
        {
            if (!vars.TryGetValue(key, out var def)) continue;

            // Mirror PanelBuilder's control-type decision chain: slider, button,
            // and both read-only flavors are checked before the combo branch.
            bool rendersAsCombo =
                !def.RenderAsSlider &&
                !def.RenderAsButton &&
                !def.RenderAsReadOnlyStatus &&
                !def.OnlyAnnounceValueDescriptionMatches &&
                def.ValueDescriptions != null && def.ValueDescriptions.Count > 1;
            if (!rendersAsCombo) continue;

            bool hasDuplicateLabels =
                def.ValueDescriptions!.Values.Distinct().Count() < def.ValueDescriptions.Count;
            string id = $"{aircraft.AircraftCode}/{key}";
            if (hasDuplicateLabels)
                Assert.True(DeliberateCollapses.Contains(id),
                    $"{id} duplicates a combo label but is not a documented deliberate collapse");
            else
                Assert.False(DeliberateCollapses.Contains(id),
                    $"{id} is listed as a deliberate collapse but its labels are unique");
        }
    }
}
