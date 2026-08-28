using MSFSBlindAssist.Aircraft;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Characterization tests for the PMDG 777 hydraulic pump labels.
///
/// These exist because the labels encode a hardware fact a blind pilot cannot
/// check against a placard: which hydraulic system each pump serves. The 777
/// puts BOTH electric primary pumps and BOTH air demand pumps on the CENTER
/// system, so only the engine primaries and the electric demands are a
/// left/right pair. Getting that wrong does not fail loudly - it produces a
/// switch that confidently names the wrong system.
///
/// The comment on GetPMDGVariables() records why; this file is what makes a
/// renumber fail the build rather than reach a cockpit. Same division of labour
/// as Pmdg777StabTrimTests ("Why ... lives on the class doc - this file only
/// pins the behaviour").
/// </summary>
public class Pmdg777HydraulicPumpLabelTests
{
    private const string FaultSuffix = " FAULT Light";

    /// <summary>varKey, PMDG struct field, spoken label, FAULT-light varKey.</summary>
    public static TheoryData<string, string, string, string> Pumps() => new()
    {
        // PRIMARY row, left to right: L ENG, C1 ELEC, C2 ELEC, R ENG.
        { "HYD_PrimEngPump_1",    "HYD_PrimaryEngPump_Sw_ON_0",   "Primary Engine Pump Left",      "HYD_annunPrimEngPumpFAULT_1" },
        { "HYD_PrimElecPump_1",   "HYD_PrimaryElecPump_Sw_ON_0",  "Primary Electric Pump Center 1","HYD_annunPrimElecPumpFAULT_1" },
        { "HYD_PrimElecPump_2",   "HYD_PrimaryElecPump_Sw_ON_1",  "Primary Electric Pump Center 2","HYD_annunPrimElecPumpFAULT_2" },
        { "HYD_PrimEngPump_2",    "HYD_PrimaryEngPump_Sw_ON_1",   "Primary Engine Pump Right",     "HYD_annunPrimEngPumpFAULT_2" },

        // DEMAND row, left to right: L ELEC, C1 AIR, C2 AIR, R ELEC.
        { "HYD_DemandElecPump_1", "HYD_DemandElecPump_Selector_0","Demand Electric Pump Left",     "HYD_annunDemandElecPumpFAULT_1" },
        { "HYD_DemandAirPump_1",  "HYD_DemandAirPump_Selector_0", "Demand Air Pump Center 1",      "HYD_annunDemandAirPumpFAULT_1" },
        { "HYD_DemandAirPump_2",  "HYD_DemandAirPump_Selector_1", "Demand Air Pump Center 2",      "HYD_annunDemandAirPumpFAULT_2" },
        { "HYD_DemandElecPump_2", "HYD_DemandElecPump_Selector_1","Demand Electric Pump Right",    "HYD_annunDemandElecPumpFAULT_2" },
    };

    [Theory]
    [MemberData(nameof(Pumps))]
    public void Pump_switch_pins_its_struct_field_and_spoken_label(
        string varKey, string structField, string label, string faultKey)
    {
        _ = faultKey;
        var vars = new PMDG777Definition().GetVariables();

        Assert.True(vars.ContainsKey(varKey), $"missing hydraulic pump var {varKey}");
        Assert.Equal(structField, vars[varKey].Name);
        Assert.Equal(label, vars[varKey].DisplayName);
    }

    /// <summary>
    /// The annunciator must carry its switch's name verbatim. The two labels are
    /// spoken through different channels - the switch from the Hydraulic panel,
    /// the FAULT light only as a background announcement - so a drift between
    /// them surfaces as a FAULT for a pump the panel appears not to have. This
    /// file already contains one such drift: "Isolation Valve Left" against
    /// "Isolation Valve L CLOSED Light".
    /// </summary>
    [Theory]
    [MemberData(nameof(Pumps))]
    public void Fault_light_label_is_derived_from_its_switch_label(
        string varKey, string structField, string label, string faultKey)
    {
        _ = structField;
        var vars = new PMDG777Definition().GetVariables();

        Assert.True(vars.ContainsKey(faultKey), $"missing FAULT light var {faultKey}");
        Assert.Equal(label + FaultSuffix, vars[faultKey].DisplayName);
        Assert.Equal(vars[varKey].DisplayName + FaultSuffix, vars[faultKey].DisplayName);
    }

    /// <summary>
    /// The structural half, and the one that catches a genuine mistake rather
    /// than a diff: a pump on the CENTER system must never be named after a
    /// side, and a sided pump must never be named "Center". This is what fails
    /// if someone renumbers the labels back to a bare 1/2, or applies a
    /// left-to-right event-id ordering to the array and swaps the pairs.
    /// </summary>
    [Theory]
    [MemberData(nameof(Pumps))]
    public void Center_system_pumps_are_never_named_after_a_side(
        string varKey, string structField, string label, string faultKey)
    {
        _ = structField;
        _ = faultKey;
        var vars = new PMDG777Definition().GetVariables();
        string spoken = vars[varKey].DisplayName;

        // The center system carries both electric primaries and both air demands.
        bool onCenterSystem = varKey is "HYD_PrimElecPump_1" or "HYD_PrimElecPump_2"
                                     or "HYD_DemandAirPump_1" or "HYD_DemandAirPump_2";

        if (onCenterSystem)
        {
            Assert.Contains("Center", spoken);
            Assert.DoesNotContain("Left", spoken);
            Assert.DoesNotContain("Right", spoken);
        }
        else
        {
            Assert.DoesNotContain("Center", spoken);
            Assert.True(spoken.Contains("Left") || spoken.Contains("Right"),
                $"{varKey} serves the left or right system, so its label must say which: got \"{spoken}\"");

            // A sided pump ending in a bare index has lost its side - the exact
            // regression this family exists to prevent. ("Center 1"/"Center 2"
            // are fine: there the digit is qualified by the system name.)
            Assert.False(spoken.EndsWith(" 1") || spoken.EndsWith(" 2"),
                $"{varKey} must name its side, not a bare index: got \"{spoken}\"");
        }

        _ = label;
    }

    /// <summary>
    /// The Hydraulic panel list is the tab order (MainForm.PanelBuilder iterates
    /// it directly, unsorted). Because the labels name positions, the traversal
    /// has to trace the real rows or the two contradict each other.
    /// </summary>
    [Fact]
    public void Hydraulic_panel_tab_order_traces_the_physical_rows()
    {
        var panel = new PMDG777Definition().GetPanelControls()["Hydraulic"];

        Assert.Equal(new[]
        {
            "HYD_PrimEngPump_1", "HYD_PrimElecPump_1", "HYD_PrimElecPump_2", "HYD_PrimEngPump_2",
            "HYD_DemandElecPump_1", "HYD_DemandAirPump_1", "HYD_DemandAirPump_2", "HYD_DemandElecPump_2",
            "HYD_RAT",
        }, panel);
    }
}
